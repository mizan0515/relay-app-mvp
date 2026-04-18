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

        var repairCtx = new RelayRepairContext(
            SessionId: "smoke-session",
            TurnNumber: 2,
            SourceSide: RelaySide.Codex,
            OriginalPrompt: "Smoke task — no real work.",
            OriginalOutput: "...some malformed non-handoff trailing text...",
            RepairPrompt: "(unused — BuildInteractiveRepairPrompt renders its own).");

        var repair = RelayPromptBuilder.BuildInteractiveRepairPrompt(repairCtx);

        // Drive the production carry-forward serializer directly (not the
        // pre-rendered passthrough above) so G7 semantic evidence covers
        // the real State -> markdown path, not just marker ordering.
        var produced = CarryForwardRenderer.TryBuild(
            carryForwardPending: true,
            lastHandoffHash: "deadbeef0000000000000000",
            goal: "Smoke-validate production carry-forward serializer.",
            completed: new[] { "Extracted CarryForwardRenderer from RelayBroker." },
            pending: new[] { "Assert production `- goal:` / `### Completed` shape." },
            constraints: new[] { "Headless only — no broker State." });

        // G6 — drive the production rolling-summary writer headlessly
        // and assert on-disk bytes, path shape, and event payload.
        var summaryFields = new RollingSummaryFields(
            SessionId: "smoke-session",
            SegmentNumber: 99,
            RotationReason: "rotation-smoke headless exercise",
            SessionStartedAt: DateTimeOffset.Now.AddMinutes(-5),
            TurnsSinceLastRotation: 42,
            ActiveSideAtRotation: RelaySide.Codex,
            TotalInputTokens: 1234,
            TotalOutputTokens: 567,
            TotalCacheReadInputTokens: 89,
            TotalCacheCreationInputTokens: 10,
            TotalCostClaudeUsd: 0.0123,
            TotalCostCodexUsd: 0.0456,
            LastHandoff: null,
            PendingPrompt: null);

        var summaryBaseDir = RollingSummaryWriter.ResolveBaseDirectory();
        var summaryExpectedPath = RollingSummaryWriter.ResolvePath(
            summaryBaseDir, summaryFields.SessionId, summaryFields.SegmentNumber);
        try { if (File.Exists(summaryExpectedPath)) File.Delete(summaryExpectedPath); }
        catch { /* best-effort cleanup; assertions below will catch a stale file */ }

        // App.OnStartup runs on the WPF UI thread; .GetAwaiter().GetResult()
        // directly on the async WriteAsync would deadlock the dispatcher while
        // the continuation waits to resume on the same captured context.
        // Task.Run hops to the thread pool so the async IO can complete.
        var summaryResult = Task.Run(
            () => RollingSummaryWriter.WriteAsync(summaryFields, CancellationToken.None))
            .GetAwaiter().GetResult();
        var summaryPayload = RollingSummaryWriter.BuildGeneratedEventPayload(
            summaryResult.Path, summaryResult.Bytes,
            summaryFields.SegmentNumber, summaryFields.SessionId,
            summaryFields.TurnsSinceLastRotation,
            summaryFields.TotalCostClaudeUsd, summaryFields.TotalCostCodexUsd);
        var summaryOnDiskBytes = File.Exists(summaryResult.Path)
            ? new FileInfo(summaryResult.Path).Length
            : -1;

        var checks = new (string Name, bool Pass)[]
        {
            // G7 — carry-forward rendering
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
            // G5 — E-spec-2 repair prompt contract
            ("repair prompt has start marker", repair.Contains(RelayPromptBuilder.HandoffStartMarker)),
            ("repair prompt has end marker", repair.Contains(RelayPromptBuilder.HandoffEndMarker)),
            ("repair prompt requires ready=true", repair.Contains("ready=true")),
            ("repair prompt has Do-NOT rules", repair.Contains("Do NOT")),
            ("repair prompt forbids echoing previous output",
                repair.Contains("copy the previous invalid output")),
            // G7 — production CarryForwardRenderer output shape
            ("production block rendered", produced is not null),
            ("production block has heading", produced?.Contains("## Carry-forward") == true),
            ("production block has prior_handoff_hash line",
                produced?.Contains("- prior_handoff_hash: deadbeef0000000000000000") == true),
            ("production block has - goal: line", produced?.Contains("- goal:") == true),
            ("production block has ### Completed heading",
                produced?.Contains("### Completed") == true),
            ("production block has ### Pending heading",
                produced?.Contains("### Pending") == true),
            ("production block has ### Constraints heading",
                produced?.Contains("### Constraints") == true),
            // G6 — RollingSummaryWriter on-disk + payload shape
            ("summary file exists on disk", summaryOnDiskBytes >= 0),
            ("summary on-disk bytes match result (+ optional UTF-8 BOM)",
                summaryOnDiskBytes == summaryResult.Bytes
                || summaryOnDiskBytes == summaryResult.Bytes + 3),
            ("summary markdown has session header",
                summaryResult.Markdown.Contains($"# Session {summaryFields.SessionId}")),
            ("summary markdown has Cumulative totals heading",
                summaryResult.Markdown.Contains("## Cumulative totals")),
            ("summary markdown has rotation reason line",
                summaryResult.Markdown.Contains($"- Rotation reason: {summaryFields.RotationReason}")),
            ("summary markdown has no-handoff fallback",
                summaryResult.Markdown.Contains("- (no handoff captured this segment)")),
            ("summary.generated payload has path key",
                summaryPayload.Contains("\"path\":\"")),
            ("summary.generated payload has bytes field",
                summaryPayload.Contains($"\"bytes\":{summaryResult.Bytes}")),
            ("summary.generated payload has segment=99",
                summaryPayload.Contains("\"segment\":99")),
        };

        var summary = new StringBuilder();
        summary.AppendLine("rotation-smoke (G5 repair + G6 rolling summary + G7 carry-forward)");
        summary.AppendLine($"turn prompt bytes: {rendered.Length}   repair prompt bytes: {repair.Length}");
        summary.AppendLine($"summary path: {summaryResult.Path}   bytes: {summaryResult.Bytes}");
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
