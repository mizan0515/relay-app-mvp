using RelayApp.Core.Models;

namespace RelayApp.Core.Adapters;

public interface IRelayAdapter
{
    RelaySide Side { get; }

    Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<RelayAdapterResult> RunTurnAsync(RelayTurnContext context, CancellationToken cancellationToken);

    Task<RelayAdapterResult> RunRepairAsync(RelayRepairContext context, CancellationToken cancellationToken);
}
