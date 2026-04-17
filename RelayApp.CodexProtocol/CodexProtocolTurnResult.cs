using System.Text.Json;

namespace RelayApp.CodexProtocol;

public sealed class CodexProtocolTurnResult
{
    public required string WorkingDirectory { get; init; }

    public required string Prompt { get; init; }

    public required string ThreadId { get; init; }

    public required string TurnId { get; init; }

    public required JsonElement InitializeResult { get; init; }

    public required JsonElement AuthStatus { get; init; }

    public required JsonElement ThreadStartResult { get; init; }

    public required JsonElement TurnStartResult { get; init; }

    public required JsonElement TurnCompletedNotification { get; init; }

    public string? FinalAgentMessageText { get; init; }

    public string? LastAgentMessageDelta { get; init; }

    public JsonElement LastTokenUsageNotification { get; init; }

    public required IReadOnlyList<CodexProtocolMessage> Messages { get; init; }
}
