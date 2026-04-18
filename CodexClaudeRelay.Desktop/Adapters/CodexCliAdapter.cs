using System.IO;
using System.Text.Json;
using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Broker;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Policy;
using CodexClaudeRelay.Core.Protocol;

namespace CodexClaudeRelay.Desktop.Adapters;

internal sealed class CodexCliAdapter : IRelayAdapter
{
    private const string ProbePrompt = "Return exactly the word ok.";

    private readonly ProcessCommandRunner _runner;
    private readonly string _workingDirectory;
    private readonly CodexCommandSpec _command;
    // Resolved once at construction. Stale if the operator edits config.toml
    // mid-session; not re-read per turn to avoid I/O on the hot path.
    private readonly string? _configuredModel;

    public CodexCliAdapter(
        string workingDirectory,
        string command = "codex",
        RelayJobObjectOptions? jobObjectOptions = null)
    {
        _runner = new ProcessCommandRunner(jobObjectOptions);
        _workingDirectory = workingDirectory;
        _command = ResolveCodexCommand(command);
        _configuredModel = CodexModelResolver.TryResolveConfiguredModel();
    }

    public string Role => AgentRole.Codex;

    public async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var versionResult = await _runner.RunAsync(
            _command.FileName,
            BuildCommandArguments(["--version"]),
            _workingDirectory,
            cancellationToken);
        if (versionResult.ExitCode != 0)
        {
            return new AdapterStatus(
                RelayHealthStatus.Unavailable,
                false,
                string.IsNullOrWhiteSpace(versionResult.StandardError)
                    ? "codex --version failed."
                    : versionResult.StandardError);
        }

