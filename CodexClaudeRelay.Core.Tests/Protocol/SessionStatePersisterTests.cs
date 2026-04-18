using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Protocol;

public class SessionStatePersisterTests
{
    [Fact]
    public void Render_emits_core_fields()
    {
        var snap = new SessionStateSnapshot(
            "2026-04-18-g4", 2, AgentRole.Claude,
            new DateTimeOffset(2026, 4, 18, 13, 35, 0, TimeSpan.Zero));

        var json = SessionStatePersister.Render(snap);

        Assert.Contains("\"session_id\": \"2026-04-18-g4\"", json);
        Assert.Contains("\"current_turn\": 2", json);
        Assert.Contains("\"active_agent\": \"claude-code\"", json);
        Assert.Contains("\"updated_at\":", json);
    }

    [Fact]
    public async Task WriteAsync_atomic_and_overwrites()
    {
        var dir = Path.Combine(Path.GetTempPath(), "g4-state-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "state.json");
        try
        {
            var snap1 = new SessionStateSnapshot("s", 1, AgentRole.Codex, DateTimeOffset.Now);
            await SessionStatePersister.WriteAsync(snap1, path);

            var snap2 = new SessionStateSnapshot("s", 5, AgentRole.Claude, DateTimeOffset.Now);
            var bytes = await SessionStatePersister.WriteAsync(snap2, path);

            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("\"current_turn\": 5", content);
            Assert.Contains("\"active_agent\": \"claude-code\"", content);
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
        var snap = new SessionStateSnapshot(
            "weird\"id\\here", 1, AgentRole.Codex, DateTimeOffset.Now);

        var json = SessionStatePersister.Render(snap);

        Assert.Contains("\"session_id\": \"weird\\\"id\\\\here\"", json);
    }
}
