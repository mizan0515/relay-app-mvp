namespace CodexClaudeRelay.Core.Models;

public sealed record RelayPendingApproval(
    string Id,
    string Role,
    string Category,
    string Title,
    string Message,
    string? Payload,
    string? PolicyKey,
    DateTimeOffset CreatedAt,
    string RiskLevel = "medium");
