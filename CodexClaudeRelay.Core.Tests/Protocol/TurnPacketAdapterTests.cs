using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Protocol;

public class TurnPacketAdapterTests
{
    [Fact]
    public void FromHandoffEnvelope_carries_checkpoint_results()
    {
        var env = new HandoffEnvelope
        {
            Source = AgentRole.Codex,
            Target = AgentRole.Claude,
            SessionId = "2026-04-18-g3x",
            Turn = 3,
            Ready = true,
            Prompt = "next",
            Reason = "peer turn",
            CheckpointResults = new[]
            {
                new CheckpointResult { CheckpointId = "c1", Status = CheckpointStatus.Pass },
                new CheckpointResult { CheckpointId = "c2", Status = CheckpointStatus.Fail, EvidenceRef = "logs/x.txt" },
            },
        };

        var packet = TurnPacketAdapter.FromHandoffEnvelope(env);

        Assert.Equal(AgentRole.Codex, packet.From);
        Assert.Equal(3, packet.Turn);
        Assert.Equal(CloseoutKind.PeerHandoff, packet.Handoff.CloseoutKind);
        Assert.Equal("accepted", packet.Contract.Status);
        Assert.Contains("c1", packet.Contract.Checkpoints);
        Assert.Equal(2, packet.PeerReview.CheckpointResults.Count);
        Assert.Equal("c1", packet.PeerReview.CheckpointResults[0].CheckpointId);
        Assert.Equal(CheckpointStatus.Fail, packet.PeerReview.CheckpointResults[1].Status);
        Assert.Equal("logs/x.txt", packet.PeerReview.CheckpointResults[1].EvidenceRef);
        Assert.Single(packet.PeerReview.IssuesFound);
        Assert.Equal("next", packet.MyWork.Plan);
        Assert.Contains("logs/x.txt", packet.MyWork.Evidence.Artifacts);
    }

    [Fact]
    public void FromHandoffEnvelope_empty_results_produces_empty_peer_review()
    {
        var env = new HandoffEnvelope
        {
            Source = AgentRole.Claude,
            SessionId = "s",
            Turn = 1,
            Ready = true,
        };

        var packet = TurnPacketAdapter.FromHandoffEnvelope(env);

        Assert.Empty(packet.PeerReview.CheckpointResults);
        Assert.Equal("medium", packet.MyWork.Confidence);
    }

    [Fact]
    public void FromHandoffEnvelope_carries_closeout_kind_recovery_resume()
    {
        var env = new HandoffEnvelope
        {
            Source = AgentRole.Claude,
            Target = AgentRole.Codex,
            SessionId = "2026-04-18-g5",
            Turn = 4,
            Ready = false,
            Prompt = "context overflow",
            CloseoutKind = Models.CloseoutKind.RecoveryResume,
        };

        var packet = TurnPacketAdapter.FromHandoffEnvelope(env);

        Assert.Equal(Models.CloseoutKind.RecoveryResume, packet.Handoff.CloseoutKind);
        Assert.Equal(AgentRole.Claude, packet.From);
        Assert.Equal(4, packet.Turn);
        Assert.Equal("low", packet.MyWork.Confidence);
    }

    [Fact]
    public void FromHandoffEnvelope_enables_verifier_block_detection()
    {
        var env = new HandoffEnvelope
        {
            Source = AgentRole.Codex,
            SessionId = "s",
            Turn = 1,
            Ready = true,
            CheckpointResults = new[]
            {
                new CheckpointResult { CheckpointId = "c1", Status = CheckpointStatus.Fail, EvidenceRef = "" },
            },
        };

        var packet = TurnPacketAdapter.FromHandoffEnvelope(env);
        var report = CheckpointVerifier.Verify(packet);

        Assert.True(report.BlocksTurnClose);
        Assert.Equal(new[] { "c1" }, report.MissingEvidenceFor);
    }
}
