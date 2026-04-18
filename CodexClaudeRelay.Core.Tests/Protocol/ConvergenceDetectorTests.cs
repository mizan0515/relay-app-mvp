using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;
using Xunit;

namespace CodexClaudeRelay.Core.Tests.Protocol;

public class ConvergenceDetectorTests
{
    private static TurnPacket MakeTurn(
        string from,
        int turn,
        bool ready = true,
        bool suggestDone = true,
        string closeoutKind = CloseoutKind.PeerHandoff,
        IReadOnlyList<CheckpointResult>? checkpoints = null) =>
        new()
        {
            From = from,
            Turn = turn,
            SessionId = "2026-04-18-g7",
            Handoff = new TurnHandoff
            {
                CloseoutKind = closeoutKind,
                ReadyForPeerVerification = ready,
                SuggestDone = suggestDone,
                DoneReason = "work complete",
            },
            PeerReview = new PeerReview
            {
                CheckpointResults = checkpoints ?? new[]
                {
                    new CheckpointResult { CheckpointId = "cp-1", Status = CheckpointStatus.Pass, EvidenceRef = "log:a" },
                    new CheckpointResult { CheckpointId = "cp-2", Status = CheckpointStatus.Pass, EvidenceRef = "log:b" },
                },
            },
        };

    [Fact]
    public void Evaluate_returns_converged_when_opposite_peers_agree_with_matching_checkpoints()
    {
        var prev = MakeTurn(AgentRole.Codex, 1);
        var curr = MakeTurn(AgentRole.Claude, 2);

        var decision = ConvergenceDetector.Evaluate(prev, curr);

        Assert.True(decision.IsConverged);
        Assert.Contains("both peers agree", decision.Reason);
        Assert.Equal(new[] { "cp-1", "cp-2" }, decision.MatchingCheckpoints);
    }

    [Fact]
    public void Evaluate_is_order_agnostic_across_peer_ordering()
    {
        var codexFirst = ConvergenceDetector.Evaluate(
            MakeTurn(AgentRole.Codex, 1),
            MakeTurn(AgentRole.Claude, 2));
        var claudeFirst = ConvergenceDetector.Evaluate(
            MakeTurn(AgentRole.Claude, 1),
            MakeTurn(AgentRole.Codex, 2));

        Assert.True(codexFirst.IsConverged);
        Assert.True(claudeFirst.IsConverged);
    }

    [Fact]
    public void Evaluate_rejects_when_previous_turn_is_null()
    {
        var curr = MakeTurn(AgentRole.Claude, 2);

        var decision = ConvergenceDetector.Evaluate(null, curr);

        Assert.False(decision.IsConverged);
        Assert.Contains("no previous turn", decision.Reason);
    }

    [Fact]
    public void Evaluate_rejects_recovery_resume_closeout_even_when_flags_set()
    {
        var prev = MakeTurn(AgentRole.Codex, 1, closeoutKind: CloseoutKind.RecoveryResume);
        var curr = MakeTurn(AgentRole.Claude, 2);

        var decision = ConvergenceDetector.Evaluate(prev, curr);

        Assert.False(decision.IsConverged);
        Assert.Contains("closeout_kind not peer_handoff", decision.Reason);
    }

    [Fact]
    public void Evaluate_rejects_same_peer_consecutive_turns()
    {
        var prev = MakeTurn(AgentRole.Codex, 1);
        var curr = MakeTurn(AgentRole.Codex, 2);

        var decision = ConvergenceDetector.Evaluate(prev, curr);

        Assert.False(decision.IsConverged);
        Assert.Contains("same peer", decision.Reason);
    }

    [Fact]
    public void Evaluate_rejects_when_checkpoint_statuses_differ()
    {
        var prev = MakeTurn(AgentRole.Codex, 1);
        var curr = MakeTurn(AgentRole.Claude, 2, checkpoints: new[]
        {
            new CheckpointResult { CheckpointId = "cp-1", Status = CheckpointStatus.Pass, EvidenceRef = "log:a" },
            new CheckpointResult { CheckpointId = "cp-2", Status = CheckpointStatus.Fail, EvidenceRef = "log:b" },
        });

        var decision = ConvergenceDetector.Evaluate(prev, curr);

        Assert.False(decision.IsConverged);
        Assert.Contains("cp-2", decision.Reason);
        Assert.Empty(decision.MatchingCheckpoints);
    }

    [Fact]
    public void Evaluate_rejects_when_suggest_done_missing_on_either_side()
    {
        var prev = MakeTurn(AgentRole.Codex, 1, suggestDone: false);
        var curr = MakeTurn(AgentRole.Claude, 2);

        var decision = ConvergenceDetector.Evaluate(prev, curr);

        Assert.False(decision.IsConverged);
        Assert.Contains("suggest_done", decision.Reason);
    }
}
