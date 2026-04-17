using System.Text.Json;

namespace RelayApp.CodexProtocol;

public sealed class CodexProtocolSpikeRunResult
{
    public required string WorkingDirectory { get; init; }

    public required JsonElement InitializeResult { get; init; }

    public required JsonElement AuthStatus { get; init; }

    public required JsonElement ThreadStartResult { get; init; }

    public required JsonElement TurnStartResult { get; init; }

    public required JsonElement TurnCompletedNotification { get; init; }

    public required string ThreadId { get; init; }

    public required IReadOnlyList<CodexProtocolMessage> Messages { get; init; }
}
