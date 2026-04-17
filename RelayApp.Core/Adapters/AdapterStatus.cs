using RelayApp.Core.Models;

namespace RelayApp.Core.Adapters;

public sealed record AdapterStatus(
    RelayHealthStatus Health,
    bool IsAuthenticated,
    string Message);
