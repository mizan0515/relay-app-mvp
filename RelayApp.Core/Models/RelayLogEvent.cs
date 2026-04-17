namespace RelayApp.Core.Models;

public sealed record RelayLogEvent(
    DateTimeOffset Timestamp,
    string EventType,
    RelaySide? Side,
    string Message,
    string? Payload = null);
