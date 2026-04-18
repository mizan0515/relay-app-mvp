using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Protocol;

/// <summary>
/// G1 core evidence: symmetric round-trip for both peers.
/// Covers the full set of fields the TurnPacketYamlPersister emits so the
/// Read parser stays in lock-step with the Write emitter.
/// </summary>
public class PacketIOTests
{
    [Theory]
    [InlineData(AgentRole.Codex)]
    [InlineData(AgentRole.Claude)]
    public async Task Round_trip_file_preserves_all_fields(string from)
    {
        var packet = new TurnPacket
        {
            From = from,
            Turn = 3,
            SessionId = "2026-04-19-g1",
            Handoff = new TurnHandoff
            {
                CloseoutKind = CloseoutKind.PeerHandoff,
                NextTask = "verify packet round-trip",
                Context = "turn-3 follow-up: colon-heavy text, quotes \" and backslash \\",
                PromptArtifact = "turn-3-handoff.md",
                ReadyForPeerVerification = true,
                SuggestDone = false,
                DoneReason = string.Empty,
                Questions = new[] { "does the parser handle empty?", "colon: in question" },
            },
            PeerReview = new PeerReview
            {
                CheckpointResults = new[]
                {
                    new CheckpointResult { CheckpointId = "cp-1", Status = CheckpointStatus.Pass, EvidenceRef = "artifacts/cp-1.json" },
                    new CheckpointResult { CheckpointId = "cp-2", Status = CheckpointStatus.Fail,  EvidenceRef = "artifacts/cp-2.json" },
                },
            },
        };

        var tmp = Path.Combine(Path.GetTempPath(), $"packetio-{Guid.NewGuid():N}.yaml");
        try
        {
            await PacketIO.WriteAsync(packet, tmp);
            var round = await PacketIO.ReadAsync(tmp);

            Assert.Equal(packet.Type, round.Type);
            Assert.Equal(packet.From, round.From);
            Assert.Equal(packet.Turn, round.Turn);
            Assert.Equal(packet.SessionId, round.SessionId);

            Assert.Equal(packet.Handoff.CloseoutKind, round.Handoff.CloseoutKind);
            Assert.Equal(packet.Handoff.NextTask, round.Handoff.NextTask);
            Assert.Equal(packet.Handoff.Context, round.Handoff.Context);
            Assert.Equal(packet.Handoff.PromptArtifact, round.Handoff.PromptArtifact);
            Assert.Equal(packet.Handoff.ReadyForPeerVerification, round.Handoff.ReadyForPeerVerification);
            Assert.Equal(packet.Handoff.SuggestDone, round.Handoff.SuggestDone);
            Assert.Equal(packet.Handoff.DoneReason, round.Handoff.DoneReason);
            Assert.Equal(packet.Handoff.Questions, round.Handoff.Questions);

            Assert.Equal(packet.PeerReview.CheckpointResults.Count, round.PeerReview.CheckpointResults.Count);
            for (int i = 0; i < packet.PeerReview.CheckpointResults.Count; i++)
            {
                var a = packet.PeerReview.CheckpointResults[i];
                var b = round.PeerReview.CheckpointResults[i];
                Assert.Equal(a.CheckpointId, b.CheckpointId);
                Assert.Equal(a.Status, b.Status);
                Assert.Equal(a.EvidenceRef, b.EvidenceRef);
            }
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Parse_handles_empty_questions_and_empty_checkpoint_list()
    {
        var packet = new TurnPacket
        {
            From = AgentRole.Codex,
            Turn = 1,
            SessionId = "empty-lists",
            Handoff = new TurnHandoff
            {
                CloseoutKind = CloseoutKind.FinalNoHandoff,
                NextTask = "",
                Context = "",
                SuggestDone = true,
                DoneReason = "complete",
            },
        };

        var yaml = TurnPacketYamlPersister.Render(packet);
        var round = PacketIO.Parse(yaml);

        Assert.Equal(packet.From, round.From);
        Assert.Equal(packet.Turn, round.Turn);
        Assert.Equal(packet.SessionId, round.SessionId);
        Assert.Equal(packet.Handoff.CloseoutKind, round.Handoff.CloseoutKind);
        Assert.Equal(packet.Handoff.SuggestDone, round.Handoff.SuggestDone);
        Assert.Equal(packet.Handoff.DoneReason, round.Handoff.DoneReason);
        Assert.Empty(round.Handoff.Questions);
        Assert.Empty(round.PeerReview.CheckpointResults);
    }
}
