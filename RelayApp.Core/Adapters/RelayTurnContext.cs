using RelayApp.Core.Models;

namespace RelayApp.Core.Adapters;

public sealed record RelayTurnContext(
    string SessionId,
    int TurnNumber,
    RelaySide SourceSide,
    string Prompt,
    string? ExistingSessionHandle = null);
