namespace RelayApp.Core.Models;

public sealed record RelayObservedAction(
    string EventType,
    string Message,
    string? Payload = null,
    string? Category = null,
    string? Title = null);
