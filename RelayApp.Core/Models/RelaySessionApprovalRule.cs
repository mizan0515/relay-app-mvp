namespace RelayApp.Core.Models;

public sealed record RelaySessionApprovalRule(
    string PolicyKey,
    RelayApprovalDecision Decision,
    string Category,
    string Title,
    string Summary,
    DateTimeOffset CreatedAt,
    string RiskLevel = "medium");
