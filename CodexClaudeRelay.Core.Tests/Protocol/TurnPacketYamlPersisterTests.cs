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
    }

    [Fact]
    public void Render_empty_collections_as_flow_sequences()
    {
        var packet = new TurnPacket { SessionId = "s", Turn = 1 };

        var yaml = TurnPacketYamlPersister.Render(packet);

        Assert.Contains("questions: []", yaml);
        Assert.Contains("checkpoint_results: []", yaml);
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
}
