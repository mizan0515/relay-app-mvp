using System.Text;
using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;

namespace CodexClaudeRelay.Core.Runtime;

/// <summary>
/// Headless smoke-exercise of F-impl-3 carry-forward prompt injection.
/// Seeds a synthetic 4-section carry-forward block, renders the next
/// turn prompt, and asserts the block + handoff marker both appear.
/// G7 evidence without needing WPF or a live adapter.
/// </summary>
public static class RotationSmokeRunner
{
    public sealed record Result(int ExitCode, string Summary);

    public static Result Run()
    {
        var carryForward = string.Join(Environment.NewLine, new[]
        {
            "## Carry-forward",
            "",
            "**Goal:** Smoke-validate rotation carry-forward rendering.",
            "",
            "**Completed:**",
            "- Seeded synthetic state via RotationSmokeRunner.",
            "",
            "**Pending:**",
            "- Assert carry-forward block lands above dad_handoff marker.",
            "",
            "**Constraints:**",
            "- Headless only — no WPF, no adapter, no network.",
            "",
            "**LastHandoffHash:** `deadbeef0000000000000000`",
        });

        var ctx = new RelayTurnContext(
            SessionId: "smoke-session",
            TurnNumber: 2,
            SourceSide: RelaySide.Codex,
            Prompt: "Smoke task — no real work.",
            CarryForward: carryForward);

        var rendered = RelayPromptBuilder.BuildTurnPrompt(ctx);

        var checks = new (string Name, bool Pass)[]
        {
            ("carry-forward heading present", rendered.Contains("## Carry-forward")),
            ("goal section present", rendered.Contains("**Goal:**")),
            ("completed section present", rendered.Contains("**Completed:**")),
            ("pending section present", rendered.Contains("**Pending:**")),
            ("constraints section present", rendered.Contains("**Constraints:**")),
            ("LastHandoffHash line present", rendered.Contains("**LastHandoffHash:**")),
            ("handoff start marker present", rendered.Contains(RelayPromptBuilder.HandoffStartMarker)),
            ("carry-forward precedes handoff marker",
                rendered.IndexOf("## Carry-forward", StringComparison.Ordinal) >= 0
                && rendered.IndexOf("## Carry-forward", StringComparison.Ordinal)
                   < rendered.IndexOf(RelayPromptBuilder.HandoffStartMarker, StringComparison.Ordinal)),
        };

        var summary = new StringBuilder();
        summary.AppendLine("rotation-smoke (F-impl-3 carry-forward prompt injection)");
        summary.AppendLine($"rendered bytes: {rendered.Length}");
        var failed = 0;
        foreach (var (name, pass) in checks)
        {
            summary.AppendLine($"  [{(pass ? "OK" : "FAIL")}] {name}");
            if (!pass) failed++;
        }
        summary.AppendLine($"result: {(failed == 0 ? "PASS" : $"FAIL ({failed}/{checks.Length})")}");

        return new Result(failed == 0 ? 0 : 1, summary.ToString());
    }
}
