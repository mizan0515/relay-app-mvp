using RelayApp.Core.Adapters;
using RelayApp.Core.Broker;
using RelayApp.Core.Models;
using RelayApp.Core.Policy;
using RelayApp.Core.Protocol;
using RelayApp.Desktop.Adapters;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RelayApp.Desktop.Interactive;

internal sealed class ClaudeInteractiveAdapter : IRelayAdapter, IAsyncDisposable
{
    private const string ProbePrompt = "Return exactly the word ok.";

    private readonly ProcessCommandRunner _runner;
    private readonly string _workingDirectory;
    private readonly string _command;
    private string? _cachedCliVersion;
    private string? _cachedAuthMethod;

    public ClaudeInteractiveAdapter(
        string workingDirectory,
        string command = "claude",
        RelayJobObjectOptions? jobObjectOptions = null)
    {
        _runner = new ProcessCommandRunner(jobObjectOptions);
        _workingDirectory = workingDirectory;
        _command = command;
    }

    public RelaySide Side => RelaySide.Claude;

    public async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var hasLongLivedToken = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN"));
        var authStatus = await _runner.RunAsync(_command, ["auth", "status"], _workingDirectory, cancellationToken);
        var authLooksLoggedIn = authStatus.ExitCode == 0 &&
                                authStatus.StandardOutput.Contains("\"loggedIn\": true", StringComparison.OrdinalIgnoreCase);
        var tokenSummary = hasLongLivedToken
            ? "CLAUDE_CODE_OAUTH_TOKEN detected"
            : "using /login subscription OAuth";

        RelayAdapterResult probeResult;
        try
        {
            probeResult = await ExecuteAsync(
                ProbePrompt,
                Guid.NewGuid().ToString(),
                resumeExisting: false,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return new AdapterStatus(
                authLooksLoggedIn ? RelayHealthStatus.Degraded : RelayHealthStatus.Unavailable,
                false,
                $"{tokenSummary}; auth status {(authLooksLoggedIn ? "ok" : "unclear")}; stream-json probe failed: {ex.Message}");
        }

        var probeOk = string.Equals(probeResult.Output.Trim(), "ok", StringComparison.OrdinalIgnoreCase);
        var health = probeOk
            ? RelayHealthStatus.Healthy
            : authLooksLoggedIn
                ? RelayHealthStatus.Degraded
                : RelayHealthStatus.Unavailable;

        return new AdapterStatus(
            health,
            (authLooksLoggedIn || hasLongLivedToken) && probeOk,
            $"{tokenSummary}; auth status {(authLooksLoggedIn ? "ok" : "unclear")}; stream-json probe {(probeOk ? "ok" : "did not return ok marker")}");
    }

    public Task<RelayAdapterResult> RunTurnAsync(RelayTurnContext context, CancellationToken cancellationToken)
    {
        var prompt = RelayPromptBuilder.BuildInteractiveTurnPrompt(context);
        var sessionHandle = string.IsNullOrWhiteSpace(context.ExistingSessionHandle)
            ? Guid.NewGuid().ToString()
            : context.ExistingSessionHandle!;

        return ExecuteAsync(prompt, sessionHandle, !string.IsNullOrWhiteSpace(context.ExistingSessionHandle), cancellationToken);
    }

