using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Protocol;

public static class TurnPacketAdapter
{
    public static TurnPacket FromHandoffEnvelope(HandoffEnvelope env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var checkpointIds = env.CheckpointResults
            .Where(static item => !string.IsNullOrWhiteSpace(item.CheckpointId))
            .Select(static item => item.CheckpointId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var issueLines = env.CheckpointResults
            .Where(static item => string.Equals(item.Status, CheckpointStatus.Fail, StringComparison.Ordinal) ||
                                  string.Equals(item.Status, CheckpointStatus.Blocked, StringComparison.Ordinal))
            .Select(static item =>
                string.IsNullOrWhiteSpace(item.EvidenceRef)
                    ? $"{item.CheckpointId} reported {item.Status}"
                    : $"{item.CheckpointId} reported {item.Status} at {item.EvidenceRef}")
            .ToArray();
        var artifactRefs = env.CheckpointResults
            .Where(static item => !string.IsNullOrWhiteSpace(item.EvidenceRef))
            .Select(static item => item.EvidenceRef)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var confidence = string.Equals(env.CloseoutKind, CloseoutKind.RecoveryResume, StringComparison.Ordinal)
            ? "low"
            : env.SuggestDone
                ? "high"
                : "medium";
        var verification = env.CheckpointResults.Count == 0
            ? "No explicit checkpoint results were attached to this handoff."
            : string.Join("; ", env.CheckpointResults.Select(static item =>
                $"{item.CheckpointId}:{item.Status}{(string.IsNullOrWhiteSpace(item.EvidenceRef) ? string.Empty : $" ({item.EvidenceRef})")}"));
        var openRisks = new List<string>();
        if (!string.IsNullOrWhiteSpace(env.Reason) &&
            (string.Equals(env.CloseoutKind, CloseoutKind.RecoveryResume, StringComparison.Ordinal) || !env.SuggestDone))
        {
            openRisks.Add(env.Reason);
        }

        foreach (var constraint in env.Constraints)
        {
            if (!string.IsNullOrWhiteSpace(constraint))
            {
                openRisks.Add(constraint);
            }
        }

        return new TurnPacket
        {
            From = env.Source,
            Turn = env.Turn,
            SessionId = env.SessionId,
            Contract = new TurnContract
            {
                Status = "accepted",
                Checkpoints = checkpointIds,
            },
            Handoff = new TurnHandoff
            {
                CloseoutKind = string.IsNullOrWhiteSpace(env.CloseoutKind)
                    ? CloseoutKind.PeerHandoff
                    : env.CloseoutKind,
                NextTask = env.Prompt,
                Context = env.Reason,
                ReadyForPeerVerification = env.Ready,
                SuggestDone = env.SuggestDone,
                DoneReason = env.DoneReason,
            },
            PeerReview = new PeerReview
            {
                ProjectAnalysis = env.Reason,
                TaskModelReview = new TaskModelReview
                {
                    Status = "aligned",
                    RiskFollowups = openRisks.ToArray(),
                },
                CheckpointResults = env.CheckpointResults,
                IssuesFound = issueLines,
                FixesApplied = env.Completed.Where(static item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            },
            MyWork = new MyWork
            {
                Plan = env.Prompt,
                Changes = new WorkChanges
                {
                    Summary = env.Completed.Count == 0
                        ? string.Join("; ", env.Summary.Where(static item => !string.IsNullOrWhiteSpace(item)))
                        : string.Join("; ", env.Completed.Where(static item => !string.IsNullOrWhiteSpace(item))),
                },
                SelfIterations = 1,
                Evidence = new WorkEvidence
                {
                    Artifacts = artifactRefs,
                },
                Verification = verification,
                OpenRisks = openRisks,
                Confidence = confidence,
            },
        };
    }
}
