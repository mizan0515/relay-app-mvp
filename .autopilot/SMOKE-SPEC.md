# .autopilot/SMOKE-SPEC.md — rotation-smoke assertion catalog

Headless self-exercise invoked via
`CodexClaudeRelay.Desktop.exe --rotation-smoke`. Bypasses WPF bootstrap,
drives production code paths, writes a pass/fail log to
`%LocalAppData%\CodexClaudeRelayMvp\rotation-smoke.log`, and exits with
status 0 when all assertions pass.

This document is the **contract** the smoke defends. Any assertion
removed here without a replacement is a regression, even if the smoke
still exits 0 — the smoke's value is proportional to what it covers.

## Invocation

```powershell
dotnet build CodexClaudeRelay.sln -c Release
.\CodexClaudeRelay.Desktop\bin\Release\net10.0-windows\CodexClaudeRelay.Desktop.exe --rotation-smoke
Get-Content "$env:LOCALAPPDATA\CodexClaudeRelayMvp\rotation-smoke.log"
```

Exit 0 = all assertions PASS. Exit 1 = at least one FAIL.

## Assertions (34 total as of PR #22)

### G7 — carry-forward prompt injection (8 assertions)

Drives `RelayPromptBuilder.BuildTurnPrompt` with a pre-rendered
carry-forward block. Verifies marker placement and section shape.

1. `carry-forward heading present` — `## Carry-forward` in rendered prompt.
2. `goal section present` — `**Goal:**` line reproduced.
3. `completed section present` — `**Completed:**` line reproduced.
4. `pending section present` — `**Pending:**` line reproduced.
5. `constraints section present` — `**Constraints:**` line reproduced.
6. `LastHandoffHash line present` — `**LastHandoffHash:**` line.
7. `handoff start marker present` — `RelayPromptBuilder.HandoffStartMarker`
   appears after the carry-forward block.
8. `carry-forward precedes handoff marker` — ordering invariant.

### G5 — interactive-repair prompt contract (5 assertions)

Drives `RelayPromptBuilder.BuildInteractiveRepairPrompt` with a
synthetic malformed handoff. Verifies the tightened prompt survives.

9.  `repair prompt has start marker` — `HandoffStartMarker` present.
10. `repair prompt has end marker` — `HandoffEndMarker` present.
11. `repair prompt requires ready=true` — explicit readiness contract.
12. `repair prompt has Do-NOT rules` — operator-visible constraints.
13. `repair prompt forbids echoing previous output` — phrase
    `copy the previous invalid output` present.

### G7 — production CarryForwardRenderer output shape (7 assertions)

Drives `CarryForwardRenderer.TryBuild` (extracted from
`RelayBroker.TryBuildCarryForwardBlock` in PR #18) with synthetic
list inputs. Verifies the real `State → markdown` serializer, not
just the passthrough in assertions 1-8.

14. `production block rendered` — non-null result when inputs present.
15. `production block has heading` — `## Carry-forward`.
16. `production block has prior_handoff_hash line` —
    `- prior_handoff_hash: <hash>`.
17. `production block has - goal: line` — `- goal:` (note: lowercase
    list form, distinct from the bold `**Goal:**` passthrough form).
18. `production block has ### Completed heading` — `### Completed`.
19. `production block has ### Pending heading` — `### Pending`.
20. `production block has ### Constraints heading` — `### Constraints`.

### G6 — RollingSummaryWriter on-disk + payload shape (9 assertions)

Drives `RollingSummaryWriter.WriteAsync` (extracted from
`RelayBroker.WriteRollingSummaryAsync` in PR #19) headlessly via
`Task.Run(...).GetAwaiter().GetResult()` (WPF dispatcher deadlock
avoidance — see inline comment in `RotationSmokeRunner`).

21. `summary file exists on disk` — post-write `File.Exists` true.
22. `summary on-disk bytes match result (+ optional UTF-8 BOM)` — on
    disk length equals `result.Bytes` or `result.Bytes + 3`
    (`File.WriteAllTextAsync(..., Encoding.UTF8, ...)` prepends a
    3-byte BOM; the byte count in the result excludes it).
23. `summary markdown has session header` — `# Session <id>`.
24. `summary markdown has Cumulative totals heading` —
    `## Cumulative totals`.
25. `summary markdown has rotation reason line` — verbatim reason.
26. `summary markdown has no-handoff fallback` —
    `- (no handoff captured this segment)` when `LastHandoff == null`.
27. `summary.generated payload has path key` — JSON `"path":"..."`.
28. `summary.generated payload has bytes field` — JSON
    `"bytes":<N>` with N matching `result.Bytes`.
29. `summary.generated payload has segment=99` — JSON
    `"segment":99`.

### G8 — RotationEvaluator boundary semantics (5 assertions)

Drives `RotationEvaluator.Evaluate` (extracted from
`RelayBroker.EvaluateRotationReason` in PR #22) across the rotation
boundary. Fixes `rotMaxTurns=3`, `rotMaxDuration=15min`, freezes
`rotNow` so the assertion is deterministic.

30. `rotation below maxTurns yields no reason` —
    `Evaluate(2, 3, freshStart, 15m, now)` → null.
31. `rotation at maxTurns yields planned reason` —
    `Evaluate(3, 3, ...)` → `"Planned rotation triggered after 3 turns."`
32. `rotation above maxTurns yields planned reason` —
    `Evaluate(8, 3, ...)` still returns the planned reason (defensive
    against counter overflow / missed rotation).
33. `rotation turns-trigger precedes duration text` — the turns-based
    reason has no `:` (duration format is `mm\\:ss`).
34. `aged session triggers duration rotation even at 0 turns` —
    `Evaluate(0, 3, agedStart=now-16m, 15m, now)` returns a non-null
    reason that does NOT end with ` turns.` (duration branch wins).

## Gate coverage summary

| Gate | Assertions | Producing PR(s) |
|---|---|---|
| G5 — handoff repair prompt | 9-13 | #12 (repair prompt tightening), #17 (smoke) |
| G6 — rolling summary write | 21-29 | #5 (writer), #19 (extract + smoke) |
| G7 — carry-forward injection | 1-8, 14-20 | #8/#9/#10 (impl), #15 (smoke), #18 (extract + smoke) |
| G8 — rotation boundary | 30-34 | #22 (extract + smoke) |

G5 and G8 additionally require a live-run (full WPF rotation across
`MaxTurnsPerSession`) to flip to `[x]` — both blocked on a UI
Automation harness. The smoke covers the semantic half; the live
half requires operator assist or the harness tracked under
`[F-live-1]`/`[F-live-2]` in BACKLOG.

## Extension rules

1. **Never shrink the catalog silently.** Removing an assertion must
   be paired with either (a) a replacement of equal or greater
   coverage, or (b) a METRICS note with `smoke_coverage_reduced:
   <reason>` and an operator FINDING.
2. **Keep assertions deterministic.** No wall-clock in comparisons;
   freeze `DateTimeOffset` inputs as iter 27 did with `rotNow`.
3. **Prefer driving production helpers directly.** The G7 pair
   (1-8 passthrough, 14-20 production) is the canonical shape — test
   both the rendered-prompt layer AND the underlying serializer
   whenever they diverge.
4. **One fail = fail.** The smoke reports `FAIL (N/M)` on any red;
   iter 22's UTF-8 BOM miss is the reference incident for assertion
   precision.
