using System.Text;

namespace CodexClaudeRelay.Core.Protocol;

/// <summary>
/// Renders the rotation carry-forward markdown block from raw session fields.
/// Extracted from RelayBroker.TryBuildCarryForwardBlock so the production
/// serializer is reachable from headless harnesses (RotationSmokeRunner)
/// without a live broker + RelaySessionState. Format is authoritative —
/// downstream consumers (next-turn prompt prepend) rely on the exact
/// `## Carry-forward` / `- prior_handoff_hash:` / `- goal:` / `### Completed|Pending|Constraints`
/// shape.
/// </summary>
public static class CarryForwardRenderer
{
    public static string? TryBuild(
        bool carryForwardPending,
        string? lastHandoffHash,
        string? goal,
        IReadOnlyList<string> completed,
        IReadOnlyList<string> pending,
        IReadOnlyList<string> constraints)
    {
        if (!carryForwardPending)
        {
            return null;
        }

        var hasContent =
            !string.IsNullOrWhiteSpace(goal)
            || completed.Count > 0
            || pending.Count > 0
            || constraints.Count > 0
            || !string.IsNullOrWhiteSpace(lastHandoffHash);
        if (!hasContent)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append("## Carry-forward").Append(Environment.NewLine);
        if (!string.IsNullOrWhiteSpace(lastHandoffHash))
        {
            sb.Append("- prior_handoff_hash: ").Append(lastHandoffHash).Append(Environment.NewLine);
        }
        if (!string.IsNullOrWhiteSpace(goal))
        {
            sb.Append("- goal: ").Append(goal).Append(Environment.NewLine);
        }
        AppendSection(sb, "Completed", completed);
        AppendSection(sb, "Pending", pending);
        AppendSection(sb, "Constraints", constraints);
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }
        sb.Append("### ").Append(heading).Append(Environment.NewLine);
        foreach (var item in items)
        {
            sb.Append("- ").Append(item).Append(Environment.NewLine);
        }
    }
}