        try
        {
            var probeArgs = BuildCommandArguments(
            [
                "exec",
                "--json",
                "--skip-git-repo-check",
                "-C",
                _workingDirectory,
                ProbePrompt,
            ]);

            var probeResult = await ExecuteAndParseAsync(probeArgs, cancellationToken);
            var warningSummary = SummarizeWarnings(probeResult.Diagnostics);
            var message = string.IsNullOrWhiteSpace(warningSummary)
                ? "codex exec probe ok"
                : $"codex exec probe ok; warnings: {warningSummary}";

            return new AdapterStatus(RelayHealthStatus.Healthy, true, message);
        }
        catch (Exception ex)
        {
            var health = versionResult.ExitCode == 0 ? RelayHealthStatus.Degraded : RelayHealthStatus.Unavailable;
            return new AdapterStatus(health, false, $"codex exec probe failed: {ex.Message}");
        }
    }

    public async Task<RelayAdapterResult> RunTurnAsync(RelayTurnContext context, CancellationToken cancellationToken)
    {
        var prompt = RelayPromptBuilder.BuildTurnPrompt(context);
        var args = BuildCommandArguments(
        [
            "exec",
            "--json",
            "--skip-git-repo-check",
            "-C",
            _workingDirectory,
            prompt,
        ]);
        return await ExecuteAndParseAsync(args, cancellationToken);
    }

    public async Task<RelayAdapterResult> RunRepairAsync(RelayRepairContext context, CancellationToken cancellationToken)
    {
        var prompt = RelayPromptBuilder.BuildRepairPrompt(context);
        if (string.IsNullOrWhiteSpace(context.ExistingSessionHandle))
        {
            var firstTurnArgs = BuildCommandArguments(
            [
                "exec",
                "--json",
                "--skip-git-repo-check",
                "-C",
                _workingDirectory,
                prompt,
            ]);
            return await ExecuteAndParseAsync(firstTurnArgs, cancellationToken);
        }

        var args = BuildCommandArguments(
        [
            "exec",
            "resume",
            "--json",
            "--skip-git-repo-check",
            context.ExistingSessionHandle,
            prompt,
        ]);

        return await ExecuteAndParseAsync(args, cancellationToken);
    }

    private async Task<RelayAdapterResult> ExecuteAndParseAsync(
        IEnumerable<string> args,
        CancellationToken cancellationToken)
    {
        var outputFilePath = Path.Combine(Path.GetTempPath(), $"relay-codex-{Guid.NewGuid():N}.txt");
        var argumentList = args.ToList();

        argumentList.Add("-o");
        argumentList.Add(outputFilePath);

        try
        {
            var result = await _runner.RunAsync(_command.FileName, argumentList, _workingDirectory, cancellationToken);
            string? threadId = null;
            RelayUsageMetrics? usage = null;
            var observedActions = new List<RelayObservedAction>();
            var finalText = File.Exists(outputFilePath)
                ? File.ReadAllText(outputFilePath).Trim()
                : null;
            var stdoutDiagnostics = new List<string>();

            foreach (var rawLine in result.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!LooksLikeJsonObject(line))
                {
                    stdoutDiagnostics.Add($"non-json stdout: {line}");
                    continue;
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException ex)
                {
                    stdoutDiagnostics.Add($"invalid-json stdout: {ex.Message} | {Shorten(line, 240)}");
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    var type = root.TryGetProperty("type", out var typeElement)
                        ? typeElement.GetString()
                        : null;
                    TryCaptureObservedAction(root, type, observedActions);
                    if (TryReadCodexExecUsage(root, type, out var usageElement))
                    {
                        var parsedUsage = RelayUsageMetrics.FromCodexExecUsage(usageElement);
                        if (parsedUsage is not null)
                        {
                            // DAD-v2 reset: Codex-specific pricing stripped (peer-symmetric protocol).
                            // Cost is 0 locally; agents can surface their own usage via telemetry if needed.
                            var pricingFallbackReason = (string?)null;
                            usage = parsedUsage with
                            {
                                Model = _configuredModel,
                                CostUsd = 0.0,
                                PricingFallbackReason = pricingFallbackReason
                            };

                            if (!string.IsNullOrWhiteSpace(pricingFallbackReason))
                            {
                                stdoutDiagnostics.Add(pricingFallbackReason);
                            }
                        }
                    }

                    if (type == "thread.started" && root.TryGetProperty("thread_id", out var threadElement))
                    {
                        threadId = threadElement.GetString();
                    }
                    else if (string.IsNullOrWhiteSpace(finalText) &&
                             type == "item.completed" &&
                             root.TryGetProperty("item", out var itemElement) &&
                             itemElement.TryGetProperty("type", out var itemTypeElement) &&
                             string.Equals(itemTypeElement.GetString(), "agent_message", StringComparison.Ordinal) &&
                             itemElement.TryGetProperty("text", out var textElement))
                    {
                        finalText = textElement.GetString();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(finalText))
            {
                throw new InvalidOperationException(
                    $"Codex did not produce a final message.{Environment.NewLine}stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}");
            }

            var diagnostics = JoinDiagnostics(result.StandardError, stdoutDiagnostics);
            return new RelayAdapterResult(finalText, threadId, diagnostics, usage, UsageIsCumulative: true, ObservedActions: observedActions);
        }
        finally
        {
            if (File.Exists(outputFilePath))
            {
                File.Delete(outputFilePath);
            }
        }
    }

    private static string SummarizeWarnings(string? diagnostics)
    {
        if (string.IsNullOrWhiteSpace(diagnostics))
        {
            return string.Empty;
        }

        var firstLine = diagnostics
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return string.Empty;
        }

        return firstLine.Length <= 140 ? firstLine : $"{firstLine[..140]}...";
    }

    private static string JoinDiagnostics(string? standardError, IReadOnlyCollection<string> stdoutDiagnostics)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(standardError))
        {
            parts.Add(standardError.Trim());
        }

        if (stdoutDiagnostics.Count > 0)
        {
            parts.AddRange(stdoutDiagnostics);
        }

        return string.Join(Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static bool TryReadCodexExecUsage(JsonElement root, string? type, out JsonElement usageElement)
    {
        usageElement = default;

        // codex exec --json emits flat typed events on stdout.
        // Usage appears on turn.completed as snake_case fields:
        // {"type":"turn.completed","usage":{"input_tokens":...,"cached_input_tokens":...,"output_tokens":...}}
        if (string.Equals(type, "turn.completed", StringComparison.Ordinal) &&
            root.TryGetProperty("usage", out var usageObject) &&
            usageObject.ValueKind == JsonValueKind.Object)
        {
            usageElement = usageObject.Clone();
            return true;
        }

        return false;
    }

    private static void TryCaptureObservedAction(
        JsonElement root,
        string? type,
        ICollection<RelayObservedAction> observedActions)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return;
        }

        if (type == "item.started" || type == "item.completed")
        {
            var itemType = TryReadExecItemType(root);
            if (string.Equals(itemType, "agent_message", StringComparison.Ordinal))
            {
                return;
            }

            var category = RelayApprovalPolicy.ClassifyCodexItemCategory(itemType);
            var title = RelayApprovalPolicy.GetToolTitle(category, itemType);
            var eventType = type == "item.started" ? "tool.invoked" : "tool.completed";
            var message = $"Codex exec {type} ({itemType ?? "unknown-item"}).";
            var summary = RelayApprovalPolicy.DescribeToolSummary(category, itemType);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                message = $"{message}{Environment.NewLine}{summary}";
            }

            observedActions.Add(new RelayObservedAction(
                eventType,
                message,
                root.GetRawText(),
                category,
                title));

            var categoryEventType = RelayApprovalPolicy.GetCategoryEventType(
                category,
                eventType switch
                {
                    "tool.invoked" => "requested",
                    "tool.completed" => "completed",
                    _ => string.Empty
                });
            if (!string.IsNullOrWhiteSpace(categoryEventType))
            {
                observedActions.Add(new RelayObservedAction(
                    categoryEventType,
                    message,
                    root.GetRawText(),
                    category,
                    title));
            }
            return;
        }

        if (type == "turn.failed" || type == "error")
        {
            observedActions.Add(new RelayObservedAction(
                "tool.failed",
                $"Codex exec reported '{type}'.",
                root.GetRawText()));
            return;
        }

        if (type == "thread.started" || type == "turn.started" || type == "turn.completed")
        {
            observedActions.Add(new RelayObservedAction(
                "adapter.event",
                $"Codex exec event '{type}'.",
                root.GetRawText()));
        }
    }

    private static string? TryReadExecItemType(JsonElement root)
    {
        if (!root.TryGetProperty("item", out var itemElement) ||
            itemElement.ValueKind != JsonValueKind.Object ||
            !itemElement.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return typeElement.GetString();
    }

    private static bool LooksLikeJsonObject(string line) =>
        line.StartsWith('{') && line.EndsWith('}');

    private static string Shorten(string text, int maxLength) =>
        text.Length <= maxLength ? text : $"{text[..maxLength]}...";

    private static string GetPeer(string role) =>
        role == AgentRole.Codex ? AgentRole.Claude : AgentRole.Codex;

    private IEnumerable<string> BuildCommandArguments(IEnumerable<string> args) =>
        _command.PrefixArguments.Concat(args);

    private static CodexCommandSpec ResolveCodexCommand(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            var normalized = NormalizeWindowsCommand(command);
            if (normalized is not null)
            {
                return normalized;
            }
        }

        if (!string.Equals(command, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return new CodexCommandSpec(command, []);
        }

        var preferredPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.ps1"),
        };

        var resolved = preferredPaths.FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return new CodexCommandSpec(resolved, []);
        }

        return new CodexCommandSpec("codex.cmd", []);
    }

    private static CodexCommandSpec? NormalizeWindowsCommand(string command)
    {
        if (string.Equals(command, "codex", StringComparison.OrdinalIgnoreCase))
        {
            var nodeCommand = TryResolveNodeCodexCommand();
            if (nodeCommand is not null)
            {
                return nodeCommand;
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var codexCmd = Path.Combine(appData, "npm", "codex.cmd");
            if (File.Exists(codexCmd))
            {
                return new CodexCommandSpec(codexCmd, []);
            }

            var codexPs1 = Path.Combine(appData, "npm", "codex.ps1");
            if (File.Exists(codexPs1))
            {
                return WrapPowerShellScript(codexPs1);
            }

            return new CodexCommandSpec("codex.cmd", []);
        }

        if (command.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return WrapPowerShellScript(command);
        }

        return null;
    }

    private static CodexCommandSpec? TryResolveNodeCodexCommand()
    {
        var nodePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "nodejs",
            "node.exe");
        var codexJsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm",
            "node_modules",
            "@openai",
            "codex",
            "bin",
            "codex.js");

        if (!File.Exists(nodePath) || !File.Exists(codexJsPath))
        {
            return null;
        }

        // Prefer calling node + codex.js directly on Windows so the desktop app
        // does not rely on cmd.exe reparsing npm wrapper scripts around a free-form prompt.
        return new CodexCommandSpec(nodePath, [codexJsPath]);
    }

    private static CodexCommandSpec WrapPowerShellScript(string scriptPath)
    {
        var powershellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        return new CodexCommandSpec(
            powershellPath,
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath]);
    }

    private sealed record CodexCommandSpec(string FileName, IReadOnlyList<string> PrefixArguments);
}
