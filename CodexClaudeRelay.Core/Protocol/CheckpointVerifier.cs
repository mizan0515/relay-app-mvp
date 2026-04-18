using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Protocol;

/// <summary>
/// G3: PASS/FAIL collection. Walks the turn packet's peer_review.checkpoint_results,
/// emits one verification record per entry, and refuses to close the turn if a
/// non-PASS result lacks evidence.
///
/// Pure function. No broker state, no I/O. Broker wires the output into
/// log events and the "turn close" gate in a follow-up commit.
/// </summary>
public static class CheckpointVerifier
{
    public sealed record VerificationRecord(
        string CheckpointId,
        string Status,
        string EvidenceRef,
        bool EvidenceMissing);

    public sealed record VerificationReport(
        IReadOnlyList<VerificationRecord> Records,
        bool BlocksTurnClose,
        IReadOnlyList<string> MissingEvidenceFor);

    public static VerificationReport Verify(TurnPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var results = packet.PeerReview.CheckpointResults;
        var records = new List<VerificationRecord>(results.Count);
        var missing = new List<string>();

        foreach (var r in results)
        {
            if (string.IsNullOrWhiteSpace(r.CheckpointId))
                throw new InvalidOperationException("CheckpointResult.CheckpointId is required.");
            if (string.IsNullOrWhiteSpace(r.Status))
                throw new InvalidOperationException($"CheckpointResult.Status is required (id={r.CheckpointId}).");

            var evidenceMissing =
                !string.Equals(r.Status, CheckpointStatus.Pass, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(r.EvidenceRef);

            if (evidenceMissing) missing.Add(r.CheckpointId);

            records.Add(new VerificationRecord(
                r.CheckpointId,
                r.Status,
                r.EvidenceRef,
                evidenceMissing));
        }

        return new VerificationReport(records, missing.Count > 0, missing);
    }
}
