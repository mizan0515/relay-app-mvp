using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Protocol;

public class SessionLearningRecordWriterTests
{
    [Fact]
    public void Build_captures_session_and_checkpoint_summary()
    {
        var state = new RelaySessionState
        {
            SessionId = "sess-1",
            Status = RelaySessionStatus.Converged,
            Mode = "hybrid",
            Scope = "medium",
            OriginBacklogId = "companion-depth-first-slice",
            TaskBucket = "ui-runtime",
            TaskSummary = "Companion depth slice",
            ContractStatus = "accepted",
            CurrentTurn = 3,
            ActiveAgent = AgentRole.Claude,
            TotalInputTokens = 1200,
            TotalOutputTokens = 450,
            Decisions = new List<string> { "focus: none" },
            Constraints = new List<string> { "verification_expectation: narrow editmode" },
        };
        var handoff = new HandoffEnvelope
        {
            CloseoutKind = CloseoutKind.PeerHandoff,
            SuggestDone = true,
            DoneReason = "peer verification complete",
            CheckpointResults = new[]
            {
                new CheckpointResult { CheckpointId = "C1", Status = CheckpointStatus.Pass, EvidenceRef = "Tests.log" }
            },
        };

        var record = SessionLearningRecordWriter.Build(state, handoff, new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("sess-1", record.SessionId);
        Assert.Equal("converged", record.SessionStatus);
        Assert.Equal("companion-depth-first-slice", record.OriginBacklogId);
        Assert.Equal("ui-runtime", record.TaskBucket);
        Assert.Single(record.CheckpointSummary);
        Assert.Equal("C1:PASS@Tests.log", record.CheckpointSummary[0]);
    }

    [Fact]
    public async Task AppendAsync_writes_jsonl_line()
    {
        var dir = Path.Combine(Path.GetTempPath(), "learning-record-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "session-outcomes.jsonl");
        try
        {
            var record = new SessionLearningRecord
            {
                RecordedAt = DateTimeOffset.UtcNow,
                SessionId = "sess-2",
                SessionStatus = "stopped",
            };

            await SessionLearningRecordWriter.AppendAsync(record, path);

            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("\"session_id\":\"sess-2\"", content);
            Assert.EndsWith(Environment.NewLine, content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
