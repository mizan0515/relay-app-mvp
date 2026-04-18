using System.Reflection;
using System.Text.Json;
using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Broker;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Persistence;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Broker;

[Collection("BrokerCwdMutating")]
public class BrokerReplayDedupE2ETests
{
    private sealed class CannedAdapter : IRelayAdapter
    {
        public string Role { get; init; } = string.Empty;
        public string HandoffJson { get; set; } = string.Empty;

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AdapterStatus(RelayHealthStatus.Healthy, true, "ok"));
        public Task<RelayAdapterResult> RunTurnAsync(RelayTurnContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new RelayAdapterResult(HandoffJson));
        public Task<RelayAdapterResult> RunRepairAsync(RelayRepairContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new RelayAdapterResult(HandoffJson));
    }

    private sealed class InMemorySessionStore : IRelaySessionStore
    {
        public RelaySessionState? Last { get; set; }
        public Task SaveAsync(RelaySessionState state, CancellationToken cancellationToken) { Last = state; return Task.CompletedTask; }
        public Task<RelaySessionState?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Last);
    }

    private sealed class InMemoryEventLog : IEventLogWriter
    {
        public List<RelayLogEvent> Events { get; } = new();
        public Task AppendAsync(string sessionId, RelayLogEvent logEvent, CancellationToken cancellationToken)
        {
            Events.Add(logEvent);
            return Task.CompletedTask;
        }
        public string GetLogPath(string sessionId) => string.Empty;
    }

    private static HandoffEnvelope BuildEnvelope(string sessionId, int turn) => new()
    {
        Source = AgentRole.Codex,
        Target = AgentRole.Claude,
        SessionId = sessionId,
        Turn = turn,
        Ready = true,
        Prompt = "claude, please continue",
        Reason = "replay-dedup smoke",
        Summary = new[] { "pending A" },
        Completed = new[] { "done 1" },
        CloseoutKind = CloseoutKind.PeerHandoff,
        CreatedAt = new DateTimeOffset(2026, 4, 19, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task Identical_handoff_submitted_twice_commits_only_once()
    {
        const string sessionId = "sess-g8-dedup";
        var tmpDir = Path.Combine(Path.GetTempPath(), "g8-dedup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var originalCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = tmpDir;
        try
        {
            var codex = new CannedAdapter { Role = AgentRole.Codex };
            var claude = new CannedAdapter { Role = AgentRole.Claude };
            var store = new InMemorySessionStore();
            var log = new InMemoryEventLog();
            var broker = new RelayBroker(new IRelayAdapter[] { codex, claude }, store, log);

            await broker.StartSessionAsync(sessionId, AgentRole.Codex, "initial", CancellationToken.None);

            var envelope = BuildEnvelope(sessionId, 1);
            codex.HandoffJson = JsonSerializer.Serialize(envelope, HandoffJson.SerializerOptions);

            var first = await broker.AdvanceAsync(CancellationToken.None);
            Assert.True(first.Succeeded);
            Assert.Equal(AgentRole.Claude, broker.State.ActiveAgent);
            Assert.Equal(2, broker.State.CurrentTurn);
            Assert.Single(broker.State.AcceptedRelayKeys);
            var priorKey = broker.State.AcceptedRelayKeys[0];

            // Simulate an at-most-once replay — roll turn + active agent back as if
            // the same envelope were re-submitted to the broker. AcceptedRelayKeys
            // persists deliberately; that is the dedup contract.
            broker.State.CurrentTurn = 1;
            broker.State.ActiveAgent = AgentRole.Codex;

            var complete = typeof(RelayBroker).GetMethod(
                "CompleteHandoffAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(complete);
            var task = (Task<BrokerAdvanceResult>)complete!.Invoke(
                broker, new object[] { envelope, /*repaired*/ false, CancellationToken.None })!;
            var second = await task;

            Assert.False(second.Succeeded);
            Assert.Contains("Duplicate handoff detected", second.Message);
            Assert.Single(broker.State.AcceptedRelayKeys);
            Assert.Equal(priorKey, broker.State.AcceptedRelayKeys[0]);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task Duplicate_submission_hashes_match_and_survive_writer_disposal()
    {
        var logDir = Path.Combine(Path.GetTempPath(), "g8-crash-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ts = new DateTimeOffset(2026, 4, 19, 1, 0, 0, TimeSpan.Zero);
            var ev = new RelayLogEvent(ts, "session.replay_attempt", AgentRole.Codex, "duplicate handoff sighted", "{\"turn\":1}");

            string path;
            {
                var writer = new JsonlEventLogWriter(logDir);
                path = writer.GetLogPath("sess-crash");
                await writer.AppendAsync("sess-crash", ev, CancellationToken.None);
                await writer.AppendAsync("sess-crash", ev, CancellationToken.None);
                // writer goes out of scope — simulates process termination after log flush
            }

            Assert.True(File.Exists(path), "event log must survive writer disposal (process crash proxy)");
            var lines = await File.ReadAllLinesAsync(path);
            Assert.Equal(2, lines.Length);
            var h1 = JsonDocument.Parse(lines[0]).RootElement.GetProperty("event_hash").GetString();
            var h2 = JsonDocument.Parse(lines[1]).RootElement.GetProperty("event_hash").GetString();
            Assert.Equal(h1, h2);
            Assert.Matches("^[0-9a-f]{64}$", h1!);
        }
        finally
        {
            if (Directory.Exists(logDir)) Directory.Delete(logDir, recursive: true);
        }
    }
}
