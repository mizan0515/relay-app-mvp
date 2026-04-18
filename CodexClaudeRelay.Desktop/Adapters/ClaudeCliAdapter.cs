using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Broker;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;

namespace CodexClaudeRelay.Desktop.Adapters;

internal sealed class ClaudeCliAdapter : IRelayAdapter
{
    private const string ProbePrompt = "Return exactly the word ok.";

    private readonly ProcessCommandRunner _runner;
    private readonly string _workingDirectory;
    private readonly string _command;
    private readonly double? _maxBudgetUsd;
    private string? _cachedCliVersion;
    private string? _cachedAuthMethod;

    public ClaudeCliAdapter(
        string workingDirectory,
        string command = "claude",
        double? maxBudgetUsd = null,
        RelayJobObjectOptions? jobObjectOptions = null)
    {
        _runner = new ProcessCommandRunner(jobObjectOptions);
        _workingDirectory = workingDirectory;
        _command = command;
        _maxBudgetUsd = maxBudgetUsd;
    }

    public string Role => AgentRole.Claude;

    public async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var hasLongLivedToken = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN"));
        var tokenSummary = hasLongLivedToken
            ? "CLAUDE_CODE_OAUTH_TOKEN detected"
            : "using /login subscription OAuth";
        var authStatus = await _runner.RunAsync(_command, ["auth", "status"], _workingDirectory, cancellationToken);
        var probe = await _runner.RunAsync(
            _command,
            ["-p", ProbePrompt, "--output-format", "json"],
            _workingDirectory,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(probe.StandardOutput))
        {
            return new AdapterStatus(
                RelayHealthStatus.Unavailable,
                false,
                $"probe produced no stdout. stderr: {probe.StandardError}");
        }

        JsonDocument probeDocument;
        try
        {
            probeDocument = JsonDocument.Parse(probe.StandardOutput);
        }
        catch (JsonException ex)
        {
            return new AdapterStatus(
                RelayHealthStatus.Unavailable,
                false,
                $"{tokenSummary}; probe returned non-JSON ({ex.Message}). stderr: {probe.StandardError}");
        }

