using System.Text.Json;
using System.Text.Json.Serialization;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;

namespace CodexClaudeRelay.Core.Persistence;

public sealed class JsonlEventLogWriter : IEventLogWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _logDirectory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonlEventLogWriter(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    public async Task AppendAsync(string sessionId, RelayLogEvent logEvent, CancellationToken cancellationToken)
    {
        var path = GetLogPath(sessionId);
        var stamped = logEvent.EventHash is null
            ? logEvent with { EventHash = ComputeEventHash(sessionId, logEvent) }
            : logEvent;
        var line = JsonSerializer.Serialize(stamped, SerializerOptions);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public string GetLogPath(string sessionId) => Path.Combine(_logDirectory, $"{sessionId}.jsonl");

    public static string ComputeEventHash(string sessionId, RelayLogEvent e)
    {
        var canonical =
            $"session={sessionId}\n" +
            $"ts={e.Timestamp.ToUniversalTime():O}\n" +
            $"type={e.EventType}\n" +
            $"role={e.Role ?? string.Empty}\n" +
            $"msg={e.Message}\n" +
            $"payload={e.Payload ?? string.Empty}";
        return CanonicalHash.OfString(canonical);
    }
}
