namespace CodexClaudeRelay.Core.Models;

public sealed record RelayApprovalQueueItem(
    string Id,
    string SessionId,
    int Turn,
    string Role,
    string Category,
    string Title,
    string Message,
    string? Payload,
    string? PolicyKey,
    string RiskLevel,
    DateTimeOffset CreatedAt,
    string Status = "pending",
    DateTimeOffset? ResolvedAt = null);
