using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Adapters;

public sealed record RelayRepairContext(
    string SessionId,
    int TurnNumber,
    string SourceRole,
    string OriginalPrompt,
    string OriginalOutput,
    string RepairPrompt,
    string? ExistingSessionHandle = null);
