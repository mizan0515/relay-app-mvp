namespace RelayApp.Core.Models;

public sealed record RelayApprovalQueueItem(
    string Id,
    string SessionId,
    int Turn,
    RelaySide Side,
    string Category,
    string Title,
    string Message,
    string? Payload,
    string? PolicyKey,
    string RiskLevel,
    DateTimeOffset CreatedAt,
    string Status = "pending",
    DateTimeOffset? ResolvedAt = null);
