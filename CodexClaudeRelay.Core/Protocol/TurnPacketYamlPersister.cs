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

        return sb.ToString();
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
