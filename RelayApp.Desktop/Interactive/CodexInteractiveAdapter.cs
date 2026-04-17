using RelayApp.Core.Adapters;
using RelayApp.Core.Broker;
using RelayApp.Core.Models;
using RelayApp.Core.Policy;
using RelayApp.Core.Pricing;
using RelayApp.Core.Protocol;
using RelayApp.CodexProtocol;
using RelayApp.Desktop.Adapters;
using System.Text;
using System.Text.Json;

namespace RelayApp.Desktop.Interactive;

internal sealed class CodexInteractiveAdapter : IRelayAdapter, IAsyncDisposable
{
    private readonly string _workingDirectory;
    private readonly RelayJobObjectOptions _jobObjectOptions;
    private readonly Func<RelayPendingApproval, CancellationToken, Task<RelayApprovalDecision>>? _approvalHandler;
    private readonly Func<bool>? _autoApproveAllProvider;
    // Resolved once at construction. Stale if the operator edits config.toml
    // mid-session; not re-read per turn to avoid I/O on the hot path.
    private readonly string? _configuredModel;

    public CodexInteractiveAdapter(
        string workingDirectory,
        Func<RelayPendingApproval, CancellationToken, Task<RelayApprovalDecision>>? approvalHandler = null,
        Func<bool>? autoApproveAllProvider = null,
        RelayJobObjectOptions? jobObjectOptions = null)
    {
        _workingDirectory = workingDirectory;
        _approvalHandler = approvalHandler;
        _autoApproveAllProvider = autoApproveAllProvider;
        _jobObjectOptions = jobObjectOptions ?? new RelayJobObjectOptions();
        _configuredModel = CodexModelResolver.TryResolveConfiguredModel();
    }

    public RelaySide Side => RelaySide.Codex;

    public async Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await CodexProtocolConnection.StartAsync(
                new CodexProtocolConnectionOptions
                {
                    WorkingDirectory = _workingDirectory,
                    JobObjectOptions = _jobObjectOptions,
                },
                cancellationToken);

            await connection.SendRequestAsync(
                CodexProtocolMethods.Initialize,
                new
                {
                    clientInfo = new
                    {
                        name = "relay-app-codex-interactive",
                        title = "Relay App Codex Interactive Adapter",
                        version = "0.1.0"
                    },
                    capabilities = new
                    {
                        experimentalApi = true,
                        optOutNotificationMethods = Array.Empty<string>()
                    }
                },
                cancellationToken);

            var authStatus = await connection.SendRequestAsync(CodexProtocolMethods.GetAuthStatus, new { }, cancellationToken);
            var authMethod = authStatus.TryGetProperty("authMethod", out var authMethodElement) && authMethodElement.ValueKind == JsonValueKind.String
                ? authMethodElement.GetString()
                : null;

            var threadStartResult = await connection.SendRequestAsync(
                CodexProtocolMethods.ThreadStart,
                new
                {
                    model = (string?)null,
                    modelProvider = (string?)null,
                    serviceTier = (string?)null,
                    cwd = _workingDirectory,
                    approvalPolicy = "never",
                    approvalsReviewer = (object?)null,
                    sandbox = "workspace-write",
                    config = (object?)null,
                    serviceName = "relay-app-codex-interactive",
                    baseInstructions = (string?)null,
                    developerInstructions = "This is a protocol adapter readiness probe.",
                    personality = (string?)null,
                    ephemeral = true,
                    experimentalRawEvents = false,
                    persistExtendedHistory = true
                },
                cancellationToken);

            var threadId = threadStartResult
                .GetProperty("thread")
                .GetProperty("id")
                .GetString();

