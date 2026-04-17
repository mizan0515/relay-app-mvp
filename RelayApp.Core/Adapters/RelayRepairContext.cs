using RelayApp.Core.Models;

namespace RelayApp.Core.Adapters;

public sealed record RelayRepairContext(
    string SessionId,
    int TurnNumber,
    RelaySide SourceSide,
    string OriginalPrompt,
    string OriginalOutput,
    string RepairPrompt,
    string? ExistingSessionHandle = null);