    public Task<RelayAdapterResult> RunRepairAsync(RelayRepairContext context, CancellationToken cancellationToken)
    {
        var prompt = RelayPromptBuilder.BuildInteractiveRepairPrompt(context);
        var sessionHandle = string.IsNullOrWhiteSpace(context.ExistingSessionHandle)
            ? Guid.NewGuid().ToString()
            : context.ExistingSessionHandle!;

        return ExecuteAsync(prompt, sessionHandle, !string.IsNullOrWhiteSpace(context.ExistingSessionHandle), cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<RelayAdapterResult> ExecuteAsync(
        string prompt,
        string sessionHandle,
        bool resumeExisting,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "-p",
            prompt,
            "--output-format",
            "stream-json",
            "--verbose",
        };

        if (resumeExisting)
        {
            args.Add("--resume");
            args.Add(sessionHandle);
        }
        else
        {
            args.Add("--session-id");
            args.Add(sessionHandle);
        }

        var invocation = await _runner.RunAsync(_command, args, _workingDirectory, cancellationToken);
        if (string.IsNullOrWhiteSpace(invocation.StandardOutput))
        {
            throw new InvalidOperationException($"Claude stream-json produced no stdout. stderr: {invocation.StandardError}");
        }

        var cliVersion = await GetCliVersionAsync(cancellationToken);
        var parsed = ParseStreamJson(invocation.StandardOutput);
        if (!parsed.SawResultLine)
        {
            throw new InvalidOperationException(
                $"Claude stream-json did not emit a terminal result line. Turn is considered failed. Parse warnings: {parsed.ParseWarnings ?? "(none)"}{Environment.NewLine}stderr: {invocation.StandardError}");
        }

        if (parsed.IsError)
        {
            throw new InvalidOperationException(
                $"Claude stream-json returned an error result. Output: {parsed.ResultText}{Environment.NewLine}stderr: {invocation.StandardError}");
        }

        var output = !string.IsNullOrWhiteSpace(parsed.ResultText)
            ? parsed.ResultText!
            : !string.IsNullOrWhiteSpace(parsed.AssistantText)
                ? parsed.AssistantText!
                : invocation.StandardOutput;

        var resolvedSessionHandle = !string.IsNullOrWhiteSpace(parsed.SessionId)
            ? parsed.SessionId!
            : sessionHandle;

        var usage = parsed.Usage;
        if (usage is not null)
        {
            usage = usage with
            {
                CliVersion = cliVersion,
                AuthMethod = await GetAuthMethodAsync(cancellationToken)
            };
        }

        return new RelayAdapterResult(
            output,
            resolvedSessionHandle,
            BuildDiagnostics(invocation.StandardError, parsed),
            usage,
            ObservedActions: parsed.ObservedActions);
    }

    private static ParsedClaudeStreamResult ParseStreamJson(string stdout)
    {
        string? sessionId = null;
        string? assistantText = null;
        string? resultText = null;
        RelayUsageMetrics? usage = null;
        var isError = false;
        string? assistantModel = null;
        string? modelUsageSummary = null;
        var sawResultLine = false;
        var parseWarnings = new List<string>();
        var observedActions = new List<RelayObservedAction>();

        foreach (var rawLine in stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith('{'))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                parseWarnings.Add($"Skipped malformed stream-json line ({ex.Message}): {Shorten(line, 160)}");
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString()
                    : null;
                CaptureObservedAction(root, type, observedActions);

                if (root.TryGetProperty("session_id", out var sessionElement) && sessionElement.ValueKind == JsonValueKind.String)
                {
                    sessionId = sessionElement.GetString();
                }

                if (type == "assistant" &&
                    root.TryGetProperty("message", out var messageElement) &&
                    messageElement.TryGetProperty("content", out var contentElement) &&
                    contentElement.ValueKind == JsonValueKind.Array)
                {
                    if (messageElement.TryGetProperty("model", out var messageModelElement) &&
                        messageModelElement.ValueKind == JsonValueKind.String)
                    {
                        assistantModel = messageModelElement.GetString();
                    }

                    assistantText = ExtractAssistantText(contentElement);
                    continue;
                }

                if (type == "result")
                {
                    sawResultLine = true;
                    usage = MergeUsage(usage, RelayUsageMetrics.FromClaudeJson(root));
                    modelUsageSummary ??= SummarizeClaudeModelUsage(root);

                    if (root.TryGetProperty("is_error", out var isErrorElement) &&
                        (isErrorElement.ValueKind == JsonValueKind.True || isErrorElement.ValueKind == JsonValueKind.False))
                    {
                        isError = isErrorElement.GetBoolean();
                    }

                    if (root.TryGetProperty("result", out var resultElement) && resultElement.ValueKind == JsonValueKind.String)
                    {
                        resultText = resultElement.GetString();
                    }
                }
            }
        }