            return new AdapterStatus(
                RelayHealthStatus.Healthy,
                !string.IsNullOrWhiteSpace(authMethod),
                $"protocol app-server ready; auth={authMethod ?? "unknown"}; thread/start ok ({threadId ?? "no-thread-id"})");
        }
        catch (Exception ex)
        {
            return new AdapterStatus(
                RelayHealthStatus.Unavailable,
                false,
                $"protocol app-server failed: {ex.Message}");
        }
    }

    public Task<RelayAdapterResult> RunTurnAsync(RelayTurnContext context, CancellationToken cancellationToken)
    {
        var prompt = RelayPromptBuilder.BuildInteractiveTurnPrompt(context);
        return RunProtocolTurnAsync(prompt, cancellationToken);
    }

    public Task<RelayAdapterResult> RunRepairAsync(RelayRepairContext context, CancellationToken cancellationToken)
    {
        var prompt = RelayPromptBuilder.BuildInteractiveRepairPrompt(context);
        return RunProtocolTurnAsync(prompt, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<RelayAdapterResult> RunProtocolTurnAsync(string prompt, CancellationToken cancellationToken)
    {
        var approvalActions = new List<RelayObservedAction>();
        // TODO: Preserve Codex thread continuity across turns once the broker-side
        // rotation/carry policy is implemented. The current one-shot thread model
        // is useful for protocol validation but makes cost look worse than the target design.
        var result = await CodexProtocolTurnRunner.RunOneShotAsync(
            _workingDirectory,
            prompt,
            _jobObjectOptions,
            (request, token) => HandleServerRequestAsync(request, approvalActions, token),
            cancellationToken);
        var output = !string.IsNullOrWhiteSpace(result.FinalAgentMessageText)
            ? result.FinalAgentMessageText!
            : !string.IsNullOrWhiteSpace(result.LastAgentMessageDelta)
                ? result.LastAgentMessageDelta!
                : result.TurnCompletedNotification.GetRawText();

        var usage = RelayUsageMetrics.FromCodexTokenCount(result.LastTokenUsageNotification);
        string? pricingWarning = null;
        if (usage is not null)
        {
            var (costUsd, modelRecognized) = CodexPricing.EstimateUsdWithRecognition(usage, _configuredModel);
            pricingWarning = !modelRecognized
                ? string.IsNullOrWhiteSpace(_configuredModel)
                    ? "pricing fallback: Codex model not resolved from config.toml; using default rates"
                    : $"pricing fallback: model '{_configuredModel}' not in rate card; using default rates"
                : null;
            usage = usage with
            {
                Model = _configuredModel,
                CostUsd = costUsd,
                PricingFallbackReason = pricingWarning
            };
        }

        return new RelayAdapterResult(
            output,
            result.ThreadId,
            BuildDiagnostics(result, pricingWarning),
            usage,
            UsageIsCumulative: true,
            ObservedActions: BuildObservedActions(result, includeServerRequests: approvalActions.Count == 0)
                .Concat(approvalActions)
                .ToArray());
    }

    private async Task<CodexProtocolServerRequestResponse> HandleServerRequestAsync(
        CodexProtocolServerRequest request,
        List<RelayObservedAction> approvalActions,
        CancellationToken cancellationToken)
    {
        if (!IsApprovalRequestMethod(request.Method) || _approvalHandler is null)
        {
            return CodexProtocolServerRequestResponse.Unhandled();
        }

        var pendingApproval = BuildPendingApproval(request);
        var autoApproveAll = _autoApproveAllProvider?.Invoke() == true;
        if (!autoApproveAll &&
            RelayApprovalPolicy.TryResolveDefaultDecision(request.Method, pendingApproval, request.Payload, out var policyDecision, out var policyReason))
        {
            AddApprovalAction(
                approvalActions,
                "policy.applied",
                pendingApproval,
                $"{pendingApproval.Message}{Environment.NewLine}Policy: {policyReason}");

            AddApprovalAction(
                approvalActions,
                policyDecision is RelayApprovalDecision.ApproveOnce or RelayApprovalDecision.ApproveForSession
                    ? "approval.granted"
                    : "approval.denied",
                pendingApproval,
                $"{pendingApproval.Message}{Environment.NewLine}Policy: {policyReason}");

            return CodexProtocolServerRequestResponse.FromResult(BuildServerRequestResponse(request, policyDecision));
        }

        AddApprovalAction(
            approvalActions,
            "approval.requested",
            pendingApproval,
            pendingApproval.Message);

        RelayApprovalDecision decision;
        if (autoApproveAll)
        {
            // Resolve auto-approve synchronously here so the server request is answered
            // before the Codex app-server times out or emits a decline. Offloading to
            // _approvalHandler (which marshals onto the UI Dispatcher and persists the
            // approval queue) previously let the reply arrive after Codex had already
            // declined the command on its own timeout.
            AddApprovalAction(
                approvalActions,
                "approval.auto_mode.applied",
                pendingApproval,
                $"Auto-approved {pendingApproval.Title} because dangerous auto-approve mode is enabled.");
            decision = RelayApprovalDecision.ApproveForSession;
            _ = Task.Run(() => _approvalHandler(pendingApproval, cancellationToken), cancellationToken);
        }
        else
        {
            decision = await _approvalHandler(pendingApproval, cancellationToken);
        }
        AddApprovalAction(
            approvalActions,
            decision is RelayApprovalDecision.ApproveOnce or RelayApprovalDecision.ApproveForSession
                ? "approval.granted"
                : "approval.denied",
            pendingApproval,
            $"{pendingApproval.Message} Decision={decision}.");

        return CodexProtocolServerRequestResponse.FromResult(BuildServerRequestResponse(request, decision));
    }

    private static IReadOnlyList<RelayObservedAction> BuildObservedActions(
        CodexProtocolTurnResult result,
        bool includeServerRequests)
    {
        var actions = new List<RelayObservedAction>();

        foreach (var message in result.Messages)
        {
            switch (message.Kind)
            {
                case CodexProtocolMessageKind.ServerRequest:
                    if (includeServerRequests)
                    {
                        actions.Add(new RelayObservedAction(
                            "approval.requested",
                            $"Codex server request '{message.Method ?? "(unknown)"}' requires broker handling.",
                            message.Payload.ValueKind == JsonValueKind.Undefined ? null : message.Payload.GetRawText()));
                    }
                    break;

                case CodexProtocolMessageKind.Notification:
                    if (TryBuildNotificationAction(message, out var action))
                    {
                        actions.Add(action);
                        var categoryEventType = RelayApprovalPolicy.GetCategoryEventType(
                            action.Category ?? string.Empty,
                            action.EventType switch
                            {
                                "tool.invoked" => "requested",
                                "tool.completed" => "completed",
                                "tool.failed" => "failed",
                                _ => string.Empty
                            });
                        if (!string.IsNullOrWhiteSpace(categoryEventType))
                        {
                            actions.Add(new RelayObservedAction(
                                categoryEventType,
                                action.Message,
                                action.Payload,
                                action.Category,
                                action.Title));
                        }
                    }
                    break;
            }
        }

        return actions;
    }

    private static bool TryBuildNotificationAction(CodexProtocolMessage message, out RelayObservedAction action)
    {
        action = default!;

        if (string.IsNullOrWhiteSpace(message.Method))
        {
            return false;
        }

        if (message.Method == CodexProtocolMethods.ItemStartedNotification ||
            message.Method == CodexProtocolMethods.ItemCompletedNotification)
        {
            var itemType = TryReadItemType(message.Payload);
            if (string.Equals(itemType, "agentMessage", StringComparison.Ordinal))
            {
                return false;
            }

            var eventType = message.Method == CodexProtocolMethods.ItemStartedNotification
                ? "tool.invoked"
                : "tool.completed";
            var category = RelayApprovalPolicy.ClassifyCodexItemCategory(itemType);
            // For commandExecution items, refine the generic "shell" category into
            // git / git-add / git-commit / git-push / pr when the underlying command warrants it.
            // This is what makes the broker's git-specific approval classes reachable for Codex
            // on Windows, where every command arrives wrapped in "powershell.exe -Command '...'".
            if (string.Equals(category, "shell", StringComparison.Ordinal))
            {
                var commandPayload = TryReadItemCommand(message.Payload);
                if (!string.IsNullOrWhiteSpace(commandPayload))
                {
                    var refined = RelayApprovalPolicy.ClassifyCommandCategory(commandPayload);
                    if (!string.Equals(refined, "command", StringComparison.Ordinal))
                    {
                        category = refined;
                    }
                }
            }
            // For fileChange items, refine "file-change" into "dad-asset" when the target path
            // lives under Document/dialogue/** so DAD-runtime-critical writes (backlog / state /
            // session transcripts) surface distinctly from generic file changes.
            else if (string.Equals(category, "file-change", StringComparison.Ordinal))
            {
                var firstPath = TryReadItemFileChangePath(message.Payload);
                if (!string.IsNullOrWhiteSpace(firstPath) && RelayApprovalPolicy.IsDadAssetPath(firstPath))
                {
                    category = "dad-asset";
                }
            }
            var title = RelayApprovalPolicy.GetToolTitle(category, itemType);
            var actionMessage = $"Codex {message.Method} ({itemType ?? "unknown-item"}).";
            var summary = RelayApprovalPolicy.DescribeToolSummary(category, itemType);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                actionMessage = $"{actionMessage}{Environment.NewLine}{summary}";
            }

            action = new RelayObservedAction(
                eventType,
                actionMessage,
                message.Payload.ValueKind == JsonValueKind.Undefined ? null : message.Payload.GetRawText(),
                category,
                title);
            return true;
        }

        if (message.Method == CodexProtocolMethods.ThreadStartedNotification ||
            message.Method == CodexProtocolMethods.TurnStartedNotification ||
            message.Method == CodexProtocolMethods.TurnCompletedNotification ||
            message.Method == CodexProtocolMethods.ThreadStatusChangedNotification)
        {
            action = new RelayObservedAction(
                "adapter.event",
                $"Codex transport event '{message.Method}'.",
                message.Payload.ValueKind == JsonValueKind.Undefined ? null : message.Payload.GetRawText());
            return true;
        }

        return false;
    }

    private static string? TryReadItemType(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("item", out var itemElement) ||
            itemElement.ValueKind != JsonValueKind.Object ||
            !itemElement.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return typeElement.GetString();
    }

    private static string? TryReadItemCommand(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("item", out var itemElement) ||
            itemElement.ValueKind != JsonValueKind.Object ||
            !itemElement.TryGetProperty("command", out var commandElement) ||
            commandElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return commandElement.GetString();
    }

    private static string? TryReadItemFileChangePath(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("item", out var itemElement) ||
            itemElement.ValueKind != JsonValueKind.Object ||
            !itemElement.TryGetProperty("changes", out var changesElement) ||
            changesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var change in changesElement.EnumerateArray())
        {
            if (change.ValueKind == JsonValueKind.Object &&
                change.TryGetProperty("path", out var pathElement) &&
                pathElement.ValueKind == JsonValueKind.String)
            {
                var value = pathElement.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static RelayPendingApproval BuildFileChangePendingApproval(
        CodexProtocolServerRequest request,
        string approvalId,
        string? payloadText,
        DateTimeOffset now)
    {
        var category = RelayApprovalPolicy.RefineFileChangeCategory(request.Payload);
        return new RelayPendingApproval(
            approvalId,
            RelaySide.Codex,
            category,
            RelayApprovalPolicy.GetApprovalTitle(category),
            BuildFileChangeApprovalMessage(request.Payload),
            payloadText,
            RelayApprovalPolicy.BuildPolicyKey(request.Method, category, request.Payload),
            now,
            RelayApprovalPolicy.GetRiskLevel(category, request.Payload));
    }

    private static bool IsApprovalRequestMethod(string method) =>
        string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal) ||
        string.Equals(method, "item/fileChange/requestApproval", StringComparison.Ordinal) ||
        string.Equals(method, "item/permissions/requestApproval", StringComparison.Ordinal);

    private static RelayPendingApproval BuildPendingApproval(CodexProtocolServerRequest request)
    {
        var now = DateTimeOffset.Now;
        var payloadText = request.Payload.ValueKind == JsonValueKind.Undefined ? null : request.Payload.GetRawText();
        var approvalId = TryReadString(request.Payload, "approvalId") ??
                         TryReadString(request.Payload, "itemId") ??
                         Guid.NewGuid().ToString("N");

        return request.Method switch
        {
            "item/commandExecution/requestApproval" => new RelayPendingApproval(
                approvalId,
                RelaySide.Codex,
                RelayApprovalPolicy.ClassifyCommandCategory(request.Payload),
                RelayApprovalPolicy.GetApprovalTitle(RelayApprovalPolicy.ClassifyCommandCategory(request.Payload)),
                BuildCommandApprovalMessage(request.Payload),
                payloadText,
                RelayApprovalPolicy.BuildPolicyKey(
                    request.Method,
                    RelayApprovalPolicy.ClassifyCommandCategory(request.Payload),
                    request.Payload),
                now,
                RelayApprovalPolicy.GetRiskLevel(RelayApprovalPolicy.ClassifyCommandCategory(request.Payload), request.Payload)),
            "item/fileChange/requestApproval" => BuildFileChangePendingApproval(request, approvalId, payloadText, now),
            "item/permissions/requestApproval" => new RelayPendingApproval(
                approvalId,
                RelaySide.Codex,
                "permissions",
                RelayApprovalPolicy.GetApprovalTitle("permissions"),
                BuildPermissionsApprovalMessage(request.Payload),
                payloadText,
                RelayApprovalPolicy.BuildPolicyKey(request.Method, "permissions", request.Payload),
                now,
                RelayApprovalPolicy.GetRiskLevel("permissions", request.Payload)),
            _ => new RelayPendingApproval(
                approvalId,
                RelaySide.Codex,
                "transport",
                "Transport Approval",
                $"Codex server request '{request.Method}' requires approval.",
                payloadText,
                null,
                now,
                "medium"),
        };
    }

    private static void AddApprovalAction(
        List<RelayObservedAction> actions,
        string eventType,
        RelayPendingApproval pendingApproval,
        string message)
    {
        actions.Add(new RelayObservedAction(
            eventType,
            message,
            pendingApproval.Payload,
            pendingApproval.Category,
            pendingApproval.Title));

        var categoryEventType = RelayApprovalPolicy.GetCategoryEventType(
            pendingApproval.Category,
            eventType switch
            {
                "approval.requested" => "requested",
                "approval.granted" => "granted",
                "approval.denied" => "denied",
                _ => string.Empty
            });

        if (!string.IsNullOrWhiteSpace(categoryEventType))
        {
            actions.Add(new RelayObservedAction(
                categoryEventType,
                message,
                pendingApproval.Payload,
                pendingApproval.Category,
                pendingApproval.Title));
        }
    }

    private static object BuildServerRequestResponse(CodexProtocolServerRequest request, RelayApprovalDecision decision)
    {
        // Codex's item/commandExecution/requestApproval and item/fileChange/requestApproval
        // only accept the single-turn decisions {accept, cancel} (plus an optional
        // acceptWithExecpolicyAmendment for commandExecution). Session-scope approval is
        // expressed via item/permissions/requestApproval's `scope = "session"`, not by a
        // `decision = "acceptForSession"` string. Emitting the latter was treated as an
        // unknown value and caused the command to be declined even when the relay had
        // already auto-approved it.
        var decisionName = decision switch
        {
            RelayApprovalDecision.ApproveOnce => "accept",
            RelayApprovalDecision.ApproveForSession => "accept",
            RelayApprovalDecision.Deny => "cancel",
            RelayApprovalDecision.Cancel => "cancel",
            _ => "cancel"
        };

        return request.Method switch
        {
            "item/commandExecution/requestApproval" => new { decision = decisionName },
            "item/fileChange/requestApproval" => new { decision = decisionName },
            "item/permissions/requestApproval" => BuildPermissionsApprovalResponse(request.Payload, decision),
            _ => new { decision = decisionName }
        };
    }

    private static object BuildPermissionsApprovalResponse(JsonElement payload, RelayApprovalDecision decision)
    {
        var permissions = payload.ValueKind == JsonValueKind.Object &&
                          payload.TryGetProperty("permissions", out var permissionsElement)
            ? JsonSerializer.Deserialize<object>(permissionsElement.GetRawText())
            : new { fileSystem = (object?)null, network = (object?)null };

        if (decision is RelayApprovalDecision.Deny or RelayApprovalDecision.Cancel)
        {
            permissions = new { fileSystem = (object?)null, network = (object?)null };
        }

        return new
        {
            permissions,
            scope = decision == RelayApprovalDecision.ApproveForSession ? "session" : "turn"
        };
    }

    private static string BuildCommandApprovalMessage(JsonElement payload)
    {
        var command = TryReadString(payload, "command") ?? "(unknown command)";
        var cwd = TryReadString(payload, "cwd");
        var reason = TryReadString(payload, "reason");
        var category = RelayApprovalPolicy.ClassifyCommandCategory(payload);
        var commandSummary = RelayApprovalPolicy.DescribeCommandSummary(category, payload);
        var policyHint = RelayApprovalPolicy.DescribeDefaultPolicy(
            "item/commandExecution/requestApproval",
            category,
            payload);

        var baseMessage = string.IsNullOrWhiteSpace(reason)
            ? $"Codex wants to run command: {command}{(string.IsNullOrWhiteSpace(cwd) ? string.Empty : $" (cwd: {cwd})")}"
            : $"Codex wants to run command: {command}{(string.IsNullOrWhiteSpace(cwd) ? string.Empty : $" (cwd: {cwd})")}{Environment.NewLine}Reason: {reason}";
        if (!string.IsNullOrWhiteSpace(commandSummary))
        {
            baseMessage = $"{baseMessage}{Environment.NewLine}{commandSummary}";
        }

        baseMessage = $"{baseMessage}{Environment.NewLine}{RelayApprovalPolicy.DescribeRiskLevel(RelayApprovalPolicy.GetRiskLevel(category, payload))}";

        return string.IsNullOrWhiteSpace(policyHint)
            ? baseMessage
            : $"{baseMessage}{Environment.NewLine}{policyHint}";
    }


    private static string BuildFileChangeApprovalMessage(JsonElement payload)
    {
        var reason = TryReadString(payload, "reason");
        var target = TryReadFirstPath(payload);
        var summary = RelayApprovalPolicy.DescribeFileChangeSummary(payload);
        var policyHint = RelayApprovalPolicy.DescribeDefaultPolicy(
            "item/fileChange/requestApproval",
            "file-change",
            payload);

        var message = string.IsNullOrWhiteSpace(reason)
            ? $"Codex wants to apply file changes{(string.IsNullOrWhiteSpace(target) ? string.Empty : $" to {target}")}."
            : $"Codex wants to apply file changes{(string.IsNullOrWhiteSpace(target) ? string.Empty : $" to {target}")}.{Environment.NewLine}Reason: {reason}";
        if (!string.IsNullOrWhiteSpace(summary))
        {
            message = $"{message}{Environment.NewLine}{summary}";
        }

        message = $"{message}{Environment.NewLine}{RelayApprovalPolicy.DescribeRiskLevel(RelayApprovalPolicy.GetRiskLevel("file-change", payload))}";

        return string.IsNullOrWhiteSpace(policyHint)
            ? message
            : $"{message}{Environment.NewLine}{policyHint}";
    }

    private static string BuildPermissionsApprovalMessage(JsonElement payload)
    {
        var reason = TryReadString(payload, "reason");
        var summary = RelayApprovalPolicy.DescribePermissionsSummary(payload);
        var policyHint = RelayApprovalPolicy.DescribeDefaultPolicy(
            "item/permissions/requestApproval",
            "permissions",
            payload);

        var message = string.IsNullOrWhiteSpace(reason)
            ? "Codex requests additional permissions."
            : $"Codex requests additional permissions.{Environment.NewLine}Reason: {reason}";
        if (!string.IsNullOrWhiteSpace(summary))
        {
            message = $"{message}{Environment.NewLine}{summary}";
        }

        message = $"{message}{Environment.NewLine}{RelayApprovalPolicy.DescribeRiskLevel(RelayApprovalPolicy.GetRiskLevel("permissions", payload))}";

        return string.IsNullOrWhiteSpace(policyHint)
            ? message
            : $"{message}{Environment.NewLine}{policyHint}";
    }

    private static string? TryReadString(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return propertyElement.GetString();
    }

    private static string? TryReadFirstPath(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in new[] { "changes", "fileChanges", "paths" })
        {
            if (!payload.TryGetProperty(propertyName, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in propertyElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    return item.GetString();
                }

                if (item.ValueKind == JsonValueKind.Object)
                {
                    foreach (var candidate in new[] { "path", "filePath", "targetPath" })
                    {
                        if (item.TryGetProperty(candidate, out var candidateElement) && candidateElement.ValueKind == JsonValueKind.String)
                        {
                            return candidateElement.GetString();
                        }
                    }
                }
            }
        }

        return null;
    }

    private static string BuildDiagnostics(CodexProtocolTurnResult result, string? pricingWarning)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"transport=codex-app-server");
        builder.AppendLine($"thread_id={result.ThreadId}");
        builder.AppendLine($"turn_id={result.TurnId}");
        builder.AppendLine($"final_text={result.FinalAgentMessageText ?? "(none)"}");

        if (result.LastTokenUsageNotification.ValueKind != JsonValueKind.Undefined)
        {
            builder.AppendLine("last_token_usage=" + result.LastTokenUsageNotification.GetRawText());
        }

        var stderrLines = result.Messages
            .Where(message => message.Kind == CodexProtocolMessageKind.StderrLine && !string.IsNullOrWhiteSpace(message.Text))
            .Select(message => message.Text!.Trim())
            .ToArray();

        if (stderrLines.Length > 0)
        {
            builder.AppendLine("stderr:");
            foreach (var line in stderrLines)
            {
                builder.AppendLine(line);
            }
        }

        if (!string.IsNullOrWhiteSpace(pricingWarning))
        {
            builder.AppendLine(pricingWarning);
        }

        builder.AppendLine("note=protocol-backed experimental adapter; session continuity is not preserved yet between turns.");
        return builder.ToString().TrimEnd();
    }
}
