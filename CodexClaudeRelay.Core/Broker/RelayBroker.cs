using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Persistence;
using CodexClaudeRelay.Core.Policy;
using CodexClaudeRelay.Core.Pricing;
using CodexClaudeRelay.Core.Protocol;

namespace CodexClaudeRelay.Core.Broker;

public sealed class RelayBroker
{
    private const long SuspiciousClaudeCacheCreationTokens = 15_000;

    private readonly IReadOnlyDictionary<RelaySide, IRelayAdapter> _adapters;
    private readonly IReadOnlyDictionary<RelaySide, IRelayAdapter> _fallbackAdapters;
    private readonly IRelaySessionStore _sessionStore;
    private readonly IEventLogWriter _eventLogWriter;
    private readonly RelayBrokerOptions _options;

    public RelayBroker(
        IEnumerable<IRelayAdapter> adapters,
        IRelaySessionStore sessionStore,
        IEventLogWriter eventLogWriter,
        IEnumerable<IRelayAdapter>? fallbackAdapters = null,
        RelayBrokerOptions? options = null)
    {
        _adapters = adapters.ToDictionary(adapter => adapter.Side);
        _fallbackAdapters = fallbackAdapters?.ToDictionary(adapter => adapter.Side) ?? new Dictionary<RelaySide, IRelayAdapter>();
        _sessionStore = sessionStore;
        _eventLogWriter = eventLogWriter;
        _options = options ?? new RelayBrokerOptions();
    }

    public RelaySessionState State { get; private set; } = new();

    public RelayBrokerOptions Options => _options;

    public string CurrentLogPath =>
        string.IsNullOrWhiteSpace(State.SessionId)
            ? string.Empty
            : _eventLogWriter.GetLogPath(State.SessionId);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        State = await _sessionStore.LoadAsync(cancellationToken) ?? new RelaySessionState();
        var hadNativeHandles = State.NativeSessionHandles.Count > 0;
        State.NativeSessionHandles = [];
        State.LastCumulativeByHandle = [];
        State.TurnsSinceLastRotation = 0;
        State.SessionStartedAt = DateTimeOffset.Now;

