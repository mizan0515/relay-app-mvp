using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Broker;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Persistence;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Broker;

public class BrokerSessionBootstrapTests
{
    private sealed class NullAdapter : IRelayAdapter
    {
        public string Role { get; init; } = string.Empty;

        public Task<AdapterStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AdapterStatus(RelayHealthStatus.Healthy, true, "ok"));

        public Task<RelayAdapterResult> RunTurnAsync(RelayTurnContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RelayAdapterResult> RunRepairAsync(RelayRepairContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InMemorySessionStore : IRelaySessionStore
    {
        public RelaySessionState? Last { get; private set; }

        public Task SaveAsync(RelaySessionState state, CancellationToken cancellationToken)
        {
            Last = state;
            return Task.CompletedTask;
        }

        public Task<RelaySessionState?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Last);
    }

    private sealed class InMemoryEventLog : IEventLogWriter
    {
        public Task AppendAsync(string sessionId, RelayLogEvent logEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public string GetLogPath(string sessionId) => string.Empty;
    }

    [Fact]
    public async Task StartSessionAsync_applies_bootstrap_metadata_to_state()
    {
        var broker = new RelayBroker(
            new IRelayAdapter[]
            {
                new NullAdapter { Role = AgentRole.Codex },
                new NullAdapter { Role = AgentRole.Claude },
            },
            new InMemorySessionStore(),
            new InMemoryEventLog());

        var bootstrap = new RelaySessionBootstrap
        {
            Mode = "hybrid",
            Scope = "medium",
            OriginBacklogId = "companion-depth-first-slice",
            TaskBucket = "ui-runtime",
            TaskSummary = "Companion slice",
            ContractStatus = "accepted",
            Decisions = new List<string> { "post_mvp: companion-depth" },
            Constraints = new List<string> { "verification_expectation: narrow editmode" },
        };

        await broker.StartSessionAsync("sess-bootstrap", AgentRole.Codex, "initial prompt", bootstrap, CancellationToken.None);

        Assert.Equal("companion-depth-first-slice", broker.State.OriginBacklogId);
        Assert.Equal("ui-runtime", broker.State.TaskBucket);
        Assert.Equal("Companion slice", broker.State.TaskSummary);
        Assert.Equal("medium", broker.State.Scope);
        Assert.Contains("post_mvp: companion-depth", broker.State.Decisions);
        Assert.Contains("verification_expectation: narrow editmode", broker.State.Constraints);
    }
}
