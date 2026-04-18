using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Adapters;

public sealed record RelayTurnContext(
    string SessionId,
    int TurnNumber,
    string SourceRole,
    string Prompt,
    string? ExistingSessionHandle = null,
    string? CarryForward = null);
