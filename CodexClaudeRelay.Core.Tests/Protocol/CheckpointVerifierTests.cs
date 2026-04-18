using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Protocol;

public class CheckpointVerifierTests
{
    private static TurnPacket PacketWith(params CheckpointResult[] results) => new()
    {
        From = AgentRole.Codex,
        Turn = 1,
        SessionId = "2026-04-18-g3",
        Handoff = new TurnHandoff { CloseoutKind = CloseoutKind.PeerHandoff },
        PeerReview = new PeerReview { CheckpointResults = results },
    };

    [Fact]
    public void Verify_all_pass_does_not_block_close()
    {
        var packet = PacketWith(
            new CheckpointResult { CheckpointId = "c1", Status = CheckpointStatus.Pass },
            new CheckpointResult { CheckpointId = "c2", Status = CheckpointStatus.Pass });

        var report = CheckpointVerifier.Verify(packet);

        Assert.Equal(2, report.Records.Count);
        Assert.False(report.BlocksTurnClose);
        Assert.Empty(report.MissingEvidenceFor);
        Assert.All(report.Records, r => Assert.False(r.EvidenceMissing));
    }

    [Fact]
    public void Verify_non_pass_without_evidence_blocks_close()
    {
        var packet = PacketWith(
            new CheckpointResult { CheckpointId = "c1", Status = CheckpointStatus.Pass },
            new CheckpointResult { CheckpointId = "c2", Status = CheckpointStatus.Fail, EvidenceRef = "" });

        var report = CheckpointVerifier.Verify(packet);

        Assert.True(report.BlocksTurnClose);
        Assert.Equal(new[] { "c2" }, report.MissingEvidenceFor);
        Assert.True(report.Records[1].EvidenceMissing);
    }

    [Fact]
    public void Verify_non_pass_with_evidence_does_not_block_close()
    {
        var packet = PacketWith(
            new CheckpointResult { CheckpointId = "c1", Status = CheckpointStatus.Fail, EvidenceRef = "logs/build.txt:42" },
            new CheckpointResult { CheckpointId = "c2", Status = CheckpointStatus.Blocked, EvidenceRef = "ADR-007" });

        var report = CheckpointVerifier.Verify(packet);

        Assert.False(report.BlocksTurnClose);
        Assert.Empty(report.MissingEvidenceFor);
    }

    [Fact]
    public void Verify_empty_results_does_not_block_close()
    {
        var packet = PacketWith();

        var report = CheckpointVerifier.Verify(packet);

        Assert.Empty(report.Records);
        Assert.False(report.BlocksTurnClose);
    }

    [Fact]
    public void Verify_missing_checkpoint_id_throws()
    {
        var packet = PacketWith(
            new CheckpointResult { CheckpointId = "", Status = CheckpointStatus.Pass });

        Assert.Throws<InvalidOperationException>(() => CheckpointVerifier.Verify(packet));
    }

    [Fact]
    public void Verify_missing_status_throws()
    {
        var packet = PacketWith(
            new CheckpointResult { CheckpointId = "c1", Status = "" });

        Assert.Throws<InvalidOperationException>(() => CheckpointVerifier.Verify(packet));
    }
}
