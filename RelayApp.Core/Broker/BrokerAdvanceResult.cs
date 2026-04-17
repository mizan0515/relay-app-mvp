using RelayApp.Core.Models;

namespace RelayApp.Core.Broker;

public sealed record BrokerAdvanceResult(
    bool Succeeded,
    bool AwaitingHuman,
    bool Repaired,
    string Message,
    RelaySessionState State);
