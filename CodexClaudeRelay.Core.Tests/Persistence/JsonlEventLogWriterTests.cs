using System.Text.Json;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Persistence;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Persistence;

public class JsonlEventLogWriterTests : IDisposable
{
    private readonly string _logDir;

    public JsonlEventLogWriterTests()
    {
        _logDir = Path.Combine(Path.GetTempPath(), "jsonl-writer-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_logDir)) Directory.Delete(_logDir, recursive: true);
    }

    [Fact]
    public async Task Appended_event_carries_event_hash_in_jsonl_output()
    {
        var writer = new JsonlEventLogWriter(_logDir);
        var ev = new RelayLogEvent(
            new DateTimeOffset(2026, 4, 19, 0, 0, 0, TimeSpan.Zero),
            "turn.started", "codex", "turn 1 started", "{\"turn\":1}");

        await writer.AppendAsync("sess-1", ev, CancellationToken.None);

        var line = (await File.ReadAllLinesAsync(writer.GetLogPath("sess-1"))).Single();
        using var doc = JsonDocument.Parse(line);
        var hash = doc.RootElement.GetProperty("event_hash").GetString();
        Assert.NotNull(hash);
        Assert.Matches("^[0-9a-f]{64}$", hash!);
    }

    [Fact]
    public async Task Identical_events_in_same_session_produce_identical_hashes()
    {
        var writer = new JsonlEventLogWriter(_logDir);
        var ts = new DateTimeOffset(2026, 4, 19, 0, 5, 0, TimeSpan.Zero);
        var ev = new RelayLogEvent(ts, "packet_written", "claude-code", "turn-1.yaml landed", null);

        await writer.AppendAsync("sess-a", ev, CancellationToken.None);
        await writer.AppendAsync("sess-a", ev, CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(writer.GetLogPath("sess-a"));
        Assert.Equal(2, lines.Length);
        var h1 = JsonDocument.Parse(lines[0]).RootElement.GetProperty("event_hash").GetString();
        var h2 = JsonDocument.Parse(lines[1]).RootElement.GetProperty("event_hash").GetString();
        Assert.Equal(h1, h2);
    }

    [Fact]
    public async Task Different_sessions_produce_different_hashes_for_same_event()
    {
        var writer = new JsonlEventLogWriter(_logDir);
        var ts = new DateTimeOffset(2026, 4, 19, 0, 10, 0, TimeSpan.Zero);
        var ev = new RelayLogEvent(ts, "turn.started", "codex", "begin", null);

        await writer.AppendAsync("sess-x", ev, CancellationToken.None);
        await writer.AppendAsync("sess-y", ev, CancellationToken.None);

        var hx = JsonDocument.Parse(File.ReadAllText(writer.GetLogPath("sess-x")).Trim())
            .RootElement.GetProperty("event_hash").GetString();
        var hy = JsonDocument.Parse(File.ReadAllText(writer.GetLogPath("sess-y")).Trim())
            .RootElement.GetProperty("event_hash").GetString();
        Assert.NotEqual(hx, hy);
    }

    [Fact]
    public async Task Different_payload_or_type_produces_different_hash()
    {
        var writer = new JsonlEventLogWriter(_logDir);
        var ts = new DateTimeOffset(2026, 4, 19, 0, 15, 0, TimeSpan.Zero);
        var a = new RelayLogEvent(ts, "turn.started", "codex", "m", "p1");
        var b = new RelayLogEvent(ts, "turn.started", "codex", "m", "p2");
        var c = new RelayLogEvent(ts, "turn.completed", "codex", "m", "p1");

        await writer.AppendAsync("sess", a, CancellationToken.None);
        await writer.AppendAsync("sess", b, CancellationToken.None);
        await writer.AppendAsync("sess", c, CancellationToken.None);

        var hashes = (await File.ReadAllLinesAsync(writer.GetLogPath("sess")))
            .Select(l => JsonDocument.Parse(l).RootElement.GetProperty("event_hash").GetString())
            .ToArray();
        Assert.Equal(3, hashes.Distinct().Count());
    }

    [Fact]
    public async Task Preexisting_event_hash_is_preserved_not_recomputed()
    {
        var writer = new JsonlEventLogWriter(_logDir);
        var ev = new RelayLogEvent(
            DateTimeOffset.UtcNow, "test", "codex", "m", null,
            EventHash: "deadbeef".PadRight(64, '0'));

        await writer.AppendAsync("sess-pre", ev, CancellationToken.None);

        var line = (await File.ReadAllLinesAsync(writer.GetLogPath("sess-pre"))).Single();
        var hash = JsonDocument.Parse(line).RootElement.GetProperty("event_hash").GetString();
        Assert.Equal("deadbeef".PadRight(64, '0'), hash);
    }

    [Fact]
    public void ComputeEventHash_is_deterministic_and_differs_by_timestamp()
    {
        var ts1 = new DateTimeOffset(2026, 4, 19, 0, 0, 0, TimeSpan.Zero);
        var ts2 = new DateTimeOffset(2026, 4, 19, 0, 0, 1, TimeSpan.Zero);
        var e1 = new RelayLogEvent(ts1, "t", "codex", "m", null);
        var e2 = new RelayLogEvent(ts2, "t", "codex", "m", null);

        var h1 = JsonlEventLogWriter.ComputeEventHash("s", e1);
        var h1b = JsonlEventLogWriter.ComputeEventHash("s", e1);
        var h2 = JsonlEventLogWriter.ComputeEventHash("s", e2);

        Assert.Equal(h1, h1b);
        Assert.NotEqual(h1, h2);
    }
}
