using System.Text.Json;
using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Broker;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Persistence;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Broker;

[CollectionDefinition("BrokerCwdMutating", DisableParallelization = true)]
public class BrokerCwdMutatingCollection { }

[Collection("BrokerCwdMutating")]
public class BrokerRoutingRoundTripE2ETests
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
        public Task SaveAsync(RelaySessionState state, CancellationToken cancellationToken)
        {
            Last = state;
            return Task.CompletedTask;
        }
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

    private static string BuildHandoffJson(string source, string target, string sessionId, int turn, string prompt)
    {
        var env = new HandoffEnvelope
        {
            Source = source,
            Target = target,
            SessionId = sessionId,
            Turn = turn,
            Ready = true,
            Prompt = prompt,
            Reason = $"{source} finished turn {turn}",
            Summary = new[] { $"turn {turn} summary" },
            Completed = new[] { $"turn {turn} completed work" },
            CloseoutKind = CloseoutKind.PeerHandoff,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        return JsonSerializer.Serialize(env, HandoffJson.SerializerOptions);
    }

    private static async Task<(string tmpDir, string originalCwd, RelayBroker broker, CannedAdapter codex, CannedAdapter claude, InMemoryEventLog log)> SetupAsync(
        string sessionId, string firstRole)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "g4-routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var originalCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = tmpDir;

        var codex = new CannedAdapter { Role = AgentRole.Codex };
        var claude = new CannedAdapter { Role = AgentRole.Claude };
        var store = new InMemorySessionStore();
        var log = new InMemoryEventLog();
        var broker = new RelayBroker(new IRelayAdapter[] { codex, claude }, store, log);
        await broker.StartSessionAsync(sessionId, firstRole, "initial prompt", CancellationToken.None);
        return (tmpDir, originalCwd, broker, codex, claude, log);
    }

    [Fact]
    public async Task Codex_first_round_trip_routes_two_turns_and_lands_artifacts()
    {
        const string sessionId = "sess-g4-codex-first";
        var (tmpDir, originalCwd, broker, codex, claude, log) =
            await SetupAsync(sessionId, AgentRole.Codex);
        try
        {
            codex.HandoffJson = BuildHandoffJson(AgentRole.Codex, AgentRole.Claude, sessionId, 1, "claude, please continue");
            await broker.AdvanceAsync(CancellationToken.None);

            Assert.Equal(AgentRole.Claude, broker.State.ActiveAgent);
            Assert.Equal(2, broker.State.CurrentTurn);

            var sessionDir = Path.Combine(tmpDir, "Document", "dialogue", "sessions", sessionId);
            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-1.yaml")));
            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-1-handoff.md")));
            var stateJson = await File.ReadAllTextAsync(Path.Combine(sessionDir, "state.json"), CancellationToken.None);
            Assert.Contains("\"current_turn\": 2", stateJson);
            Assert.Contains("\"last_agent\": \"codex\"", stateJson);
            Assert.Contains("\"origin_backlog_id\": \"sess-g4-codex-first\"", stateJson);
            var rootStateJson = await File.ReadAllTextAsync(Path.Combine(tmpDir, "Document", "dialogue", "state.json"), CancellationToken.None);
            Assert.Contains("\"session_id\": \"sess-g4-codex-first\"", rootStateJson);

            claude.HandoffJson = BuildHandoffJson(AgentRole.Claude, AgentRole.Codex, sessionId, 2, "codex, next step");
            await broker.AdvanceAsync(CancellationToken.None);

            Assert.Equal(AgentRole.Codex, broker.State.ActiveAgent);
            Assert.Equal(3, broker.State.CurrentTurn);

            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-2.yaml")));
            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-2-handoff.md")));
            var stateJson2 = await File.ReadAllTextAsync(Path.Combine(sessionDir, "state.json"), CancellationToken.None);
            Assert.Contains("\"current_turn\": 3", stateJson2);
            Assert.Contains("\"last_agent\": \"claude-code\"", stateJson2);

            Assert.Contains(log.Events, e => e.EventType == "packet_written" && e.Message.Contains("turn-1.yaml"));
            Assert.Contains(log.Events, e => e.EventType == "packet_written" && e.Message.Contains("turn-2.yaml"));
            Assert.Contains(log.Events, e => e.EventType == "state_written");
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task Claude_first_round_trip_is_peer_symmetric()
    {
        const string sessionId = "sess-g4-claude-first";
        var (tmpDir, originalCwd, broker, codex, claude, log) =
            await SetupAsync(sessionId, AgentRole.Claude);
        try
        {
            claude.HandoffJson = BuildHandoffJson(AgentRole.Claude, AgentRole.Codex, sessionId, 1, "codex, please continue");
            await broker.AdvanceAsync(CancellationToken.None);

            Assert.Equal(AgentRole.Codex, broker.State.ActiveAgent);
            Assert.Equal(2, broker.State.CurrentTurn);

            codex.HandoffJson = BuildHandoffJson(AgentRole.Codex, AgentRole.Claude, sessionId, 2, "claude, next step");
            await broker.AdvanceAsync(CancellationToken.None);

            Assert.Equal(AgentRole.Claude, broker.State.ActiveAgent);
            Assert.Equal(3, broker.State.CurrentTurn);

            var sessionDir = Path.Combine(tmpDir, "Document", "dialogue", "sessions", sessionId);
            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-1.yaml")));
            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-2.yaml")));
            var stateJson = await File.ReadAllTextAsync(Path.Combine(sessionDir, "state.json"), CancellationToken.None);
            Assert.Contains("\"current_turn\": 3", stateJson);
            Assert.Contains("\"last_agent\": \"codex\"", stateJson);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }
}
