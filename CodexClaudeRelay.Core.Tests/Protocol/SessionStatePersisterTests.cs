using System.Text.Json;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Protocol;

public class SessionStatePersisterTests
{
    [Fact]
    public void Render_emits_core_fields()
    {
        var snap = new SessionStateSnapshot
        {
            SessionId = "2026-04-18-g4",
            SessionStatus = "active",
            Mode = "hybrid",
            Scope = "medium",
            CurrentTurn = 2,
            MaxTurns = 5,
            LastAgent = AgentRole.Claude,
            OriginBacklogId = "2026-04-18-g4",
            TaskSummary = "Verify popup flow.",
            ContractStatus = "accepted",
            Packets = new[] { "Document/dialogue/sessions/2026-04-18-g4/turn-1.yaml" },
        };

        var json = SessionStatePersister.Render(snap);

        Assert.Contains("\"protocol_version\": \"dad-v2\"", json);
        Assert.Contains("\"session_id\": \"2026-04-18-g4\"", json);
        Assert.Contains("\"session_status\": \"active\"", json);
        Assert.Contains("\"current_turn\": 2", json);
        Assert.Contains("\"last_agent\": \"claude-code\"", json);
        Assert.Contains("\"max_turns\": 5", json);
        Assert.Contains("\"origin_backlog_id\": \"2026-04-18-g4\"", json);
    }

    [Fact]
    public async Task WriteAsync_atomic_and_overwrites()
    {
        var dir = Path.Combine(Path.GetTempPath(), "g4-state-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "state.json");
        try
        {
            var snap1 = new SessionStateSnapshot
            {
                SessionId = "s",
                SessionStatus = "active",
                CurrentTurn = 1,
                MaxTurns = 2,
                LastAgent = AgentRole.Codex,
                OriginBacklogId = "s",
                TaskSummary = "first",
            };
            await SessionStatePersister.WriteAsync(snap1, path);

            var snap2 = new SessionStateSnapshot
            {
                SessionId = "s",
                SessionStatus = "converged",
                CurrentTurn = 5,
                MaxTurns = 5,
                LastAgent = AgentRole.Claude,
                OriginBacklogId = "s",
                TaskSummary = "second",
                ClosedReason = "done",
            };
            var bytes = await SessionStatePersister.WriteAsync(snap2, path);

            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("\"current_turn\": 5", content);
            Assert.Contains("\"last_agent\": \"claude-code\"", content);
            Assert.Contains("\"closed_reason\": \"done\"", content);
            Assert.Equal(new FileInfo(path).Length, bytes);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Render_escapes_special_chars_in_session_id()
    {
        var snap = new SessionStateSnapshot
        {
            SessionId = "weird\"id\\here",
            SessionStatus = "active",
            CurrentTurn = 1,
            MaxTurns = 2,
            LastAgent = AgentRole.Codex,
            OriginBacklogId = "weird\"id\\here",
            TaskSummary = "x",
        };

        var json = SessionStatePersister.Render(snap);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("weird\"id\\here", doc.RootElement.GetProperty("session_id").GetString());
    }
}
