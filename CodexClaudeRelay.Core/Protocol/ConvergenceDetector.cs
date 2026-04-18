using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Protocol;

public readonly record struct ConvergenceDecision(
    bool IsConverged,
    string Reason,
    IReadOnlyList<string> MatchingCheckpoints);

public static class ConvergenceDetector
{
    public static ConvergenceDecision Evaluate(TurnPacket? previousTurn, TurnPacket currentTurn)
    {
        if (previousTurn is null)
        {
            return new ConvergenceDecision(false, "no previous turn", Array.Empty<string>());
        }

        var prev = previousTurn.Handoff;
        var curr = currentTurn.Handoff;

        if (!string.Equals(prev.CloseoutKind, CloseoutKind.PeerHandoff, StringComparison.Ordinal) ||
            !string.Equals(curr.CloseoutKind, CloseoutKind.PeerHandoff, StringComparison.Ordinal))
        {
            return new ConvergenceDecision(false, "closeout_kind not peer_handoff on both turns", Array.Empty<string>());
        }

        if (!prev.ReadyForPeerVerification || !curr.ReadyForPeerVerification)
        {
            return new ConvergenceDecision(false, "ready_for_peer_verification not set on both turns", Array.Empty<string>());
        }

        if (!prev.SuggestDone || !curr.SuggestDone)
        {
            return new ConvergenceDecision(false, "suggest_done not set on both turns", Array.Empty<string>());
        }

        if (string.Equals(previousTurn.From, currentTurn.From, StringComparison.Ordinal))
        {
            return new ConvergenceDecision(false, "both turns came from the same peer", Array.Empty<string>());
        }

        var prevResults = previousTurn.PeerReview.CheckpointResults;
        var currResults = currentTurn.PeerReview.CheckpointResults;

        if (prevResults.Count == 0 || currResults.Count == 0)
        {
            return new ConvergenceDecision(false, "checkpoint_results empty on at least one turn", Array.Empty<string>());
        }

        var prevMap = prevResults
            .GroupBy(r => r.CheckpointId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Status, StringComparer.Ordinal);
        var currMap = currResults
            .GroupBy(r => r.CheckpointId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Status, StringComparer.Ordinal);

        if (prevMap.Count != currMap.Count)
        {
            return new ConvergenceDecision(false, "checkpoint_results id sets differ in size", Array.Empty<string>());
        }

        var matching = new List<string>();
        foreach (var kv in prevMap)
        {
            if (!currMap.TryGetValue(kv.Key, out var otherStatus) ||
                !string.Equals(kv.Value, otherStatus, StringComparison.Ordinal))
            {
                return new ConvergenceDecision(false, $"checkpoint {kv.Key} mismatch or missing on current turn", Array.Empty<string>());
            }

            matching.Add(kv.Key);
        }

        matching.Sort(StringComparer.Ordinal);
        return new ConvergenceDecision(true, "both peers agree across consecutive turns", matching);
    }
}