        if (hadNativeHandles && !string.IsNullOrWhiteSpace(State.SessionId))
        {
            State.UpdatedAt = DateTimeOffset.Now;
            await _sessionStore.SaveAsync(State, cancellationToken);
            await _eventLogWriter.AppendAsync(
                State.SessionId,
                new RelayLogEvent(
                    DateTimeOffset.Now,
                    "session.reloaded",
                    State.ActiveSide,
                    "Broker reloaded. Native session handles and cumulative handle baselines were reset. Cumulative token totals were preserved."),
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(State.SessionId))
        {
            await _eventLogWriter.AppendAsync(
                State.SessionId,
                new RelayLogEvent(
                    DateTimeOffset.Now,
                    "broker.options",
                    State.ActiveSide,
                    BuildOptionsSummary()),
                cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<RelaySide, AdapterStatus>> GetAdapterStatusesAsync(CancellationToken cancellationToken)
    {
        var statuses = new Dictionary<RelaySide, AdapterStatus>();
        foreach (var adapter in _adapters.Values.OrderBy(item => item.Side))
        {
            statuses[adapter.Side] = await adapter.GetStatusAsync(cancellationToken);
        }

        return statuses;
    }

    public async Task StartSessionAsync(
        string sessionId,
        RelaySide firstSide,
        string firstPrompt,
        CancellationToken cancellationToken)
    {
        State = new RelaySessionState
        {
            SessionId = sessionId,
            Status = RelaySessionStatus.Active,
            ActiveSide = firstSide,
            CurrentTurn = 1,
            PendingPrompt = firstPrompt,
            RepairAttempts = 0,
            LastError = null,
            PendingApproval = null,
            ApprovalQueue = [],
            SessionApprovalRules = [],
            PolicyGapAdvisoriesFired = [],
            LastHandoff = null,
            LastHandoffHash = null,
            Goal = null,
            Completed = [],
            Pending = [],
            Constraints = [],
            LastUsageBySide = [],
            LastCumulativeByHandle = [],
            TotalInputTokens = 0,
            TotalOutputTokens = 0,
            TotalCacheCreationInputTokens = 0,
            TotalCacheReadInputTokens = 0,
            TotalCostClaudeUsd = 0,
            TotalCostCodexUsd = 0,
            ClaudeCostAtLastRotationUsd = 0,
            CacheReadRatioByTurn = [],
            ConsecutiveLowCacheTurnsBySide = [],
            SessionStartedAt = DateTimeOffset.Now,
            RotationCount = 0,
            TurnsSinceLastRotation = 0,
            LastBudgetSignal = null,
            CacheInflationAdvisoryFired = false,
            ClaudeCostAbsentAdvisoryFired = false,
            ClaudeCostCeilingDisabledAdvisoryFired = false,
            CodexPricingFallbackAdvisoryFired = false,
            CodexRateCardStaleAdvisoryFired = false,
            UpdatedAt = DateTimeOffset.Now,
        };

        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "session.started", firstSide, "Relay session started.", firstPrompt),
            cancellationToken);

        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "broker.options", firstSide, BuildOptionsSummary()),
            cancellationToken);
    }

    public async Task PauseAsync(string reason, CancellationToken cancellationToken)
    {
        State.Status = RelaySessionStatus.Paused;
        State.LastError = reason;
        State.UpdatedAt = DateTimeOffset.Now;
        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "session.paused", State.ActiveSide, reason),
            cancellationToken);
    }

    public async Task StopAsync(string reason, CancellationToken cancellationToken)
    {
        ExpireActiveApprovalQueueItem();
        State.Status = RelaySessionStatus.Stopped;
        State.LastError = reason;
        State.PendingApproval = null;
        State.UpdatedAt = DateTimeOffset.Now;
        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "session.stopped", State.ActiveSide, reason),
            cancellationToken);
    }

    public bool TryResolveSessionApproval(
        RelayPendingApproval pendingApproval,
        out RelayApprovalDecision decision,
        out RelaySessionApprovalRule? matchedRule) =>
        RelayApprovalPolicy.TryResolveSessionDecision(State, pendingApproval, out decision, out matchedRule);

    public async Task SaveSessionApprovalRuleAsync(
        RelayPendingApproval pendingApproval,
        RelayApprovalDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision != RelayApprovalDecision.ApproveForSession ||
            string.IsNullOrWhiteSpace(pendingApproval.PolicyKey))
        {
            return;
        }

        State.SessionApprovalRules.RemoveAll(rule =>
            string.Equals(rule.PolicyKey, pendingApproval.PolicyKey, StringComparison.Ordinal));

        var rule = new RelaySessionApprovalRule(
            pendingApproval.PolicyKey,
            decision,
            pendingApproval.Category,
            pendingApproval.Title,
            pendingApproval.Message,
            DateTimeOffset.Now,
            pendingApproval.RiskLevel);

        State.SessionApprovalRules.Add(rule);
        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                "approval.session_rule.saved",
                pendingApproval.Side,
                $"Saved session approval rule for {pendingApproval.Title}.",
                pendingApproval.Payload),
            cancellationToken);
    }

    public async Task LogSessionApprovalRuleAppliedAsync(
        RelayPendingApproval pendingApproval,
        RelaySessionApprovalRule matchedRule,
        CancellationToken cancellationToken)
    {
        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                "approval.session_rule.applied",
                pendingApproval.Side,
                $"Applied saved session approval rule for {matchedRule.Title}.",
                pendingApproval.Payload),
            cancellationToken);
    }

    public async Task LogAutoApprovalModeAppliedAsync(
        RelayPendingApproval pendingApproval,
        CancellationToken cancellationToken)
    {
        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                "approval.auto_mode.applied",
                pendingApproval.Side,
                $"Auto-approved {pendingApproval.Title} because dangerous auto-approve mode is enabled.",
                pendingApproval.Payload),
            cancellationToken);
    }

    public async Task EnqueueApprovalAsync(
        RelayPendingApproval pendingApproval,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var queueItem = new RelayApprovalQueueItem(
            pendingApproval.Id,
            State.SessionId,
            State.CurrentTurn,
            pendingApproval.Side,
            pendingApproval.Category,
            pendingApproval.Title,
            pendingApproval.Message,
            pendingApproval.Payload,
            pendingApproval.PolicyKey,
            pendingApproval.RiskLevel,
            pendingApproval.CreatedAt,
            "pending",
            null);

        ReplaceApprovalQueueItem(queueItem);
        State.Status = RelaySessionStatus.AwaitingApproval;
        State.LastError = pendingApproval.Message;
        State.PendingApproval = pendingApproval;
        await PersistAndLogAsync(
            new RelayLogEvent(
                now,
                "approval.queue.enqueued",
                pendingApproval.Side,
                $"Queued approval for {pendingApproval.Title}.",
                pendingApproval.Payload),
            cancellationToken);
    }

    public async Task ResolvePendingApprovalAsync(
        RelayPendingApproval pendingApproval,
        RelayApprovalDecision decision,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var status = decision switch
        {
            RelayApprovalDecision.ApproveOnce => "approved_once",
            RelayApprovalDecision.ApproveForSession => "approved_session",
            RelayApprovalDecision.Deny => "denied",
            _ => "expired"
        };

        ReplaceApprovalQueueItem(new RelayApprovalQueueItem(
            pendingApproval.Id,
            State.SessionId,
            State.CurrentTurn,
            pendingApproval.Side,
            pendingApproval.Category,
            pendingApproval.Title,
            pendingApproval.Message,
            pendingApproval.Payload,
            pendingApproval.PolicyKey,
            pendingApproval.RiskLevel,
            pendingApproval.CreatedAt,
            status,
            now));

        if (string.Equals(State.PendingApproval?.Id, pendingApproval.Id, StringComparison.Ordinal))
        {
            State.PendingApproval = null;
        }

        if (State.Status == RelaySessionStatus.AwaitingApproval)
        {
            State.Status = RelaySessionStatus.Active;
        }

        if (decision != RelayApprovalDecision.Deny)
        {
            State.LastError = null;
        }

        await PersistAndLogAsync(
            new RelayLogEvent(
                now,
                "approval.queue.resolved",
                pendingApproval.Side,
                $"Resolved queued approval for {pendingApproval.Title} as {status}.",
                pendingApproval.Payload),
            cancellationToken);
    }

    public async Task ClearSessionApprovalRulesAsync(CancellationToken cancellationToken)
    {
        if (State.SessionApprovalRules.Count == 0)
        {
            return;
        }

        var clearedCount = State.SessionApprovalRules.Count;
        State.SessionApprovalRules.Clear();
        State.PendingApproval = null;
        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                "approval.session_rules.cleared",
                State.ActiveSide,
                $"Cleared {clearedCount} saved session approval rule(s)."),
            cancellationToken);
    }

    public async Task ResolveCurrentPendingApprovalAsync(
        RelayApprovalDecision decision,
        CancellationToken cancellationToken)
    {
        if (State.PendingApproval is null)
        {
            return;
        }

        var pendingApproval = State.PendingApproval;
        if (decision == RelayApprovalDecision.ApproveForSession)
        {
            await SaveSessionApprovalRuleAsync(pendingApproval, decision, cancellationToken);
        }

        await ResolvePendingApprovalAsync(pendingApproval, decision, cancellationToken);
        if (decision == RelayApprovalDecision.Deny || decision == RelayApprovalDecision.Cancel)
        {
            State.Status = RelaySessionStatus.Paused;
            State.LastError = $"Approval denied for {pendingApproval.Title}.";
            await PersistAndLogAsync(
                new RelayLogEvent(
                    DateTimeOffset.Now,
                    "session.paused",
                    pendingApproval.Side,
                    State.LastError,
                    pendingApproval.Payload),
                cancellationToken);
            return;
        }

        State.Status = RelaySessionStatus.Active;
        State.LastError = null;
        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                "session.resumed",
                pendingApproval.Side,
                $"Approval completed for {pendingApproval.Title}. Session can continue.",
                pendingApproval.Payload),
            cancellationToken);
    }

    public Task<BrokerAdvanceResult> AdvanceAsync(CancellationToken cancellationToken) =>
        AdvanceAsyncInternal(cancellationToken, allowCacheRegressionRecovery: true);

    private async Task<BrokerAdvanceResult> AdvanceAsyncInternal(
        CancellationToken cancellationToken,
        bool allowCacheRegressionRecovery)
    {
        if (State.Status != RelaySessionStatus.Active)
        {
            return new BrokerAdvanceResult(false, true, false, "Session is not active.", State);
        }

        if (string.IsNullOrWhiteSpace(State.PendingPrompt))
        {
            return await PauseWithResultAsync("No pending prompt exists for the active side.", cancellationToken);
        }

        if (!_adapters.TryGetValue(State.ActiveSide, out var sourceAdapter))
        {
            return await PauseWithResultAsync($"No adapter is registered for {State.ActiveSide}.", cancellationToken);
        }

        var rotationReason = EvaluateRotationReason();
        if (rotationReason is not null)
        {
            await RotateSessionAsync(rotationReason, cancellationToken);
        }

        State.NativeSessionHandles.TryGetValue(State.ActiveSide.ToString(), out var existingHandle);
        var turnContext = new RelayTurnContext(
            State.SessionId,
            State.CurrentTurn,
            State.ActiveSide,
            State.PendingPrompt,
            existingHandle);
        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "turn.started", State.ActiveSide, $"Turn {State.CurrentTurn} submitted.", State.PendingPrompt),
            cancellationToken);

        RelayAdapterResult turnResult;
        try
        {
            turnResult = await RunWithTurnTimeoutAsync(
                State.ActiveSide,
                "turn",
                token => sourceAdapter.RunTurnAsync(turnContext, token),
                cancellationToken);
        }
        catch (RelayTurnTimeoutException ex)
        {
            return await PauseWithResultAsync(ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            return await PauseWithResultAsync(
                $"Adapter turn failed on {State.ActiveSide}: {ex.GetType().Name}: {ex.Message}",
                cancellationToken);
        }

        CaptureSessionHandle(turnResult.SessionHandle);
        var budgetTrip = await CaptureUsageAsync(
            State.ActiveSide,
            turnResult.Usage,
            cancellationToken,
            isFallback: false,
            usageIsCumulative: turnResult.UsageIsCumulative,
            handle: turnResult.SessionHandle);
        if (budgetTrip is not null)
        {
            if (budgetTrip.Signal == "cache_regression")
            {
                if (!allowCacheRegressionRecovery)
                {
                    return await PauseWithResultAsync(
                        $"Cache regression persisted after planned rotation. {budgetTrip.Reason}",
                        cancellationToken);
                }

                State.RepairAttempts = 0;
                await RotateSessionAsync(
                    $"Cache regression triggered planned rotation. {budgetTrip.Reason}",
                    cancellationToken);
                return await AdvanceAsyncInternal(cancellationToken, allowCacheRegressionRecovery: false);
            }

            return await TryDowngradeTurnAsync(turnContext, budgetTrip, cancellationToken);
        }
        var reviewPendingApproval = await LogObservedActionsAsync(State.ActiveSide, turnResult.ObservedActions, cancellationToken);
        if (TryGetOutstandingApprovalRequest(turnResult.ObservedActions, out var approvalRequest))
        {
            return await AwaitApprovalAsync(State.ActiveSide, approvalRequest, cancellationToken);
        }
        if (reviewPendingApproval is not null)
        {
            return new BrokerAdvanceResult(
                false,
                true,
                false,
                $"Review required for {reviewPendingApproval.Title}.",
                State);
        }
        await LogDiagnosticsAsync(State.ActiveSide, turnResult.Diagnostics, cancellationToken);
        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "turn.completed", State.ActiveSide, $"Turn {State.CurrentTurn} completed.", turnResult.Output),
            cancellationToken);

        if (TryAcceptHandoff(turnResult.Output, out var handoff, out var failureReason))
        {
            return await CompleteHandoffAsync(handoff!, false, cancellationToken);
        }

        var lastInvalidOutput = turnResult.Output;
        while (true)
        {
            State.RepairAttempts++;
            await PersistAndLogAsync(
                new RelayLogEvent(DateTimeOffset.Now, "repair.requested", State.ActiveSide, failureReason!, lastInvalidOutput),
                cancellationToken);

            if (State.RepairAttempts > _options.MaxRepairAttempts)
            {
                return await PauseWithResultAsync(
                    $"Repair budget exceeded after {_options.MaxRepairAttempts} attempts. Last parser error: {failureReason}",
                    cancellationToken);
            }

            var repairPrompt = BuildRepairPrompt();
            State.NativeSessionHandles.TryGetValue(State.ActiveSide.ToString(), out var repairSessionHandle);
            var repairContext = new RelayRepairContext(
                State.SessionId,
                State.CurrentTurn,
                State.ActiveSide,
                State.PendingPrompt,
                lastInvalidOutput,
                repairPrompt,
                repairSessionHandle);
            RelayAdapterResult repairResult;
            try
            {
                repairResult = await RunWithTurnTimeoutAsync(
                    State.ActiveSide,
                    "repair",
                    token => sourceAdapter.RunRepairAsync(repairContext, token),
                    cancellationToken);
            }
            catch (RelayTurnTimeoutException ex)
            {
                return await PauseWithResultAsync(ex.Message, cancellationToken);
            }
            catch (Exception ex)
            {
                return await PauseWithResultAsync(
                    $"Adapter repair failed on {State.ActiveSide}: {ex.GetType().Name}: {ex.Message}",
                    cancellationToken);
            }

            CaptureSessionHandle(repairResult.SessionHandle);
            budgetTrip = await CaptureUsageAsync(
                State.ActiveSide,
                repairResult.Usage,
                cancellationToken,
                isFallback: false,
                usageIsCumulative: repairResult.UsageIsCumulative,
                handle: repairResult.SessionHandle);
            if (budgetTrip is not null)
            {
                if (budgetTrip.Signal == "cache_regression")
                {
                    if (!allowCacheRegressionRecovery)
                    {
                        return await PauseWithResultAsync(
                            $"Cache regression persisted after planned rotation. {budgetTrip.Reason}",
                            cancellationToken);
                    }

                    State.RepairAttempts = 0;
                    await RotateSessionAsync(
                        $"Cache regression triggered planned rotation during repair. {budgetTrip.Reason}",
                        cancellationToken);
                    return await AdvanceAsyncInternal(cancellationToken, allowCacheRegressionRecovery: false);
                }

                return await TryDowngradeRepairAsync(repairContext, budgetTrip, cancellationToken);
            }
            reviewPendingApproval = await LogObservedActionsAsync(State.ActiveSide, repairResult.ObservedActions, cancellationToken);
            if (TryGetOutstandingApprovalRequest(repairResult.ObservedActions, out var repairApprovalRequest))
            {
                return await AwaitApprovalAsync(State.ActiveSide, repairApprovalRequest, cancellationToken);
            }
            if (reviewPendingApproval is not null)
            {
                return new BrokerAdvanceResult(
                    false,
                    true,
                    false,
                    $"Review required for {reviewPendingApproval.Title}.",
                    State);
            }
            await LogDiagnosticsAsync(State.ActiveSide, repairResult.Diagnostics, cancellationToken);
            await PersistAndLogAsync(
                new RelayLogEvent(DateTimeOffset.Now, "repair.completed", State.ActiveSide, $"Repair attempt {State.RepairAttempts} completed.", repairResult.Output),
                cancellationToken);

            if (TryAcceptHandoff(repairResult.Output, out handoff, out failureReason))
            {
                return await CompleteHandoffAsync(handoff!, true, cancellationToken);
            }

            lastInvalidOutput = repairResult.Output;
        }
    }

    private async Task<BrokerAdvanceResult> CompleteHandoffAsync(
        HandoffEnvelope handoff,
        bool repaired,
        CancellationToken cancellationToken)
    {
        if (handoff.SessionId != State.SessionId)
        {
            return await PauseWithResultAsync(
                $"Session mismatch. Expected {State.SessionId}, got {handoff.SessionId}.",
                cancellationToken);
        }

        if (handoff.Turn != State.CurrentTurn)
        {
            return await PauseWithResultAsync(
                $"Turn mismatch. Expected {State.CurrentTurn}, got {handoff.Turn}.",
                cancellationToken);
        }

        if (handoff.RequiresHuman || !handoff.Ready)
        {
            return await PauseWithResultAsync(
                string.IsNullOrWhiteSpace(handoff.Reason)
                    ? "Handoff requested human intervention."
                    : handoff.Reason,
                cancellationToken);
        }

        var relayHash = HandoffParser.ComputeCanonicalHash(handoff);
        var relayKey = $"{handoff.SessionId}:{handoff.Target}:{handoff.Turn}:{relayHash}";
        if (State.AcceptedRelayKeys.Contains(relayKey, StringComparer.Ordinal))
        {
            return await PauseWithResultAsync("Duplicate handoff detected. Relay was not committed twice.", cancellationToken);
        }

        State.AcceptedRelayKeys.Add(relayKey);
        State.LastHandoff = handoff;
        State.LastHandoffHash = relayHash;
        State.PendingPrompt = handoff.Prompt;
        State.ActiveSide = handoff.Target;
        State.CurrentTurn++;
        State.TurnsSinceLastRotation++;
        State.RepairAttempts = 0;
        State.PendingApproval = null;
        State.LastError = null;
        State.UpdatedAt = DateTimeOffset.Now;

        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "handoff.accepted", handoff.Source, $"Accepted handoff to {handoff.Target}.", handoff.Prompt),
            cancellationToken);

        return new BrokerAdvanceResult(true, false, repaired, "Handoff accepted and queued for the opposite side.", State);
    }

    private bool TryAcceptHandoff(string rawOutput, out HandoffEnvelope? handoff, out string? failureReason)
    {
        var expectedTarget = State.ActiveSide == RelaySide.Codex ? RelaySide.Claude : RelaySide.Codex;
        if (!HandoffParser.TryParseWithFallback(
                rawOutput,
                State.PendingPrompt,
                State.ActiveSide,
                expectedTarget,
                State.SessionId,
                State.CurrentTurn,
                out handoff,
                out failureReason,
                out _))
        {
            return false;
        }

        if (handoff!.Source != State.ActiveSide)
        {
            failureReason = $"Handoff source mismatch. Expected {State.ActiveSide}, got {handoff.Source}.";
            handoff = null;
            return false;
        }

        return true;
    }

    private async Task<BrokerAdvanceResult> PauseWithResultAsync(string reason, CancellationToken cancellationToken)
    {
        await PauseAsync(reason, cancellationToken);
        return new BrokerAdvanceResult(false, true, false, reason, State);
    }

    private async Task<BrokerAdvanceResult> AwaitApprovalAsync(
        RelaySide side,
        RelayObservedAction approvalRequest,
        CancellationToken cancellationToken)
    {
        var pendingApproval = new RelayPendingApproval(
            Guid.NewGuid().ToString("N"),
            side,
            approvalRequest.Category ?? "transport",
            approvalRequest.Title ?? approvalRequest.EventType,
            approvalRequest.Message,
            approvalRequest.Payload,
            null,
            DateTimeOffset.Now);

        await EnqueueApprovalAsync(pendingApproval, cancellationToken);

        return new BrokerAdvanceResult(
            false,
            true,
            false,
            $"Approval requested by {side}: {approvalRequest.Message}",
            State);
    }

    private void ExpireActiveApprovalQueueItem()
    {
        if (State.PendingApproval is null)
        {
            return;
        }

        ReplaceApprovalQueueItem(new RelayApprovalQueueItem(
            State.PendingApproval.Id,
            State.SessionId,
            State.CurrentTurn,
            State.PendingApproval.Side,
            State.PendingApproval.Category,
            State.PendingApproval.Title,
            State.PendingApproval.Message,
            State.PendingApproval.Payload,
            State.PendingApproval.PolicyKey,
            State.PendingApproval.RiskLevel,
            State.PendingApproval.CreatedAt,
            "expired",
            DateTimeOffset.Now));
    }

    private void ReplaceApprovalQueueItem(RelayApprovalQueueItem queueItem)
    {
        var existingIndex = State.ApprovalQueue.FindIndex(item =>
            string.Equals(item.Id, queueItem.Id, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            State.ApprovalQueue[existingIndex] = queueItem;
        }
        else
        {
            State.ApprovalQueue.Add(queueItem);
        }
    }

    private async Task PersistAndLogAsync(RelayLogEvent logEvent, CancellationToken cancellationToken)
    {
        State.UpdatedAt = DateTimeOffset.Now;
        await _sessionStore.SaveAsync(State, cancellationToken);
        await _eventLogWriter.AppendAsync(State.SessionId, logEvent, cancellationToken);
    }

    private void CaptureSessionHandle(string? sessionHandle)
    {
        if (string.IsNullOrWhiteSpace(sessionHandle))
        {
            return;
        }

        var sideKey = State.ActiveSide.ToString();
        if (State.NativeSessionHandles.TryGetValue(sideKey, out var previousHandle) &&
            !string.IsNullOrWhiteSpace(previousHandle) &&
            !string.Equals(previousHandle, sessionHandle, StringComparison.Ordinal))
        {
            State.LastCumulativeByHandle.Remove(previousHandle);
        }

        State.NativeSessionHandles[sideKey] = sessionHandle;
    }

    private async Task LogDiagnosticsAsync(RelaySide side, string? diagnostics, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(diagnostics))
        {
            return;
        }

        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "adapter.diagnostics", side, $"Diagnostics captured for {side}.", diagnostics),
            cancellationToken);
    }

    private async Task<RelayPendingApproval?> LogObservedActionsAsync(
        RelaySide side,
        IReadOnlyList<RelayObservedAction>? observedActions,
        CancellationToken cancellationToken)
    {
        if (observedActions is null || observedActions.Count == 0)
        {
            return null;
        }

        RelayPendingApproval? reviewPendingApproval = null;

        foreach (var action in observedActions)
        {
            if (string.IsNullOrWhiteSpace(action.EventType) || string.IsNullOrWhiteSpace(action.Message))
            {
                continue;
            }

            await PersistAndLogAsync(
                new RelayLogEvent(
                    DateTimeOffset.Now,
                    action.EventType,
                    side,
                    action.Message,
                    action.Payload),
                cancellationToken);

            reviewPendingApproval ??= await LogToolPolicyGapAdvisoryAsync(side, action, cancellationToken);
        }

        return reviewPendingApproval;
    }

    private async Task<RelayPendingApproval?> LogToolPolicyGapAdvisoryAsync(
        RelaySide side,
        RelayObservedAction action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.Category))
        {
            return null;
        }

        if (!string.Equals(action.EventType, "tool.invoked", StringComparison.Ordinal) &&
            !action.EventType.EndsWith(".requested", StringComparison.Ordinal))
        {
            return null;
        }

        if (action.Category is not ("mcp" or "web"))
        {
            return null;
        }

        var title = string.IsNullOrWhiteSpace(action.Title) ? action.Category : action.Title;
        var reviewMessage = $"Observed {title} activity without broker-routed approval. Operator review is required before continuing this session.";
        var toolSummary = RelayApprovalPolicy.DescribeToolSummary(action.Category, title);
        if (!string.IsNullOrWhiteSpace(toolSummary))
        {
            reviewMessage = $"{reviewMessage}{Environment.NewLine}{toolSummary}";
        }

        reviewMessage = $"{reviewMessage}{Environment.NewLine}{RelayApprovalPolicy.DescribeRiskLevel(RelayApprovalPolicy.GetToolRiskLevel(action.Category))}";
        var reviewPolicy = RelayApprovalPolicy.DescribeToolReviewPolicy(action.Category);
        if (!string.IsNullOrWhiteSpace(reviewPolicy))
        {
            reviewMessage = $"{reviewMessage}{Environment.NewLine}{reviewPolicy}";
        }

        var policyKey = RelayApprovalPolicy.BuildToolReviewPolicyKey(action.Category, title);
        var pendingApproval = new RelayPendingApproval(
            Guid.NewGuid().ToString("N"),
            side,
            action.Category,
            RelayApprovalPolicy.GetApprovalTitle(action.Category),
            reviewMessage,
            action.Payload,
            policyKey,
            DateTimeOffset.Now,
            RelayApprovalPolicy.GetToolRiskLevel(action.Category));

        if (TryResolveSessionApproval(pendingApproval, out var decision, out var matchedRule))
        {
            await LogSessionApprovalRuleAppliedAsync(pendingApproval, matchedRule!, cancellationToken);
            await ResolvePendingApprovalAsync(pendingApproval, decision, cancellationToken);
            return null;
        }

        if (RelayApprovalPolicy.TryResolveDefaultToolReviewDecision(
                action.Category,
                title,
                action.Payload,
                out var defaultDecision,
                out var defaultReason))
        {
            await PersistAndLogAsync(
                new RelayLogEvent(
                    DateTimeOffset.Now,
                    "policy.applied",
                    side,
                    $"{reviewMessage}{Environment.NewLine}Policy: {defaultReason}",
                    action.Payload),
                cancellationToken);

            await PersistAndLogAsync(
                new RelayLogEvent(
                    DateTimeOffset.Now,
                    defaultDecision is RelayApprovalDecision.ApproveOnce or RelayApprovalDecision.ApproveForSession
                        ? "approval.granted"
                        : "approval.denied",
                    side,
                    $"{reviewMessage}{Environment.NewLine}Policy: {defaultReason}",
                    action.Payload),
                cancellationToken);
            return null;
        }

        var advisoryKey = $"{side}:{policyKey}";
        if (State.PolicyGapAdvisoriesFired.Contains(advisoryKey, StringComparer.Ordinal))
        {
            return null;
        }

        State.PolicyGapAdvisoriesFired.Add(advisoryKey);
        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                $"{action.Category}.review_required",
                side,
                $"{reviewMessage}{Environment.NewLine}Current product path treats {action.Category} activity as audit-visible but operator review is still required for policy hardening.",
                action.Payload),
            cancellationToken);
        await EnqueueApprovalAsync(pendingApproval, cancellationToken);
        return pendingApproval;
    }

    private static bool TryGetOutstandingApprovalRequest(
        IReadOnlyList<RelayObservedAction>? observedActions,
        out RelayObservedAction approvalRequest)
    {
        approvalRequest = default!;
        if (observedActions is null || observedActions.Count == 0)
        {
            return false;
        }

        foreach (var action in observedActions)
        {
            if (string.Equals(action.EventType, "approval.granted", StringComparison.Ordinal) ||
                string.Equals(action.EventType, "approval.denied", StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (var action in observedActions)
        {
            if (string.Equals(action.EventType, "approval.requested", StringComparison.Ordinal))
            {
                approvalRequest = action;
                return true;
            }
        }

        return false;
    }

    private async Task<BudgetTrip?> CaptureUsageAsync(
        RelaySide side,
        RelayUsageMetrics? usage,
        CancellationToken cancellationToken,
        bool isFallback = false,
        bool usageIsCumulative = false,
        string? handle = null)
    {
        if (usage is null || !usage.HasValues)
        {
            if (!isFallback)
            {
                await PersistAndLogAsync(
                    new RelayLogEvent(
                        DateTimeOffset.Now,
                        "adapter.usage_unknown",
                        side,
                        $"No usage reported for {side} turn {State.CurrentTurn}. Token spend for this turn is untracked in cumulative totals."),
                    cancellationToken);
            }

            return null;
        }

        var effectiveUsage = usage;
        if (usageIsCumulative && !string.IsNullOrWhiteSpace(handle))
        {
            State.LastCumulativeByHandle.TryGetValue(handle, out var previousCumulative);
            effectiveUsage = ComputeDelta(previousCumulative, usage);
            State.LastCumulativeByHandle[handle] = usage;
        }

        State.LastUsageBySide[side.ToString()] = effectiveUsage;
        State.TotalInputTokens += effectiveUsage.InputTokens ?? 0;
        State.TotalOutputTokens += effectiveUsage.OutputTokens ?? 0;
        State.TotalCacheCreationInputTokens += effectiveUsage.CacheCreationInputTokens ?? 0;
        State.TotalCacheReadInputTokens += effectiveUsage.CacheReadInputTokens ?? 0;
        var costDelta = effectiveUsage.CostUsd ?? 0;
        if (side == RelaySide.Claude)
        {
            State.TotalCostClaudeUsd += costDelta;
        }
        else if (side == RelaySide.Codex)
        {
            State.TotalCostCodexUsd += costDelta;
        }

        var cacheReadRatio = CalculateCacheReadRatio(effectiveUsage);
        if (!isFallback &&
            side == RelaySide.Claude &&
            State.TurnsSinceLastRotation >= 2 &&
            cacheReadRatio.HasValue)
        {
            State.CacheReadRatioByTurn.Add(cacheReadRatio.Value);
            var sideKey = side.ToString();
            State.ConsecutiveLowCacheTurnsBySide[sideKey] = cacheReadRatio.Value < _options.CacheReadRatioFloor
                ? GetLowCacheTurnCount(side) + 1
                : 0;
        }

        var codexRateCardSummary = side == RelaySide.Codex
            ? $", rate_card={CodexPricing.DescribeRateCard()}"
            : string.Empty;

        var summary =
            usageIsCumulative && !string.IsNullOrWhiteSpace(handle)
                ? $"Usage captured for {side}. cumulative(input={usage.InputTokens?.ToString() ?? "?"}, output={usage.OutputTokens?.ToString() ?? "?"}, cache_read={usage.CacheReadInputTokens?.ToString() ?? "?"}, cache_create={usage.CacheCreationInputTokens?.ToString() ?? "?"}, cost_usd={(usage.CostUsd?.ToString("F4") ?? "?")}); delta(input={effectiveUsage.InputTokens?.ToString() ?? "?"}, output={effectiveUsage.OutputTokens?.ToString() ?? "?"}, cache_read={effectiveUsage.CacheReadInputTokens?.ToString() ?? "?"}, cache_create={effectiveUsage.CacheCreationInputTokens?.ToString() ?? "?"}, cost_usd={(effectiveUsage.CostUsd?.ToString("F4") ?? "?")}, model={effectiveUsage.Model ?? "unknown"}{codexRateCardSummary}); handle={handle}"
                : $"Usage captured for {side}. input={effectiveUsage.InputTokens?.ToString() ?? "?"}, " +
                  $"output={effectiveUsage.OutputTokens?.ToString() ?? "?"}, " +
                  $"cache_read={effectiveUsage.CacheReadInputTokens?.ToString() ?? "?"}, " +
                  $"cache_create={effectiveUsage.CacheCreationInputTokens?.ToString() ?? "?"}, " +
                  $"cost_usd={(effectiveUsage.CostUsd?.ToString("F4") ?? "?")}, " +
                  $"model={effectiveUsage.Model ?? "unknown"}{codexRateCardSummary}";

        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "adapter.usage", side, summary, usage.RawJson),
            cancellationToken);

        await LogCodexPricingFallbackAsync(side, effectiveUsage, cancellationToken);
        await LogCodexRateCardStaleAsync(side, cancellationToken);
        await LogCostAvailabilitySignalsAsync(side, effectiveUsage, cancellationToken);
        await LogCacheInflationSignalAsync(side, effectiveUsage, cancellationToken);
        await LogClaudeCostCeilingDisabledAsync(side, effectiveUsage, cancellationToken);
        if (await TryRotateForClaudeCostCeilingAsync(side, effectiveUsage, cancellationToken))
        {
            return null;
        }

        return await EvaluateBudgetSignalsAsync(side, effectiveUsage, cacheReadRatio, cancellationToken);
    }

    private async Task<BudgetTrip?> EvaluateBudgetSignalsAsync(
        RelaySide side,
        RelayUsageMetrics usage,
        double? cacheReadRatio,
        CancellationToken cancellationToken)
    {
        if (State.TotalOutputTokens >= _options.MaxCumulativeOutputTokens)
        {
            var reason = await TripBudgetAsync(
                side,
                "output_budget_exceeded",
                $"Cumulative output token budget exceeded for {side}. Total output tokens: {State.TotalOutputTokens}.",
                usage.RawJson,
                cancellationToken);
            return new BudgetTrip("output_budget_exceeded", reason);
        }

        if (side == RelaySide.Claude &&
            cacheReadRatio.HasValue &&
            GetLowCacheTurnCount(side) >= _options.ConsecutiveLowCacheTurnsThreshold)
        {
            var reason = await TripBudgetAsync(
                side,
                "cache_regression",
                $"Cache read ratio stayed below {_options.CacheReadRatioFloor:F2} for {GetLowCacheTurnCount(side)} consecutive turns on {side}.",
                usage.RawJson,
                cancellationToken);
            return new BudgetTrip("cache_regression", reason);
        }

        return null;
    }

    private async Task<string> TripBudgetAsync(
        RelaySide side,
        string signal,
        string reason,
        string? payload,
        CancellationToken cancellationToken)
    {
        State.LastBudgetSignal = signal;
        State.UpdatedAt = DateTimeOffset.Now;

        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "budget.tripped", side, reason, payload),
            cancellationToken);

        return reason;
    }

    private static double? CalculateCacheReadRatio(RelayUsageMetrics usage)
    {
        var inputTokens = usage.InputTokens ?? 0;
        var cacheReadTokens = usage.CacheReadInputTokens ?? 0;
        var cacheCreateTokens = usage.CacheCreationInputTokens ?? 0;
        var denominator = inputTokens + cacheReadTokens + cacheCreateTokens;
        if (denominator <= 0)
        {
            return null;
        }

        return cacheReadTokens / (double)denominator;
    }

    private async Task LogCostAvailabilitySignalsAsync(
        RelaySide side,
        RelayUsageMetrics usage,
        CancellationToken cancellationToken)
    {
        if (side != RelaySide.Claude)
        {
            return;
        }

        if (State.ClaudeCostAbsentAdvisoryFired)
        {
            return;
        }

        var turnHadRealTokens =
            (usage.InputTokens ?? 0) > 0 ||
            (usage.OutputTokens ?? 0) > 0 ||
            (usage.CacheCreationInputTokens ?? 0) > 0 ||
            (usage.CacheReadInputTokens ?? 0) > 0;

        if (!turnHadRealTokens || (usage.CostUsd is not null && usage.CostUsd > 0d))
        {
            return;
        }

        State.ClaudeCostAbsentAdvisoryFired = true;
        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                "claude.cost.absent",
                side,
                $"Claude reported token usage but no positive CLI-estimated cost. This can happen on subscription plans, provider-routed deployments, or CLI versions without cost reporting. claude_cli_version={usage.CliVersion ?? "unknown"}",
                usage.RawJson),
            cancellationToken);
    }

    private async Task LogCodexPricingFallbackAsync(
        RelaySide side,
        RelayUsageMetrics usage,
        CancellationToken cancellationToken)
    {
        if (side != RelaySide.Codex ||
            string.IsNullOrWhiteSpace(usage.PricingFallbackReason) ||
            State.CodexPricingFallbackAdvisoryFired)
        {
            return;
        }

        State.CodexPricingFallbackAdvisoryFired = true;
        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                "codex.pricing.fallback",
                side,
                $"{usage.PricingFallbackReason}. Rate card: {CodexPricing.DescribeRateCard()}. codex_model={usage.Model ?? "unknown"}",
                usage.RawJson),
            cancellationToken);
    }

    private async Task LogClaudeCostCeilingDisabledAsync(
        RelaySide side,
        RelayUsageMetrics usage,
        CancellationToken cancellationToken)
    {
        if (side != RelaySide.Claude ||
            !_options.MaxClaudeCostUsd.HasValue ||
            State.ClaudeCostCeilingDisabledAdvisoryFired ||
            string.IsNullOrWhiteSpace(usage.AuthMethod) ||
            ClaudeAuthMethod.IsApiKey(usage.AuthMethod))
        {
            return;
        }

        State.ClaudeCostCeilingDisabledAdvisoryFired = true;
        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                "cost.ceiling.disabled",
                side,
                $"Configured Claude cost ceiling is inactive for this session segment because auth is not api-key. ceiling_usd={_options.MaxClaudeCostUsd.Value:F4}, observed_auth_method={usage.AuthMethod}.",
                usage.RawJson),
            cancellationToken);
    }

    private async Task LogCacheInflationSignalAsync(
        RelaySide side,
        RelayUsageMetrics usage,
        CancellationToken cancellationToken)
    {
        if (side != RelaySide.Claude)
        {
            return;
        }

        if (State.TurnsSinceLastRotation < 1 || State.CacheInflationAdvisoryFired)
        {
            return;
        }

        var cacheCreationTokens = usage.CacheCreationInputTokens ?? 0;
        var nonCachedInputTokens = usage.InputTokens ?? 0;
        if (cacheCreationTokens < SuspiciousClaudeCacheCreationTokens)
        {
            return;
        }

        if (nonCachedInputTokens > 0 && cacheCreationTokens <= nonCachedInputTokens * 2)
        {
            return;
        }

        State.CacheInflationAdvisoryFired = true;
        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                "cache.inflation.suspected",
                side,
                $"cache_creation_input_tokens={cacheCreationTokens}, input_tokens={nonCachedInputTokens}, claude_cli_version={usage.CliVersion ?? "unknown"}. Possible Claude CLI cache-creation inflation (see Anthropic issue #46917). Further inflation this session will remain visible only in adapter.usage totals.",
                usage.RawJson),
            cancellationToken);
    }

    private async Task LogCodexRateCardStaleAsync(
        RelaySide side,
        CancellationToken cancellationToken)
    {
        if (side != RelaySide.Codex || State.CodexRateCardStaleAdvisoryFired)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ageDays = today.DayNumber - CodexPricing.RateCardAsOf.DayNumber;
        if (ageDays < 180)
        {
            return;
        }

        State.CodexRateCardStaleAdvisoryFired = true;
        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                "codex.rate_card.stale",
                side,
                $"Codex rate card {CodexPricing.DescribeRateCard()} is {ageDays} days old. Local Codex cost estimates for this session may be inaccurate until the rate card is refreshed."),
            cancellationToken);
    }

    private async Task<bool> TryRotateForClaudeCostCeilingAsync(
        RelaySide side,
        RelayUsageMetrics usage,
        CancellationToken cancellationToken)
    {
        if (side != RelaySide.Claude || !_options.MaxClaudeCostUsd.HasValue)
        {
            return false;
        }

        if (!ClaudeAuthMethod.IsApiKey(usage.AuthMethod))
        {
            return false;
        }

        var segmentCost = Math.Max(0d, State.TotalCostClaudeUsd - State.ClaudeCostAtLastRotationUsd);
        if (segmentCost < _options.MaxClaudeCostUsd.Value)
        {
            return false;
        }

        var reason = await TripBudgetAsync(
            side,
            "claude_cost_ceiling",
            $"Claude api-key estimated cost ceiling reached for this session segment. segment_cost_usd={segmentCost:F4}, ceiling_usd={_options.MaxClaudeCostUsd.Value:F4}. Rotating session.",
            usage.RawJson,
            cancellationToken);
        await RotateSessionAsync(reason, cancellationToken);
        return true;
    }

    private string? EvaluateRotationReason()
    {
        if (State.TurnsSinceLastRotation >= _options.MaxTurnsPerSession)
        {
            return $"Planned rotation triggered after {State.TurnsSinceLastRotation} turns.";
        }

        var sessionAge = DateTimeOffset.Now - State.SessionStartedAt;
        if (sessionAge >= _options.MaxSessionDuration)
        {
            return $"Planned rotation triggered after {sessionAge:mm\\:ss}.";
        }

        return null;
    }

    private async Task RotateSessionAsync(string reason, CancellationToken cancellationToken)
    {
        await WriteRollingSummaryAsync(reason, cancellationToken);

        RemoveActiveSessionHandle();
        var prunedBaselineCount = PruneDeadHandleBaselines();
        State.ConsecutiveLowCacheTurnsBySide.Clear();
        State.SessionStartedAt = DateTimeOffset.Now;
        State.TurnsSinceLastRotation = 0;
        State.RotationCount++;
        State.ClaudeCostAtLastRotationUsd = State.TotalCostClaudeUsd;
        State.LastBudgetSignal = null;
        State.CacheInflationAdvisoryFired = false;
        State.ClaudeCostAbsentAdvisoryFired = false;
        State.ClaudeCostCeilingDisabledAdvisoryFired = false;
        State.CodexPricingFallbackAdvisoryFired = false;
        State.CodexRateCardStaleAdvisoryFired = false;
        State.PolicyGapAdvisoriesFired.Clear();
        State.UpdatedAt = DateTimeOffset.Now;

        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "rotation.triggered", State.ActiveSide, reason, State.PendingPrompt),
            cancellationToken);

        if (prunedBaselineCount > 0)
        {
            await PersistAndLogAsync(
                new RelayLogEvent(
                    DateTimeOffset.Now,
                    "handle.baseline.pruned",
                    State.ActiveSide,
                    $"Pruned {prunedBaselineCount} stale cumulative-handle baseline(s) on rotation."),
                cancellationToken);
        }
    }

    private int GetLowCacheTurnCount(RelaySide side) =>
        State.ConsecutiveLowCacheTurnsBySide.TryGetValue(side.ToString(), out var count)
            ? count
            : 0;

    private async Task<BrokerAdvanceResult> TryDowngradeTurnAsync(
        RelayTurnContext turnContext,
        BudgetTrip budgetTrip,
        CancellationToken cancellationToken)
    {
        if (!_fallbackAdapters.TryGetValue(turnContext.SourceSide, out var fallbackAdapter))
        {
            return await PauseWithResultAsync(budgetTrip.Reason, cancellationToken);
        }

        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "downgrade.started", turnContext.SourceSide, $"Retrying turn {turnContext.TurnNumber} through bounded fallback because of {budgetTrip.Signal}.", turnContext.Prompt),
            cancellationToken);

        RemoveActiveSessionHandle();
        State.Status = RelaySessionStatus.Active;
        var fallbackContext = new RelayTurnContext(
            turnContext.SessionId,
            turnContext.TurnNumber,
            turnContext.SourceSide,
            turnContext.Prompt,
            ExistingSessionHandle: null);

        RelayAdapterResult fallbackResult;
        try
        {
            fallbackResult = await RunWithTurnTimeoutAsync(
                turnContext.SourceSide,
                "bounded fallback turn",
                token => fallbackAdapter.RunTurnAsync(fallbackContext, token),
                cancellationToken);
        }
        catch (RelayTurnTimeoutException ex)
        {
            return await PauseWithResultAsync(ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            return await PauseWithResultAsync($"Fallback downgrade failed: {ex.Message}", cancellationToken);
        }

        CaptureSessionHandle(fallbackResult.SessionHandle);
        var fallbackBudgetTrip = await CaptureFallbackUsageAsync(
            turnContext.SourceSide,
            fallbackResult,
            cancellationToken);
        var reviewPendingApproval = await LogObservedActionsAsync(turnContext.SourceSide, fallbackResult.ObservedActions, cancellationToken);
        if (TryGetOutstandingApprovalRequest(fallbackResult.ObservedActions, out var fallbackApprovalRequest))
        {
            return await AwaitApprovalAsync(turnContext.SourceSide, fallbackApprovalRequest, cancellationToken);
        }
        if (reviewPendingApproval is not null)
        {
            return new BrokerAdvanceResult(
                false,
                true,
                false,
                $"Review required for {reviewPendingApproval.Title}.",
                State);
        }
        await LogDiagnosticsAsync(turnContext.SourceSide, fallbackResult.Diagnostics, cancellationToken);
        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "downgrade.completed", turnContext.SourceSide, $"Bounded fallback completed for turn {turnContext.TurnNumber}.", fallbackResult.Output),
            cancellationToken);

        if (fallbackBudgetTrip is not null)
        {
            return await PauseWithResultAsync(
                $"Fallback downgrade also exceeded budget. {fallbackBudgetTrip.Reason}",
                cancellationToken);
        }

        if (TryAcceptHandoff(fallbackResult.Output, out var handoff, out var failureReason))
        {
            return await CompleteHandoffAsync(handoff!, false, cancellationToken);
        }

        return await PauseWithResultAsync(
            $"Fallback downgrade returned invalid handoff. {failureReason}",
            cancellationToken);
    }

    private async Task<BrokerAdvanceResult> TryDowngradeRepairAsync(
        RelayRepairContext repairContext,
        BudgetTrip budgetTrip,
        CancellationToken cancellationToken)
    {
        if (!_fallbackAdapters.TryGetValue(repairContext.SourceSide, out var fallbackAdapter))
        {
            return await PauseWithResultAsync(budgetTrip.Reason, cancellationToken);
        }

        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "downgrade.started", repairContext.SourceSide, $"Retrying repair for turn {repairContext.TurnNumber} through bounded fallback because of {budgetTrip.Signal}.", repairContext.RepairPrompt),
            cancellationToken);

        RemoveActiveSessionHandle();
        State.Status = RelaySessionStatus.Active;
        var fallbackContext = new RelayRepairContext(
            repairContext.SessionId,
            repairContext.TurnNumber,
            repairContext.SourceSide,
            repairContext.OriginalPrompt,
            repairContext.OriginalOutput,
            repairContext.RepairPrompt,
            ExistingSessionHandle: null);

        RelayAdapterResult fallbackResult;
        try
        {
            fallbackResult = await RunWithTurnTimeoutAsync(
                repairContext.SourceSide,
                "bounded fallback repair",
                token => fallbackAdapter.RunRepairAsync(fallbackContext, token),
                cancellationToken);
        }
        catch (RelayTurnTimeoutException ex)
        {
            return await PauseWithResultAsync(ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            return await PauseWithResultAsync($"Fallback downgrade failed during repair: {ex.Message}", cancellationToken);
        }

        CaptureSessionHandle(fallbackResult.SessionHandle);
        var fallbackBudgetTrip = await CaptureFallbackUsageAsync(
            repairContext.SourceSide,
            fallbackResult,
            cancellationToken);
        var reviewPendingApproval = await LogObservedActionsAsync(repairContext.SourceSide, fallbackResult.ObservedActions, cancellationToken);
        if (TryGetOutstandingApprovalRequest(fallbackResult.ObservedActions, out var fallbackRepairApprovalRequest))
        {
            return await AwaitApprovalAsync(repairContext.SourceSide, fallbackRepairApprovalRequest, cancellationToken);
        }
        if (reviewPendingApproval is not null)
        {
            return new BrokerAdvanceResult(
                false,
                true,
                false,
                $"Review required for {reviewPendingApproval.Title}.",
                State);
        }
        await LogDiagnosticsAsync(repairContext.SourceSide, fallbackResult.Diagnostics, cancellationToken);
        await PersistAndLogAsync(
            new RelayLogEvent(DateTimeOffset.Now, "downgrade.completed", repairContext.SourceSide, $"Bounded fallback repair completed for turn {repairContext.TurnNumber}.", fallbackResult.Output),
            cancellationToken);

        if (fallbackBudgetTrip is not null)
        {
            return await PauseWithResultAsync(
                $"Fallback downgrade repair also exceeded budget. {fallbackBudgetTrip.Reason}",
                cancellationToken);
        }

        if (TryAcceptHandoff(fallbackResult.Output, out var handoff, out var failureReason))
        {
            return await CompleteHandoffAsync(handoff!, true, cancellationToken);
        }

        return await PauseWithResultAsync(
            $"Fallback downgrade repair returned invalid handoff. {failureReason}",
            cancellationToken);
    }

    private async Task<BudgetTrip?> CaptureFallbackUsageAsync(
        RelaySide side,
        RelayAdapterResult result,
        CancellationToken cancellationToken)
    {
        var usage = result.Usage;
        if (usage is null || !usage.HasValues)
        {
            await PersistAndLogAsync(
                new RelayLogEvent(
                    DateTimeOffset.Now,
                    "downgrade.usage_unknown",
                    side,
                    "Bounded fallback completed without usage metrics. Actual token spend for the retry is untracked."),
                cancellationToken);
            return null;
        }

        await PersistAndLogAsync(
            new RelayLogEvent(
                DateTimeOffset.Now,
                "downgrade.usage_captured",
                side,
                "Bounded fallback returned usage metrics. Retry spend has been added to cumulative broker totals.",
                usage.RawJson),
            cancellationToken);

        return await CaptureUsageAsync(
            side,
            usage,
            cancellationToken,
            isFallback: true,
            usageIsCumulative: result.UsageIsCumulative,
            handle: result.SessionHandle);
    }

    private void RemoveActiveSessionHandle()
    {
        if (State.NativeSessionHandles.TryGetValue(State.ActiveSide.ToString(), out var existingHandle) &&
            !string.IsNullOrWhiteSpace(existingHandle))
        {
            State.LastCumulativeByHandle.Remove(existingHandle);
        }

        State.NativeSessionHandles.Remove(State.ActiveSide.ToString());
    }

    private int PruneDeadHandleBaselines()
    {
        if (State.LastCumulativeByHandle.Count == 0)
        {
            return 0;
        }

        var liveHandles = State.NativeSessionHandles.Values
            .Where(handle => !string.IsNullOrWhiteSpace(handle))
            .ToHashSet(StringComparer.Ordinal);
        var staleHandles = State.LastCumulativeByHandle.Keys
            .Where(handle => !liveHandles.Contains(handle))
            .ToArray();

        foreach (var handle in staleHandles)
        {
            State.LastCumulativeByHandle.Remove(handle);
        }

        return staleHandles.Length;
    }

    private static RelayUsageMetrics ComputeDelta(RelayUsageMetrics? previous, RelayUsageMetrics current)
    {
        if (previous is null)
        {
            return current;
        }

        static long? DeltaLong(long? now, long? before) =>
            now.HasValue ? Math.Max(0, now.Value - (before ?? 0)) : null;

        static double? DeltaDouble(double? now, double? before) =>
            now.HasValue ? Math.Max(0, now.Value - (before ?? 0)) : null;

        return new RelayUsageMetrics(
            DeltaLong(current.InputTokens, previous.InputTokens),
            DeltaLong(current.OutputTokens, previous.OutputTokens),
            DeltaLong(current.CacheCreationInputTokens, previous.CacheCreationInputTokens),
            DeltaLong(current.CacheReadInputTokens, previous.CacheReadInputTokens),
            DeltaDouble(current.CostUsd, previous.CostUsd),
            current.RawJson,
            current.Model,
            current.ModelUsageUsd,
            current.PricingFallbackReason,
            current.CliVersion,
            current.AuthMethod);
    }

    private async Task WriteRollingSummaryAsync(string rotationReason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(State.SessionId))
        {
            return;
        }

        var segmentNumber = State.RotationCount + 1;
        var sessionId = State.SessionId;
        var activeSide = State.ActiveSide;
        string? path = null;
        try
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexClaudeRelayMvp",
                "summaries");
            Directory.CreateDirectory(baseDir);
            path = Path.Combine(baseDir, $"{sessionId}-segment-{segmentNumber}.md");
            var markdown = BuildRollingSummaryMarkdown(segmentNumber, rotationReason);
            await File.WriteAllTextAsync(path, markdown, System.Text.Encoding.UTF8, cancellationToken);
            var bytes = System.Text.Encoding.UTF8.GetByteCount(markdown);

            var payload =
                "{" +
                $"\"path\":\"{EscapeJsonString(path)}\"," +
                $"\"bytes\":{bytes}," +
                $"\"segment\":{segmentNumber}," +
                $"\"session_id\":\"{EscapeJsonString(sessionId)}\"," +
                $"\"turns\":{State.TurnsSinceLastRotation}," +
                $"\"cost_claude_usd\":{State.TotalCostClaudeUsd.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"\"cost_codex_usd\":{State.TotalCostCodexUsd.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}" +
                "}";

            await PersistAndLogAsync(
                new RelayLogEvent(
                    DateTimeOffset.Now,
                    "summary.generated",
                    activeSide,
                    $"Rolling summary segment {segmentNumber} written ({bytes} bytes) for session {sessionId}.",
                    payload),
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or PathTooLongException)
        {
            await PersistAndLogAsync(
                new RelayLogEvent(
                    DateTimeOffset.Now,
                    "summary.failed",
                    activeSide,
                    $"Rolling summary segment {segmentNumber} failed for session {sessionId}: {ex.GetType().Name}: {ex.Message}. path={path ?? "(not resolved)"}"),
                cancellationToken);
        }
    }

    private string BuildRollingSummaryMarkdown(int segmentNumber, string rotationReason)
    {
        var now = DateTimeOffset.Now;
        string handoffBlock;
        if (State.LastHandoff is { } h)
        {
            handoffBlock =
                $"- source: {h.Source}{Environment.NewLine}" +
                $"- target: {h.Target}{Environment.NewLine}" +
                $"- turn: {h.Turn}{Environment.NewLine}" +
                $"- ready: {h.Ready}{Environment.NewLine}" +
                $"- reason: {(string.IsNullOrWhiteSpace(h.Reason) ? "(none)" : h.Reason)}";
        }
        else
        {
            handoffBlock = "- (no handoff captured this segment)";
        }

        var pendingPrompt = string.IsNullOrWhiteSpace(State.PendingPrompt) ? "(none)" : State.PendingPrompt;

        return
            $"# Session {State.SessionId} — segment {segmentNumber}{Environment.NewLine}{Environment.NewLine}" +
            $"- Closed at: {now:O}{Environment.NewLine}" +
            $"- Segment started at: {State.SessionStartedAt:O}{Environment.NewLine}" +
            $"- Rotation reason: {rotationReason}{Environment.NewLine}" +
            $"- Turns in this segment: {State.TurnsSinceLastRotation}{Environment.NewLine}" +
            $"- Active side at rotation: {State.ActiveSide}{Environment.NewLine}{Environment.NewLine}" +
            $"## Cumulative totals{Environment.NewLine}" +
            $"- input_tokens: {State.TotalInputTokens}{Environment.NewLine}" +
            $"- output_tokens: {State.TotalOutputTokens}{Environment.NewLine}" +
            $"- cache_read_input_tokens: {State.TotalCacheReadInputTokens}{Environment.NewLine}" +
            $"- cache_creation_input_tokens: {State.TotalCacheCreationInputTokens}{Environment.NewLine}" +
            $"- cost_claude_usd: {State.TotalCostClaudeUsd.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}{Environment.NewLine}" +
            $"- cost_codex_usd: {State.TotalCostCodexUsd.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}{Environment.NewLine}{Environment.NewLine}" +
            $"## Last handoff{Environment.NewLine}" +
            $"{handoffBlock}{Environment.NewLine}{Environment.NewLine}" +
            $"## Pending prompt at rotation boundary{Environment.NewLine}" +
            $"{pendingPrompt}{Environment.NewLine}";
    }

    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                    {
                        builder.Append("\\u").Append(((int)ch).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    private sealed record BudgetTrip(string Signal, string Reason);

    private string BuildOptionsSummary() =>
        $"Effective options: MaxCumulativeOutputTokens={_options.MaxCumulativeOutputTokens}; " +
        $"CacheReadRatioFloor={_options.CacheReadRatioFloor:F2}; " +
        $"ConsecutiveLowCacheTurnsThreshold={_options.ConsecutiveLowCacheTurnsThreshold}; " +
        $"MaxTurnsPerSession={_options.MaxTurnsPerSession}; " +
        $"MaxSessionDuration={_options.MaxSessionDuration:c}; " +
        $"MaxRepairAttempts={_options.MaxRepairAttempts}; " +
        $"FallbackClaudeBudgetUsd={_options.FallbackClaudeBudgetUsd:F2}; " +
        $"MaxClaudeCostUsd={(_options.MaxClaudeCostUsd.HasValue ? _options.MaxClaudeCostUsd.Value.ToString("F2") + " per-session-segment (api-key auth only)" : "disabled")}; " +
        $"PerTurnTimeout={_options.PerTurnTimeout:c}; " +
        $"JobObject.UserCpuTimePerJob={_options.JobObject.UserCpuTimePerJob:c}; " +
        $"JobObject.ActiveProcessLimit={_options.JobObject.ActiveProcessLimit}; " +
        $"JobObject.JobMemoryLimitBytes={_options.JobObject.JobMemoryLimitBytes}";

    private async Task<T> RunWithTurnTimeoutAsync<T>(
        RelaySide side,
        string phase,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (_options.PerTurnTimeout <= TimeSpan.Zero)
        {
            return await operation(cancellationToken);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(_options.PerTurnTimeout);

        try
        {
            return await operation(linkedCts.Token);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == linkedCts.Token && !cancellationToken.IsCancellationRequested)
        {
            var message = $"Per-turn timeout exceeded after {_options.PerTurnTimeout:mm\\:ss} during {phase} on {side}.";
            await PersistAndLogAsync(
                new RelayLogEvent(DateTimeOffset.Now, "turn.timeout", side, message),
                cancellationToken);
            throw new RelayTurnTimeoutException(message);
        }
    }

    private static string BuildRepairPrompt() =>
        """
        Output exactly one valid JSON handoff object.
        Do not add commentary.
        Do not restate analysis.
        Schema fields:
        type, version, source, target, session_id, turn, ready, prompt, summary, requires_human, reason, created_at.
        """;

    private sealed class RelayTurnTimeoutException : Exception
    {
        public RelayTurnTimeoutException(string message) : base(message)
        {
        }
    }
}