        if (usage is not null && string.IsNullOrWhiteSpace(usage.Model) && !string.IsNullOrWhiteSpace(assistantModel))
        {
            usage = usage with { Model = assistantModel };
        }

        return new ParsedClaudeStreamResult(
            sessionId,
            assistantText,
            resultText,
            usage,
            isError,
            modelUsageSummary,
            parseWarnings.Count == 0 ? null : string.Join(Environment.NewLine, parseWarnings),
            sawResultLine,
            observedActions);
    }

    private static string? ExtractAssistantText(JsonElement contentElement)
    {
        var builder = new StringBuilder();

        foreach (var item in contentElement.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var itemTypeElement) ||
                itemTypeElement.ValueKind != JsonValueKind.String ||
                !string.Equals(itemTypeElement.GetString(), "text", StringComparison.Ordinal))
            {
                continue;
            }

            if (!item.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            builder.Append(textElement.GetString());
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static RelayUsageMetrics? MergeUsage(RelayUsageMetrics? existing, RelayUsageMetrics? incoming)
    {
        if (existing is null)
        {
            return incoming;
        }

        if (incoming is null)
        {
            return existing;
        }

        return new RelayUsageMetrics(
            incoming.InputTokens ?? existing.InputTokens,
            incoming.OutputTokens ?? existing.OutputTokens,
            incoming.CacheCreationInputTokens ?? existing.CacheCreationInputTokens,
            incoming.CacheReadInputTokens ?? existing.CacheReadInputTokens,
            incoming.CostUsd ?? existing.CostUsd,
            incoming.RawJson ?? existing.RawJson,
            incoming.Model ?? existing.Model,
            incoming.ModelUsageUsd ?? existing.ModelUsageUsd,
            incoming.PricingFallbackReason ?? existing.PricingFallbackReason,
            incoming.CliVersion ?? existing.CliVersion,
            incoming.AuthMethod ?? existing.AuthMethod);
    }

    private static string BuildDiagnostics(string standardError, ParsedClaudeStreamResult parsed)
    {
        var builder = new StringBuilder();
        builder.AppendLine("transport=claude-stream-json");
        builder.AppendLine($"session_id={parsed.SessionId ?? "(none)"}");
        builder.AppendLine($"assistant_text={parsed.AssistantText ?? "(none)"}");
        builder.AppendLine($"result_text={parsed.ResultText ?? "(none)"}");

        if (parsed.Usage is not null)
        {
            builder.AppendLine($"usage={parsed.Usage.RawJson}");
            builder.AppendLine($"model={parsed.Usage.Model ?? "unknown"}");
        }

        if (!string.IsNullOrWhiteSpace(parsed.ModelUsageSummary))
        {
            builder.AppendLine(parsed.ModelUsageSummary);
        }

        if (!string.IsNullOrWhiteSpace(parsed.ParseWarnings))
        {
            builder.AppendLine(parsed.ParseWarnings);
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            builder.AppendLine("stderr:");
            builder.AppendLine(standardError.Trim());
        }

        return builder.ToString().TrimEnd();
    }

    private sealed record ParsedClaudeStreamResult(
        string? SessionId,
        string? AssistantText,
        string? ResultText,
        RelayUsageMetrics? Usage,
        bool IsError,
        string? ModelUsageSummary,
        string? ParseWarnings,
        bool SawResultLine,
        IReadOnlyList<RelayObservedAction> ObservedActions);

    private static void CaptureObservedAction(
        JsonElement root,
        string? type,
        ICollection<RelayObservedAction> observedActions)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return;
        }

        if (type == "assistant" &&
            root.TryGetProperty("message", out var messageElement) &&
            messageElement.TryGetProperty("content", out var contentElement) &&
            contentElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in contentElement.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var itemTypeElement) ||
                    itemTypeElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var itemType = itemTypeElement.GetString();
                if (!string.Equals(itemType, "tool_use", StringComparison.Ordinal))
                {
                    continue;
                }

                var toolName = item.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : "unknown-tool";
                AddClaudeToolUseActions(toolName, item, observedActions);
            }

            return;
        }

        if (type == "result")
        {
            CaptureClaudePermissionDenials(root, observedActions);
            var eventType =
                root.TryGetProperty("is_error", out var isErrorElement) &&
                isErrorElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                isErrorElement.GetBoolean()
                    ? "tool.failed"
                    : "adapter.event";
            observedActions.Add(new RelayObservedAction(
                eventType,
                $"Claude stream event '{type}'.",
                root.GetRawText()));
            return;
        }

        observedActions.Add(new RelayObservedAction(
            "adapter.event",
            $"Claude stream event '{type}'.",
            root.GetRawText()));
    }

    private static void AddClaudeToolUseActions(
        string? toolName,
        JsonElement item,
        ICollection<RelayObservedAction> observedActions)
    {
        var normalizedTool = toolName ?? "unknown-tool";
        if (string.Equals(normalizedTool, "bash", StringComparison.OrdinalIgnoreCase))
        {
            var command = TryReadClaudeToolInputString(item, "command") ??
                          TryReadClaudeToolInputString(item, "cmd");
            if (!string.IsNullOrWhiteSpace(command))
            {
                var category = RelayApprovalPolicy.ClassifyCommandCategory(command);
                var title = RelayApprovalPolicy.GetApprovalTitle(category);
                var summary = RelayApprovalPolicy.DescribeCommandSummary(category, command);
                var message = $"Claude tool_use 'bash': {command}";
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    message = $"{message}{Environment.NewLine}{summary}";
                }

                observedActions.Add(new RelayObservedAction(
                    "tool.invoked",
                    message,
                    item.GetRawText(),
                    category,
                    title));

                var categoryEventType = RelayApprovalPolicy.GetCategoryEventType(category, "requested");
                if (!string.IsNullOrWhiteSpace(categoryEventType))
                {
                    observedActions.Add(new RelayObservedAction(
                        categoryEventType,
                        message,
                        item.GetRawText(),
                        category,
                        title));
                }

                return;
            }
        }

        var toolCategory = RelayApprovalPolicy.ClassifyToolCategory(normalizedTool);
        var toolTitle = RelayApprovalPolicy.GetToolTitle(toolCategory, normalizedTool);
        var toolSummary = RelayApprovalPolicy.DescribeToolSummary(toolCategory, normalizedTool);
        var toolMessage = $"Claude tool_use '{normalizedTool}'.";
        if (!string.IsNullOrWhiteSpace(toolSummary))
        {
            toolMessage = $"{toolMessage}{Environment.NewLine}{toolSummary}";
        }

        observedActions.Add(new RelayObservedAction(
            "tool.invoked",
            toolMessage,
            item.GetRawText(),
            toolCategory,
            toolTitle));

        var toolCategoryEventType = RelayApprovalPolicy.GetCategoryEventType(toolCategory, "requested");
        if (!string.IsNullOrWhiteSpace(toolCategoryEventType))
        {
            observedActions.Add(new RelayObservedAction(
                toolCategoryEventType,
                toolMessage,
                item.GetRawText(),
                toolCategory,
                toolTitle));
        }
    }

    private static void CaptureClaudePermissionDenials(JsonElement root, ICollection<RelayObservedAction> observedActions)
    {
        if (!root.TryGetProperty("permission_denials", out var denialsElement) ||
            denialsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var denial in denialsElement.EnumerateArray())
        {
            var toolName = denial.TryGetProperty("tool", out var toolElement) && toolElement.ValueKind == JsonValueKind.String
                ? toolElement.GetString()
                : "unknown-tool";
            var command = denial.TryGetProperty("command", out var commandElement) && commandElement.ValueKind == JsonValueKind.String
                ? commandElement.GetString()
                : null;

            var category = !string.IsNullOrWhiteSpace(command)
                ? RelayApprovalPolicy.ClassifyCommandCategory(command)
                : RelayApprovalPolicy.ClassifyToolCategory(toolName);
            var title = !string.IsNullOrWhiteSpace(command)
                ? RelayApprovalPolicy.GetApprovalTitle(category)
                : RelayApprovalPolicy.GetToolTitle(category, toolName);

            var message = !string.IsNullOrWhiteSpace(command)
                ? $"Claude permission denied for command: {command}"
                : $"Claude permission denied for tool '{toolName}'.";
            var summary = !string.IsNullOrWhiteSpace(command)
                ? RelayApprovalPolicy.DescribeCommandSummary(category, command)
                : RelayApprovalPolicy.DescribeToolSummary(category, toolName);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                message = $"{message}{Environment.NewLine}{summary}";
            }

            observedActions.Add(new RelayObservedAction(
                "approval.denied",
                message,
                denial.GetRawText(),
                category,
                title));

            var categoryEventType = RelayApprovalPolicy.GetCategoryEventType(category, "denied");
            if (!string.IsNullOrWhiteSpace(categoryEventType))
            {
                observedActions.Add(new RelayObservedAction(
                    categoryEventType,
                    message,
                    denial.GetRawText(),
                    category,
                    title));
            }
        }
    }

    private static string? TryReadClaudeToolInputString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty("input", out var inputElement) ||
            inputElement.ValueKind != JsonValueKind.Object ||
            !inputElement.TryGetProperty(propertyName, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return propertyElement.GetString();
    }

    private static string? SummarizeClaudeModelUsage(JsonElement root)
    {
        if (!root.TryGetProperty("model_usage", out var modelUsageElement) ||
            modelUsageElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var summaries = new List<string>();
        foreach (var entry in modelUsageElement.EnumerateObject())
        {
            var modelCost = entry.Value.ValueKind == JsonValueKind.Object &&
                            entry.Value.TryGetProperty("costUSD", out var costElement) &&
                            costElement.ValueKind == JsonValueKind.Number
                ? costElement.GetDouble().ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                : "?";
            summaries.Add($"{entry.Name}: cost_usd={modelCost}");
        }

        return summaries.Count == 0
            ? null
            : $"Claude model_usage: {string.Join(", ", summaries)}";
    }

    private async Task<string?> GetCliVersionAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_cachedCliVersion))
        {
            return _cachedCliVersion;
        }

        try
        {
            var result = await _runner.RunAsync(_command, ["--version"], _workingDirectory, cancellationToken);
            if (result.ExitCode == 0)
            {
                var match = Regex.Match(result.StandardOutput ?? string.Empty, @"\d+\.\d+\.\d+");
                _cachedCliVersion = match.Success ? match.Value : null;
            }
        }
        catch
        {
            // Best-effort diagnostics only.
        }

        return _cachedCliVersion;
    }

    private async Task<string?> GetAuthMethodAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_cachedAuthMethod))
        {
            return _cachedAuthMethod;
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
        {
            _cachedAuthMethod = "api-key";
            return _cachedAuthMethod;
        }

        try
        {
            var result = await _runner.RunAsync(_command, ["auth", "status"], _workingDirectory, cancellationToken);
            if (result.ExitCode == 0)
            {
                _cachedAuthMethod = TryParseAuthMethod(result.StandardOutput);
            }
        }
        catch
        {
            // Best-effort diagnostics only.
        }

        if (string.IsNullOrWhiteSpace(_cachedAuthMethod) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN")))
        {
            _cachedAuthMethod = "oauth-token";
        }

        return _cachedAuthMethod;
    }

    private static string? TryParseAuthMethod(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("authMethod", out var authMethodElement) &&
                authMethodElement.ValueKind == JsonValueKind.String)
            {
                return authMethodElement.GetString();
            }
        }
        catch (JsonException)
        {
        }

        var match = Regex.Match(stdout, "\"authMethod\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string Shorten(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        var normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }
}
