namespace CodexClaudeRelay.Core.Models;

public enum RelaySessionStatus
{
    Idle,
    Active,
    AwaitingApproval,
    Paused,
    Stopped,
    Failed,
    Converged,
}
