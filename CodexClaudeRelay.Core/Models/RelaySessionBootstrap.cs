namespace CodexClaudeRelay.Core.Models;

public sealed class RelaySessionBootstrap
{
    public string Mode { get; init; } = "hybrid";

    public string Scope { get; init; } = "medium";

    public string OriginBacklogId { get; init; } = string.Empty;

    public string TaskBucket { get; init; } = string.Empty;

    public string TaskSummary { get; init; } = string.Empty;

    public string ContractStatus { get; init; } = "accepted";

    public List<string> Decisions { get; init; } = [];

    public List<string> Constraints { get; init; } = [];
}
