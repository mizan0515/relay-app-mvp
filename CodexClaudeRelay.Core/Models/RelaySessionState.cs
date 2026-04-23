namespace CodexClaudeRelay.Core.Models;

public sealed class RelaySessionState
{
    public string SessionId { get; set; } = string.Empty;

    public RelaySessionStatus Status { get; set; } = RelaySessionStatus.Idle;

    public string Mode { get; set; } = "hybrid";

    public string Scope { get; set; } = "medium";

    public string OriginBacklogId { get; set; } = string.Empty;

    public string TaskBucket { get; set; } = string.Empty;

    public string TaskSummary { get; set; } = string.Empty;

    public string ContractStatus { get; set; } = "accepted";

    public string ActiveAgent { get; set; } = AgentRole.Codex;

    public int CurrentTurn { get; set; } = 1;

    public string PendingPrompt { get; set; } = string.Empty;

    public int RepairAttempts { get; set; }

    public string? LastError { get; set; }

    public RelayPendingApproval? PendingApproval { get; set; }

    public List<RelayApprovalQueueItem> ApprovalQueue { get; set; } = [];

    public List<RelaySessionApprovalRule> SessionApprovalRules { get; set; } = [];

    public HandoffEnvelope? LastHandoff { get; set; }

    public string? LastHandoffHash { get; set; }

    public string? Goal { get; set; }

    public List<string> Completed { get; set; } = [];

    public List<string> Pending { get; set; } = [];

    public List<string> Constraints { get; set; } = [];

    public bool CarryForwardPending { get; set; }

    public List<string> AcceptedRelayKeys { get; set; } = [];

    public Dictionary<string, string> NativeSessionHandles { get; set; } = [];

    public Dictionary<string, RelayUsageMetrics> LastUsageBySide { get; set; } = [];

    public Dictionary<string, RelayUsageMetrics> LastCumulativeByHandle { get; set; } = [];

    public long TotalInputTokens { get; set; }

    public long TotalOutputTokens { get; set; }

    public long TotalCacheCreationInputTokens { get; set; }

    public long TotalCacheReadInputTokens { get; set; }

    public double TotalCostClaudeUsd { get; set; }

    public double TotalCostCodexUsd { get; set; }

    public double TotalCostUsd => TotalCostClaudeUsd + TotalCostCodexUsd;

    public double ClaudeCostAtLastRotationUsd { get; set; }

    public List<double> CacheReadRatioByTurn { get; set; } = [];

    public Dictionary<string, int> ConsecutiveLowCacheTurnsBySide { get; set; } = [];

    public List<string> PolicyGapAdvisoriesFired { get; set; } = [];

    public List<string> Decisions { get; set; } = [];

    public List<string> MetaImprovements { get; set; } = [];

    public bool TerminalLearningRecordWritten { get; set; }

    public DateTimeOffset SessionStartedAt { get; set; } = DateTimeOffset.Now;

    public int RotationCount { get; set; }

    public int TurnsSinceLastRotation { get; set; }

    public string? LastBudgetSignal { get; set; }

    public bool CacheInflationAdvisoryFired { get; set; }

    public bool ClaudeCostAbsentAdvisoryFired { get; set; }

    public bool ClaudeCostCeilingDisabledAdvisoryFired { get; set; }

    public bool CodexPricingFallbackAdvisoryFired { get; set; }

    public bool CodexRateCardStaleAdvisoryFired { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
