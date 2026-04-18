namespace CodexClaudeRelay.Core.Models;

public sealed record RelayLogEvent(
    DateTimeOffset Timestamp,
    string EventType,
    string? Role,
    string Message,
    string? Payload = null);
