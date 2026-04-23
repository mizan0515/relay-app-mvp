using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Protocol;

public class RoundTripArtifactSmokeTests
{
    private static async Task WriteTurnArtifactsAsync(
        string sessionDir, int turn, string source, string target, string sessionId)
    {
        var packet = new TurnPacket
        {
            From = source,
            Turn = turn,
            SessionId = sessionId,
            Handoff = new TurnHandoff
            {
                CloseoutKind = CloseoutKind.PeerHandoff,
                NextTask = $"turn-{turn + 1} work for {target}",
                Context = $"from {source}",
                ReadyForPeerVerification = true,
            },
        };
        await HandoffArtifactPersister.WriteAsync(packet, Path.Combine(sessionDir, $"turn-{turn}-handoff.md"));
        await TurnPacketYamlPersister.WriteAsync(packet, Path.Combine(sessionDir, $"turn-{turn}.yaml"));

        var snap = new SessionStateSnapshot
        {
            SessionId = sessionId,
            SessionStatus = "active",
            CurrentTurn = turn + 1,
            MaxTurns = 5,
            LastAgent = source,
            OriginBacklogId = sessionId,
            TaskSummary = $"round-trip {sessionId}",
            ContractStatus = "accepted",
            Packets = Enumerable.Range(1, turn)
                .Select(n => $"Document/dialogue/sessions/{sessionId}/turn-{n}.yaml")
                .ToArray(),
        };
        await SessionStatePersister.WriteAsync(snap, Path.Combine(sessionDir, "state.json"));
    }

    [Fact]
    public async Task Full_round_trip_produces_session_directory_with_both_turns_and_final_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "g4-smoke-" + Guid.NewGuid().ToString("N"));
        var sessionId = "2026-04-18-rt";
        var sessionDir = Path.Combine(root, "sessions", sessionId);
        try
        {
            // turn-1: Codex → Claude
            await WriteTurnArtifactsAsync(sessionDir, 1, AgentRole.Codex, AgentRole.Claude, sessionId);
            // turn-2: Claude → Codex
            await WriteTurnArtifactsAsync(sessionDir, 2, AgentRole.Claude, AgentRole.Codex, sessionId);

            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-1-handoff.md")));
            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-1.yaml")));
            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-2-handoff.md")));
            Assert.True(File.Exists(Path.Combine(sessionDir, "turn-2.yaml")));
            Assert.True(File.Exists(Path.Combine(sessionDir, "state.json")));
            Assert.Empty(Directory.GetFiles(sessionDir, "*.tmp"));

            var state = await File.ReadAllTextAsync(Path.Combine(sessionDir, "state.json"));
            Assert.Contains("\"current_turn\": 3", state);
            Assert.Contains("\"last_agent\": \"claude-code\"", state);

            var turn1Yaml = await File.ReadAllTextAsync(Path.Combine(sessionDir, "turn-1.yaml"));
            Assert.Contains("from: codex", turn1Yaml);
            Assert.Contains("turn: 1", turn1Yaml);

            var turn2Yaml = await File.ReadAllTextAsync(Path.Combine(sessionDir, "turn-2.yaml"));
            Assert.Contains("from: claude-code", turn2Yaml);
            Assert.Contains("turn: 2", turn2Yaml);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Round_trip_is_peer_symmetric_when_claude_starts()
    {
        var root = Path.Combine(Path.GetTempPath(), "g4-smoke-rev-" + Guid.NewGuid().ToString("N"));
        var sessionId = "2026-04-18-rt-rev";
        var sessionDir = Path.Combine(root, "sessions", sessionId);
        try
        {
            // turn-1: Claude → Codex (starter swapped)
            await WriteTurnArtifactsAsync(sessionDir, 1, AgentRole.Claude, AgentRole.Codex, sessionId);
            // turn-2: Codex → Claude
            await WriteTurnArtifactsAsync(sessionDir, 2, AgentRole.Codex, AgentRole.Claude, sessionId);

            Assert.Equal(5, Directory.GetFiles(sessionDir).Length);
            var state = await File.ReadAllTextAsync(Path.Combine(sessionDir, "state.json"));
            Assert.Contains("\"current_turn\": 3", state);
            Assert.Contains("\"last_agent\": \"codex\"", state);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task State_json_overwrite_reflects_last_turn_not_intermediate()
    {
        var root = Path.Combine(Path.GetTempPath(), "g4-smoke-ow-" + Guid.NewGuid().ToString("N"));
        var sessionId = "s";
        var sessionDir = Path.Combine(root, "sessions", sessionId);
        try
        {
            await WriteTurnArtifactsAsync(sessionDir, 1, AgentRole.Codex, AgentRole.Claude, sessionId);
            var afterTurn1 = await File.ReadAllTextAsync(Path.Combine(sessionDir, "state.json"));
            Assert.Contains("\"current_turn\": 2", afterTurn1);

            await WriteTurnArtifactsAsync(sessionDir, 2, AgentRole.Claude, AgentRole.Codex, sessionId);
            var afterTurn2 = await File.ReadAllTextAsync(Path.Combine(sessionDir, "state.json"));
            Assert.Contains("\"current_turn\": 3", afterTurn2);
            Assert.DoesNotContain("\"current_turn\": 2,", afterTurn2);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
