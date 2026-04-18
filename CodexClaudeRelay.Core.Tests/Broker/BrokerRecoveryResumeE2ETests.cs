using System.Text.Json;
using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Broker;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Persistence;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Broker;

[Collection("BrokerCwdMutating")]
public class BrokerRecoveryResumeE2ETests
{
    private sealed class CapturingAdapter : IRelayAdapter
    {
        public string Role { get; init; } = string.Empty;
        public string HandoffJson { get; set; } = string.Empty;
        public string? LastTurnPrompt { get; private set; }

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AdapterStatus(RelayHealthStatus.Healthy, true, "ok"));

        public Task<RelayAdapterResult> RunTurnAsync(RelayTurnContext context, CancellationToken cancellationToken)
        {
            LastTurnPrompt = context.Prompt;
            return Task.FromResult(new RelayAdapterResult(HandoffJson));
        }

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

    private static string BuildHandoffJson(string source, string target, string sessionId, int turn, string prompt, string closeoutKind)
    {
        var env = new HandoffEnvelope
        {
            Source = source,
            Target = target,
            SessionId = sessionId,
            Turn = turn,
            Ready = true,
            Prompt = prompt,
            Reason = $"{source} closed turn {turn} as {closeoutKind}",
            Summary = new[] { $"turn {turn} summary" },
            CloseoutKind = closeoutKind,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        return JsonSerializer.Serialize(env, HandoffJson.SerializerOptions);
    }

    [Fact]
    public async Task Recovery_resume_closeout_prepends_preamble_and_next_peer_continues()
    {
        const string sessionId = "sess-g5-resume";
        var tmpDir = Path.Combine(Path.GetTempPath(), "g5-resume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var originalCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = tmpDir;
        try
        {
            var codex = new CapturingAdapter { Role = AgentRole.Codex };
            var claude = new CapturingAdapter { Role = AgentRole.Claude };
            var store = new InMemorySessionStore();
            var log = new InMemoryEventLog();
            var broker = new RelayBroker(new IRelayAdapter[] { codex, claude }, store, log);

            await broker.StartSessionAsync(sessionId, AgentRole.Codex, "initial prompt", CancellationToken.None);

            codex.HandoffJson = BuildHandoffJson(
                AgentRole.Codex, AgentRole.Claude, sessionId, 1,
                "claude, 이어서 작업해주세요",
                CloseoutKind.RecoveryResume);

            await broker.AdvanceAsync(CancellationToken.None);

            Assert.Equal(AgentRole.Claude, broker.State.ActiveAgent);
            Assert.Equal(2, broker.State.CurrentTurn);
            Assert.True(broker.State.CarryForwardPending);
            Assert.Contains("[recovery_resume]", broker.State.PendingPrompt);
            Assert.Contains("continued_from_resume", broker.State.PendingPrompt);

            Assert.Contains(log.Events, e =>
                e.EventType == "session.recovery_resume" && e.Role == AgentRole.Codex);

            claude.HandoffJson = BuildHandoffJson(
                AgentRole.Claude, AgentRole.Codex, sessionId, 2,
                "codex, 검토 부탁",
                CloseoutKind.PeerHandoff);

            await broker.AdvanceAsync(CancellationToken.None);

            Assert.NotNull(claude.LastTurnPrompt);
            Assert.Contains("[recovery_resume]", claude.LastTurnPrompt!);
            Assert.Contains("continued_from_resume", claude.LastTurnPrompt!);
            Assert.Contains("claude, 이어서 작업해주세요", claude.LastTurnPrompt!);

            Assert.Equal(AgentRole.Codex, broker.State.ActiveAgent);
            Assert.Equal(3, broker.State.CurrentTurn);

            var sessionDir = Path.Combine(tmpDir, "Document", "dialogue", "sessions", sessionId);
            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-2.yaml")));
            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-2-handoff.md")));
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task Recovery_resume_is_peer_symmetric_when_claude_initiates()
    {
        const string sessionId = "sess-g5-resume-rev";
        var tmpDir = Path.Combine(Path.GetTempPath(), "g5-resume-rev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var originalCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = tmpDir;
        try
        {
            var codex = new CapturingAdapter { Role = AgentRole.Codex };
            var claude = new CapturingAdapter { Role = AgentRole.Claude };
            var store = new InMemorySessionStore();
            var log = new InMemoryEventLog();
            var broker = new RelayBroker(new IRelayAdapter[] { codex, claude }, store, log);

            await broker.StartSessionAsync(sessionId, AgentRole.Claude, "initial prompt", CancellationToken.None);

            claude.HandoffJson = BuildHandoffJson(
                AgentRole.Claude, AgentRole.Codex, sessionId, 1,
                "codex, 계속 진행",
                CloseoutKind.RecoveryResume);

            await broker.AdvanceAsync(CancellationToken.None);

            Assert.Equal(AgentRole.Codex, broker.State.ActiveAgent);
            Assert.True(broker.State.CarryForwardPending);
            Assert.Contains(log.Events, e =>
                e.EventType == "session.recovery_resume" && e.Role == AgentRole.Claude);

            codex.HandoffJson = BuildHandoffJson(
                AgentRole.Codex, AgentRole.Claude, sessionId, 2,
                "claude, 다음",
                CloseoutKind.PeerHandoff);

            await broker.AdvanceAsync(CancellationToken.None);

            Assert.NotNull(codex.LastTurnPrompt);
            Assert.Contains("[recovery_resume]", codex.LastTurnPrompt!);
            Assert.Equal(3, broker.State.CurrentTurn);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }
}