        using (probeDocument)
        {
            var probeRoot = probeDocument.RootElement;
            var probeOutcome = ParseProbeOutcome(probeRoot, probe.StandardOutput);
            var authLooksLoggedIn = authStatus.ExitCode == 0 &&
                                    authStatus.StandardOutput.Contains("\"loggedIn\": true", StringComparison.OrdinalIgnoreCase);

            if (probeOutcome.IsError)
            {
                var health = authLooksLoggedIn ? RelayHealthStatus.Degraded : RelayHealthStatus.Unavailable;
                var hint = BuildFailureHint(probeOutcome.Message, hasLongLivedToken, authLooksLoggedIn);
                return new AdapterStatus(
                    health,
                    false,
                    $"{tokenSummary}; probe failed: {probeOutcome.Message}; {hint}");
            }

            var authSummary = authLooksLoggedIn
                ? "auth status ok"
                : "auth status unclear";

            return new AdapterStatus(
                RelayHealthStatus.Healthy,
                true,
                $"{tokenSummary}; {authSummary}; probe ok");
        }
    }

    public Task<RelayAdapterResult> RunTurnAsync(RelayTurnContext context, CancellationToken cancellationToken)
    {
        var prompt = RelayPromptBuilder.BuildTurnPrompt(context);

        var sessionHandle = string.IsNullOrWhiteSpace(context.ExistingSessionHandle)
            ? Guid.NewGuid().ToString()
            : context.ExistingSessionHandle!;

        return ExecuteAsync(prompt, sessionHandle, resumeExisting: !string.IsNullOrWhiteSpace(context.ExistingSessionHandle), cancellationToken);
    }

    public Task<RelayAdapterResult> RunRepairAsync(RelayRepairContext context, CancellationToken cancellationToken)
    {
        var prompt = RelayPromptBuilder.BuildRepairPrompt(context);

        var sessionHandle = string.IsNullOrWhiteSpace(context.ExistingSessionHandle)
            ? Guid.NewGuid().ToString()
            : context.ExistingSessionHandle!;

        return ExecuteAsync(prompt, sessionHandle, resumeExisting: !string.IsNullOrWhiteSpace(context.ExistingSessionHandle), cancellationToken);
    }

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
            "json",
        };

        if (_maxBudgetUsd.HasValue)
        {
            args.Add("--max-budget-usd");
            args.Add(_maxBudgetUsd.Value.ToString("0.00", CultureInfo.InvariantCulture));
        }

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

        var result = await _runner.RunAsync(_command, args, _workingDirectory, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException($"Claude produced no stdout. stderr: {result.StandardError}");
        }

        var cliVersion = await GetCliVersionAsync(cancellationToken);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(result.StandardOutput);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Claude turn returned non-JSON output ({ex.Message}). stdout head: {Shorten(result.StandardOutput, 240)}; stderr: {result.StandardError}");
        }

        using (document)
        {
            var root = document.RootElement;
            var isError = root.TryGetProperty("is_error", out var isErrorElement) && isErrorElement.GetBoolean();
            var output = TryReadStructuredOutput(root) ?? (root.TryGetProperty("result", out var resultElement)
                ? resultElement.GetString() ?? string.Empty
                : result.StandardOutput);
            var resolvedSessionId = root.TryGetProperty("session_id", out var sessionElement)
                ? sessionElement.GetString()
                : sessionHandle;
            var usage = RelayUsageMetrics.FromClaudeJson(root);
            if (usage is not null)
            {
                usage = usage with
                {
                    CliVersion = cliVersion,
                    AuthMethod = await GetAuthMethodAsync(cancellationToken)
                };
            }
            var modelUsageSummary = SummarizeClaudeModelUsage(root);
            var subtype = root.TryGetProperty("subtype", out var subtypeElement) && subtypeElement.ValueKind == JsonValueKind.String
                ? subtypeElement.GetString()
                : null;
            var diagnosticsPrefix =
                !string.IsNullOrWhiteSpace(resolvedSessionId) &&
                !string.Equals(resolvedSessionId, sessionHandle, StringComparison.Ordinal)
                    ? $"Claude returned session_id={resolvedSessionId} for requested session {sessionHandle}. Possible silent fork.{Environment.NewLine}"
                    : string.Empty;
            var diagnostics = string.IsNullOrWhiteSpace(result.StandardError)
                ? diagnosticsPrefix.TrimEnd()
                : diagnosticsPrefix + result.StandardError;
            if (!string.IsNullOrWhiteSpace(modelUsageSummary))
            {
                diagnostics = string.IsNullOrWhiteSpace(diagnostics)
                    ? modelUsageSummary
                    : $"{diagnostics}{Environment.NewLine}{modelUsageSummary}";
            }

            if (isError)
            {
                if (_maxBudgetUsd.HasValue &&
                    string.Equals(subtype, "error_max_budget_usd", StringComparison.Ordinal))
                {
                    return new RelayAdapterResult(output, resolvedSessionId, diagnostics, usage);
                }

                throw new InvalidOperationException(
                    $"Claude returned an error result (subtype={subtype ?? "<none>"}). Output: {output}{Environment.NewLine}stderr: {diagnostics}");
            }

            return new RelayAdapterResult(output, resolvedSessionId, diagnostics, usage);
        }
    }

    private static (bool IsError, string Message) ParseProbeOutcome(JsonElement root, string fallback)
    {
        var isError = root.TryGetProperty("is_error", out var isErrorElement) && isErrorElement.GetBoolean();
        var message = root.TryGetProperty("result", out var resultElement)
            ? resultElement.GetString() ?? string.Empty
            : fallback;
        return (isError, message);
    }

    private static string BuildFailureHint(string message, bool hasLongLivedToken, bool authLooksLoggedIn)
    {
        if (message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Invalid authentication credentials", StringComparison.OrdinalIgnoreCase))
        {
            return hasLongLivedToken
                ? "check whether CLAUDE_CODE_OAUTH_TOKEN is still valid or restart the app shell after updating it"
                : "run claude setup-token or refresh /login auth, then retry Check Adapters";
        }

        if (message.Contains("Not logged in", StringComparison.OrdinalIgnoreCase))
        {
            return hasLongLivedToken
                ? "restart the app from a shell that inherits CLAUDE_CODE_OAUTH_TOKEN"
                : "run /login in claude or set CLAUDE_CODE_OAUTH_TOKEN";
        }

        if (!authLooksLoggedIn)
        {
            return "complete Claude authentication first, then retry Check Adapters";
        }

        return "retry the probe and inspect the event log if the problem persists";
    }

    private static string? TryReadStructuredOutput(JsonElement root)
    {
        if (!root.TryGetProperty("structured_output", out var structuredOutput) ||
            structuredOutput.ValueKind == JsonValueKind.Null ||
            structuredOutput.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var handoff = JsonSerializer.Deserialize<HandoffEnvelope>(structuredOutput.GetRawText(), HandoffJson.SerializerOptions);
        return handoff is null
            ? null
            : JsonSerializer.Serialize(handoff, HandoffJson.SerializerOptions);
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
                ? costElement.GetDouble().ToString("F4", CultureInfo.InvariantCulture)
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
