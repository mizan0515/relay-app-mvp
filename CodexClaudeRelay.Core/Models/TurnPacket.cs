namespace CodexClaudeRelay.Core.Models;

/// <summary>
/// Minimal DAD-v2 Turn Packet POCO covering the fields the broker and
/// handoff-artifact writer need today. Mirrors the canonical YAML schema in
/// <c>Document/DAD/PACKET-SCHEMA.md</c> — additional fields land as gates
/// progress (G3 checkpoint_results, G5 recovery_resume, etc.).
///
/// Serialization is deferred: gate G1 (PacketIO) introduces YamlDotNet and
/// round-trip tests. This file is data-only so G2 (handoff artifact) can
/// land independently.
/// </summary>
public sealed record TurnPacket
{
    public string Type { get; init; } = "turn";
    public string From { get; init; } = AgentRole.Codex;
    public int Turn { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public TurnContract Contract { get; init; } = new();
    public TurnHandoff Handoff { get; init; } = new();
    public PeerReview PeerReview { get; init; } = new();
    public MyWork MyWork { get; init; } = new();
}

public sealed record TurnContract
{
    public string Status { get; init; } = "accepted";
    public IReadOnlyList<string> Checkpoints { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Amendments { get; init; } = Array.Empty<string>();
}

public sealed record PeerReview
{
    public string ProjectAnalysis { get; init; } = string.Empty;
    public TaskModelReview TaskModelReview { get; init; } = new();
    public IReadOnlyList<CheckpointResult> CheckpointResults { get; init; } = Array.Empty<CheckpointResult>();
    public IReadOnlyList<string> IssuesFound { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FixesApplied { get; init; } = Array.Empty<string>();
}

public sealed record TaskModelReview
{
    public string Status { get; init; } = "aligned";
    public IReadOnlyList<string> CoverageGaps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ScopeCreep { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RiskFollowups { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Amendments { get; init; } = Array.Empty<string>();
}

public sealed record CheckpointResult
{
    public string CheckpointId { get; init; } = string.Empty;
    /// <summary>PASS | FAIL | BLOCKED | SKIPPED</summary>
    public string Status { get; init; } = string.Empty;
    public string EvidenceRef { get; init; } = string.Empty;
}

public static class CheckpointStatus
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string Blocked = "BLOCKED";
    public const string Skipped = "SKIPPED";
}

public sealed record TurnHandoff
{
    /// <summary>peer_handoff | final_no_handoff | recovery_resume</summary>
    public string CloseoutKind { get; init; } = string.Empty;
    public string NextTask { get; init; } = string.Empty;
    public string Context { get; init; } = string.Empty;
    public IReadOnlyList<string> Questions { get; init; } = Array.Empty<string>();
    public string PromptArtifact { get; init; } = string.Empty;
    public bool ReadyForPeerVerification { get; init; }
    public bool SuggestDone { get; init; }
    public string DoneReason { get; init; } = string.Empty;
}

public sealed record MyWork
{
    public string Plan { get; init; } = string.Empty;
    public WorkChanges Changes { get; init; } = new();
    public int SelfIterations { get; init; }
    public WorkEvidence Evidence { get; init; } = new();
    public string Verification { get; init; } = string.Empty;
    public IReadOnlyList<string> OpenRisks { get; init; } = Array.Empty<string>();
    public string Confidence { get; init; } = "medium";
}

public sealed record WorkChanges
{
    public IReadOnlyList<string> FilesModified { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FilesCreated { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;
}

public sealed record WorkEvidence
{
    public IReadOnlyList<string> Commands { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Artifacts { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Allowed values for <see cref="TurnHandoff.CloseoutKind"/>.
/// </summary>
public static class CloseoutKind
{
    public const string PeerHandoff = "peer_handoff";
    public const string FinalNoHandoff = "final_no_handoff";
    public const string RecoveryResume = "recovery_resume";
}
