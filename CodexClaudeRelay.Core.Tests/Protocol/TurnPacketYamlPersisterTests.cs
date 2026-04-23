using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Protocol;

public class TurnPacketYamlPersisterTests
{
    [Fact]
    public void Render_emits_required_top_level_fields()
    {
        var packet = new TurnPacket
        {
            From = AgentRole.Claude,
            Turn = 2,
            SessionId = "2026-04-18-g4",
            Handoff = new TurnHandoff
            {
                CloseoutKind = CloseoutKind.PeerHandoff,
                NextTask = "review turn-1 evidence",
                Context = "turn-2 follow-up",
                ReadyForPeerVerification = true,
            },
        };

        var yaml = TurnPacketYamlPersister.Render(packet);

        Assert.Contains("type: turn", yaml);
        Assert.Contains("from: claude-code", yaml);
        Assert.Contains("turn: 2", yaml);
        Assert.Contains("session_id: 2026-04-18-g4", yaml);
        Assert.Contains("contract:", yaml);
        Assert.Contains("my_work:", yaml);
        Assert.Contains("closeout_kind: peer_handoff", yaml);
        Assert.Contains("ready_for_peer_verification: true", yaml);
    }

    [Fact]
    public void Render_emits_checkpoint_results_list()
    {
        var packet = new TurnPacket
        {
            SessionId = "s",
            Turn = 1,
            PeerReview = new PeerReview
            {
                CheckpointResults = new[]
                {
                    new CheckpointResult { CheckpointId = "c1", Status = CheckpointStatus.Pass },
                    new CheckpointResult { CheckpointId = "c2", Status = CheckpointStatus.Fail, EvidenceRef = "logs/x.txt:42" },
                },
            },
        };

        var yaml = TurnPacketYamlPersister.Render(packet);

        Assert.Contains("checkpoint_results:", yaml);
        Assert.Contains("- checkpoint_id: c1", yaml);
        Assert.Contains("status: PASS", yaml);
        Assert.Contains("- checkpoint_id: c2", yaml);
        Assert.Contains("status: FAIL", yaml);
        Assert.Contains("evidence_ref: \"logs/x.txt:42\"", yaml);
        Assert.Contains("issues_found: []", yaml);
    }

    [Fact]
    public void Render_empty_collections_as_flow_sequences()
    {
        var packet = new TurnPacket { SessionId = "s", Turn = 1 };

        var yaml = TurnPacketYamlPersister.Render(packet);

        Assert.Contains("checkpoints: []", yaml);
        Assert.Contains("questions: []", yaml);
        Assert.Contains("checkpoint_results: []", yaml);
        Assert.Contains("open_risks: []", yaml);
    }

    [Fact]
    public async Task WriteAsync_creates_parent_directory_and_leaves_no_tmp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "g4-yaml-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "nested", "turn-1.yaml");
        try
        {
            var packet = new TurnPacket { SessionId = "s", Turn = 1 };

            var bytes = await TurnPacketYamlPersister.WriteAsync(packet, path);

            Assert.True(File.Exists(path));
            Assert.Equal(new FileInfo(path).Length, bytes);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_overwrites_existing_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "g4-yaml-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "turn-1.yaml");
        try
        {
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(path, "stale");

            var packet = new TurnPacket { SessionId = "s", Turn = 7 };
            await TurnPacketYamlPersister.WriteAsync(packet, path);

            var written = await File.ReadAllTextAsync(path);
            Assert.Contains("turn: 7", written);
            Assert.DoesNotContain("stale", written);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Render_quotes_colons_and_special_chars()
    {
        var packet = new TurnPacket
        {
            SessionId = "s",
            Turn = 1,
            Handoff = new TurnHandoff { NextTask = "review: evidence" },
        };

        var yaml = TurnPacketYamlPersister.Render(packet);

        Assert.Contains("next_task: \"review: evidence\"", yaml);
    }

    [Fact]
    public void Render_emits_contract_peer_review_and_my_work_sections()
    {
        var packet = new TurnPacket
        {
            SessionId = "s",
            Turn = 1,
            Contract = new TurnContract
            {
                Status = "accepted",
                Checkpoints = new[] { "C1", "C2" },
            },
            PeerReview = new PeerReview
            {
                ProjectAnalysis = "analysis",
                TaskModelReview = new TaskModelReview
                {
                    RiskFollowups = new[] { "watch ui drift" },
                },
                IssuesFound = new[] { "C2 reported FAIL at logs/x.txt" },
                FixesApplied = new[] { "updated popup tint" },
            },
            MyWork = new MyWork
            {
                Plan = "apply narrow fix",
                Changes = new WorkChanges
                {
                    Summary = "updated popup tint",
                },
                Evidence = new WorkEvidence
                {
                    Artifacts = new[] { "logs/x.txt" },
                },
                Verification = "C1:PASS",
                OpenRisks = new[] { "need peer review" },
                Confidence = "medium",
            },
        };

        var yaml = TurnPacketYamlPersister.Render(packet);

        Assert.Contains("status: accepted", yaml);
        Assert.Contains("checkpoints:", yaml);
        Assert.Contains("project_analysis: analysis", yaml);
        Assert.Contains("risk_followups:", yaml);
        Assert.Contains("fixes_applied:", yaml);
        Assert.Contains("plan: apply narrow fix", yaml);
        Assert.Contains("artifacts:", yaml);
        Assert.Contains("confidence: medium", yaml);
    }
}
