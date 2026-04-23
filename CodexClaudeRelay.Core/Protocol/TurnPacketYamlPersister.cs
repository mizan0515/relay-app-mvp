using System.Text;
using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Protocol;

public static class TurnPacketYamlPersister
{
    public static async Task<long> WriteAsync(TurnPacket packet, string outPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentException.ThrowIfNullOrWhiteSpace(outPath);

        var body = Render(packet);

        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = outPath + ".tmp";
        await File.WriteAllTextAsync(tmp, body, ct).ConfigureAwait(false);
        File.Move(tmp, outPath, overwrite: true);

        return new FileInfo(outPath).Length;
    }

    public static string Render(TurnPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var sb = new StringBuilder();
        sb.Append("type: ").Append(Scalar(packet.Type)).Append('\n');
        sb.Append("from: ").Append(Scalar(packet.From)).Append('\n');
        sb.Append("turn: ").Append(packet.Turn).Append('\n');
        sb.Append("session_id: ").Append(Scalar(packet.SessionId)).Append('\n');

        sb.Append("contract:\n");
        var contract = packet.Contract;
        sb.Append("  status: ").Append(Scalar(contract.Status)).Append('\n');
        AppendList(sb, "  checkpoints", contract.Checkpoints);
        AppendList(sb, "  amendments", contract.Amendments);

        sb.Append("handoff:\n");
        var h = packet.Handoff;
        sb.Append("  closeout_kind: ").Append(Scalar(h.CloseoutKind)).Append('\n');
        sb.Append("  next_task: ").Append(Scalar(h.NextTask)).Append('\n');
        sb.Append("  context: ").Append(Scalar(h.Context)).Append('\n');
        sb.Append("  prompt_artifact: ").Append(Scalar(h.PromptArtifact)).Append('\n');
        sb.Append("  ready_for_peer_verification: ").Append(h.ReadyForPeerVerification ? "true" : "false").Append('\n');
        sb.Append("  suggest_done: ").Append(h.SuggestDone ? "true" : "false").Append('\n');
        sb.Append("  done_reason: ").Append(Scalar(h.DoneReason)).Append('\n');
        if (h.Questions.Count == 0)
        {
            sb.Append("  questions: []\n");
        }
        else
        {
            sb.Append("  questions:\n");
            foreach (var q in h.Questions)
                sb.Append("    - ").Append(Scalar(q)).Append('\n');
        }

        sb.Append("peer_review:\n");
        sb.Append("  project_analysis: ").Append(Scalar(packet.PeerReview.ProjectAnalysis)).Append('\n');
        sb.Append("  task_model_review:\n");
        sb.Append("    status: ").Append(Scalar(packet.PeerReview.TaskModelReview.Status)).Append('\n');
        AppendList(sb, "    coverage_gaps", packet.PeerReview.TaskModelReview.CoverageGaps);
        AppendList(sb, "    scope_creep", packet.PeerReview.TaskModelReview.ScopeCreep);
        AppendList(sb, "    risk_followups", packet.PeerReview.TaskModelReview.RiskFollowups);
        AppendList(sb, "    amendments", packet.PeerReview.TaskModelReview.Amendments);
        var results = packet.PeerReview.CheckpointResults;
        if (results.Count == 0)
        {
            sb.Append("  checkpoint_results: []\n");
        }
        else
        {
            sb.Append("  checkpoint_results:\n");
            foreach (var r in results)
            {
                sb.Append("    - checkpoint_id: ").Append(Scalar(r.CheckpointId)).Append('\n');
                sb.Append("      status: ").Append(Scalar(r.Status)).Append('\n');
                sb.Append("      evidence_ref: ").Append(Scalar(r.EvidenceRef)).Append('\n');
            }
        }
        AppendList(sb, "  issues_found", packet.PeerReview.IssuesFound);
        AppendList(sb, "  fixes_applied", packet.PeerReview.FixesApplied);

        sb.Append("my_work:\n");
        var myWork = packet.MyWork;
        sb.Append("  plan: ").Append(Scalar(myWork.Plan)).Append('\n');
        sb.Append("  changes:\n");
        AppendList(sb, "    files_modified", myWork.Changes.FilesModified);
        AppendList(sb, "    files_created", myWork.Changes.FilesCreated);
        sb.Append("    summary: ").Append(Scalar(myWork.Changes.Summary)).Append('\n');
        sb.Append("  self_iterations: ").Append(myWork.SelfIterations).Append('\n');
        sb.Append("  evidence:\n");
        AppendList(sb, "    commands", myWork.Evidence.Commands);
        AppendList(sb, "    artifacts", myWork.Evidence.Artifacts);
        sb.Append("  verification: ").Append(Scalar(myWork.Verification)).Append('\n');
        AppendList(sb, "  open_risks", myWork.OpenRisks);
        sb.Append("  confidence: ").Append(Scalar(myWork.Confidence)).Append('\n');

        return sb.ToString();
    }

    private static void AppendList(StringBuilder sb, string key, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            sb.Append(key).Append(": []\n");
            return;
        }

        sb.Append(key).Append(":\n");
        foreach (var value in values)
        {
            sb.Append("  ", 0, Math.Max(0, 0));
            var indent = key.StartsWith("    ", StringComparison.Ordinal) ? "      " :
                key.StartsWith("  ", StringComparison.Ordinal) ? "    " : "  ";
            sb.Append(indent).Append("- ").Append(Scalar(value)).Append('\n');
        }
    }

    private static string Scalar(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (NeedsQuoting(value))
        {
            var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
            return "\"" + escaped + "\"";
        }
        return value;
    }

    private static bool NeedsQuoting(string v)
    {
        if (v.Length == 0) return true;
        foreach (var c in v)
        {
            if (c is ':' or '#' or '\n' or '"' or '\'' or '[' or ']' or '{' or '}' or ',' or '&' or '*' or '|' or '>' or '!' or '%' or '@' or '`')
                return true;
        }
        if (char.IsWhiteSpace(v[0]) || char.IsWhiteSpace(v[^1])) return true;
        return v is "true" or "false" or "null" or "~" or "yes" or "no" or "on" or "off";
    }
}
