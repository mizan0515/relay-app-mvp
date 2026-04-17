using RelayApp.Core.Models;

namespace RelayApp.Core.Persistence;

public interface IRelaySessionStore
{
    Task SaveAsync(RelaySessionState state, CancellationToken cancellationToken);

    Task<RelaySessionState?> LoadAsync(CancellationToken cancellationToken);
}
