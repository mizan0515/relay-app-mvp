using RelayApp.Core.Models;

namespace RelayApp.Core.Persistence;

public interface IEventLogWriter
{
    Task AppendAsync(string sessionId, RelayLogEvent logEvent, CancellationToken cancellationToken);

    string GetLogPath(string sessionId);
}
