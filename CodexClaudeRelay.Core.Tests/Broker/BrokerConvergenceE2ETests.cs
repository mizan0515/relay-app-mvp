using System.Reflection;
using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Broker;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Persistence;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Broker;

public class BrokerConvergenceE2ETests
{
    private sealed class NoopAdapter : IRelayAdapter
    {
        public string Role { get; init; } = string.Empty;

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AdapterStatus(RelayHealthStatus.Healthy, true, "ok"));

        public Task<RelayAdapterResult> RunTurnAsync(RelayTurnContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new RelayAdapterResult(string.Empty));

        public Task<RelayAdapterResult> RunRepairAsync(RelayRepairContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new RelayAdapterResult(string.Empty));
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

    private static async Task InvokeCompleteHandoffAsync(RelayBroker broker, HandoffEnvelope handoff)
    {
        var method = typeof(RelayBroker).GetMethod(
            "CompleteHandoffAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CompleteHandoffAsync not found via reflection");
        var task = (Task)method.Invoke(broker, new object?[] { handoff, false, CancellationToken.None })!;
        await task.ConfigureAwait(false);
    }

    [Fact]
    public async Task Convergent_handoff_pair_seals_session_and_writes_backlog_and_events()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "g7-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var originalCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = tmpDir;
        try
        {
            var adapters = new IRelayAdapter[]
            {
                new NoopAdapter { Role = AgentRole.Codex },
                new NoopAdapter { Role = AgentRole.Claude },
            };
            var store = new InMemorySessionStore();
            var log = new InMemoryEventLog();
            var broker = new RelayBroker(adapters, store, log);

            await broker.StartSessionAsync("sess-g7-e2e", AgentRole.Codex, "initial prompt", CancellationToken.None);

            var checkpoints = new[]
            {
                new CheckpointResult { CheckpointId = "cp-1", Status = CheckpointStatus.Pass, EvidenceRef = "log:a" },
            };

            var codexHandoff = new HandoffEnvelope
            {
                Source = AgentRole.Codex,
                Target = AgentRole.Claude,
                SessionId = "sess-g7-e2e",
                Turn = 1,
                Ready = true,
                Prompt = "please verify my work",
                Reason = "codex finished the task",
                Summary = new[] { "step 1 done" },
                Completed = new[] { "implemented feature" },
                CheckpointResults = checkpoints,
                SuggestDone = true,
                DoneReason = "all checks pass",
                CloseoutKind = CloseoutKind.PeerHandoff,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await InvokeCompleteHandoffAsync(broker, codexHandoff);

            Assert.Equal(RelaySessionStatus.Active, broker.State.Status);
            Assert.DoesNotContain(log.Events, e => e.EventType == "session.converged");

            var claudeHandoff = new HandoffEnvelope
            {
                Source = AgentRole.Claude,
                Target = AgentRole.Codex,
                SessionId = "sess-g7-e2e",
                Turn = 2,
                Ready = true,
                Prompt = "agreed, closing",
                Reason = "claude confirms the work",
                Summary = new[] { "reviewed, agree" },
                Completed = new[] { "verified feature" },
                CheckpointResults = checkpoints,
                SuggestDone = true,
                DoneReason = "peer verification complete",
                CloseoutKind = CloseoutKind.PeerHandoff,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await InvokeCompleteHandoffAsync(broker, claudeHandoff);

            Assert.Equal(RelaySessionStatus.Converged, broker.State.Status);
            Assert.Contains(log.Events, e => e.EventType == "session.converged");
            Assert.Contains(log.Events, e => e.EventType == "backlog.closure_written");

            var backlogPath = Path.Combine(tmpDir, "Document", "dialogue", "backlog.json");
            Assert.True(File.Exists(backlogPath), $"backlog.json should exist at {backlogPath}");
            var backlogJson = await File.ReadAllTextAsync(backlogPath, CancellationToken.None);
            Assert.Contains("\"session_id\": \"sess-g7-e2e\"", backlogJson);
            Assert.Contains("\"session_status\": \"converged\"", backlogJson);
            Assert.Contains("\"closed_by_session_id\": \"sess-g7-e2e\"", backlogJson);
            Assert.Contains("\"converged_turn\": 2", backlogJson);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task Peer_reversed_ordering_also_converges_symmetrically()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "g7-e2e-rev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var originalCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = tmpDir;
        try
        {
            var adapters = new IRelayAdapter[]
            {
                new NoopAdapter { Role = AgentRole.Codex },
                new NoopAdapter { Role = AgentRole.Claude },
            };
            var store = new InMemorySessionStore();
            var log = new InMemoryEventLog();
            var broker = new RelayBroker(adapters, store, log);

            await broker.StartSessionAsync("sess-g7-rev", AgentRole.Claude, "initial prompt", CancellationToken.None);

            var checkpoints = new[]
            {
                new CheckpointResult { CheckpointId = "cp-X", Status = CheckpointStatus.Pass, EvidenceRef = "log:x" },
            };

            var claudeHandoff = new HandoffEnvelope
            {
                Source = AgentRole.Claude, Target = AgentRole.Codex,
                SessionId = "sess-g7-rev", Turn = 1, Ready = true,
                Prompt = "please verify", Reason = "claude finished",
                Summary = new[] { "work done" },
                CheckpointResults = checkpoints,
                SuggestDone = true, DoneReason = "ready",
                CloseoutKind = CloseoutKind.PeerHandoff,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await InvokeCompleteHandoffAsync(broker, claudeHandoff);
            Assert.Equal(RelaySessionStatus.Active, broker.State.Status);

            var codexHandoff = new HandoffEnvelope
            {
                Source = AgentRole.Codex, Target = AgentRole.Claude,
                SessionId = "sess-g7-rev", Turn = 2, Ready = true,
                Prompt = "agreed", Reason = "codex confirms",
                Summary = new[] { "verified" },
                CheckpointResults = checkpoints,
                SuggestDone = true, DoneReason = "agree",
                CloseoutKind = CloseoutKind.PeerHandoff,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await InvokeCompleteHandoffAsync(broker, codexHandoff);

            Assert.Equal(RelaySessionStatus.Converged, broker.State.Status);
            Assert.Contains(log.Events, e => e.EventType == "session.converged");
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }
}
