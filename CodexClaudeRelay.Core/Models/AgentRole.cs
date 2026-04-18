namespace CodexClaudeRelay.Core.Models;

/// <summary>
/// DAD-v2 peer-symmetric agent role identifier.
///
/// Codex and Claude are equal peers in the Dual-Agent Dialogue protocol —
/// neither is an approver, neither is an auditor. This class replaces the
/// legacy <c>RelaySide</c> enum, which encoded a binary Codex/Claude axis
/// and became a drift anchor for Codex-only broker / Claude-audit-only
/// framings. See <c>project_dad_v2_mission.md</c>.
///
/// Roles are strings (not an enum) so additional peers can join without a
/// schema migration, and so external packets / logs round-trip as-is.
/// </summary>
public static class AgentRole
{
    public const string Codex = "codex";
    public const string Claude = "claude-code";

    public static bool IsValid(string? role) =>
        role is Codex or Claude;

    public static string Peer(string role) => role switch
    {
        Codex => Claude,
        Claude => Codex,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "unknown agent role"),
    };

    public static bool Equals(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(a, b, StringComparison.Ordinal);
}
