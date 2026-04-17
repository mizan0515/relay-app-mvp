namespace RelayApp.Core.Models;

public sealed record RelayPendingApproval(
    string Id,
    RelaySide Side,
    string Category,
    string Title,
    string Message,
    string? Payload,
    string? PolicyKey,
    DateTimeOffset CreatedAt,
    string RiskLevel = "medium");
