namespace RelayApp.Core.Models;

public enum RelaySessionStatus
{
    Idle,
    Active,
    AwaitingApproval,
    Paused,
    Stopped,
    Failed,
}
