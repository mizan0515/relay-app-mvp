# MVP-GATES — evidence cross-reference

Companion to `.autopilot/MVP-GATES.md` (the scorecard). This file maps
each gate to the currently-shipped evidence so the next scorecard-flip
PR is a one-pass review instead of a research task. Scorecard lives in
`.autopilot/protected_paths` — autopilot cannot self-edit it.

## G1 — Build is green end-to-end

- **Status proposal:** `[x]` flip-ready.
- **Evidence:** iter 17 Release build `CodexClaudeRelay.sln -c Release`
  — 0 errors, 0 warnings, all four projects built (Core,
  CodexProtocol, CodexProtocol.Spike, Desktop). Reproduction:
  `dotnet build CodexClaudeRelay.sln -c Release --nologo`.

## G2 — App launches + adapter smoke

- **Status proposal:** `[ ]` unchanged (live UI-driven; needs operator
  run on real desktop session, not autopilot-reachable).
- **Evidence:** none yet in autopilot scope.

## G3 — Session start → advance → clean shutdown

- **Status proposal:** `[ ]` unchanged (UI-driven, same constraint as G2).

## G4 — Approval-first gate on destructive op

- **Status proposal:** `[x]` flip-ready — DEV-PROGRESS lines 70-74
  document multiple live QA sessions (`destructive-qa-20260417-131500`,
  auto-approve-push variants) that surfaced the approval UI and
  enforced deny/approve-once. Predates the MVP-GATES scorecard so it
  was never flipped.
- **Evidence:** `DEV-PROGRESS.md` live-run notes 2026-04-17, commit
  history around iterations 4-5 of that session.

## G5 — Handoff parse + repair loop

- **Status proposal:** `[~]` (partial — prompt-text contract shipped +
  asserted on every build; live malformed-handoff trigger still pending).
- **Evidence:**
  - PR #12 tightened `BuildInteractiveRepairPrompt` with `ready=true`
    requirement + Do-NOT echo rules.
  - **PR #17 (2d0ca90)** — `RotationSmokeRunner` extended with 5 G5
    assertions on `BuildInteractiveRepairPrompt`: start/end markers,
    `ready=true` requirement, `Do NOT` rules, no-echo of previous
    invalid output. Reproduction: same `--rotation-smoke` switch as G7,
    assertion lines tagged `repair prompt ...`.
  - Remaining live half: drive a real malformed adapter output through
    the broker and assert `handoff.accepted` on first repair turn
    (F-live-2). Gated on WPF UI Automation harness.

## G6 — Rolling summary written durably (F-impl-1)

- **Status proposal:** `[x]` flip-ready — **LIVE evidence reproducible**.
- **Evidence:**
  - PR #5 (ae0f220) added `WriteRollingSummaryAsync` in `RelayBroker.cs`.
    Emits `summary.generated` with
    `path`/`bytes`/`segment`/`session_id`/`turns`/cost payload; emits
    `summary.failed` on IOException/UnauthorizedAccess/Security/
    PathTooLong. Writes to
    `%LocalAppData%\CodexClaudeRelayMvp\summaries\{sessionId}-segment-{n}.md`
    **before** per-rotation state reset.
  - **PR #19 (1ae6ba6)** — extracted `RollingSummaryWriter` +
    `RollingSummaryFields`/`RollingSummaryResult` records into
    `CodexClaudeRelay.Core.Runtime`. Broker retains event emission;
    writer owns IO + markdown + payload. `RotationSmokeRunner` now
    drives the writer headlessly every build and asserts: file
    exists on disk, on-disk bytes match result (+ optional UTF-8 BOM),
    markdown has session header + `## Cumulative totals` + rotation
    reason + no-handoff fallback, `summary.generated` payload has
    `path`/`bytes`/`segment` keys. Reproduction:
    `dotnet run --project CodexClaudeRelay.Desktop -c Release --
    --rotation-smoke` → exit 0, 29/29 PASS, summary 557B written at
    `smoke-session-segment-99.md`, log at
    `%LocalAppData%\CodexClaudeRelayMvp\rotation-smoke.log`.

## G7 — Carry-forward injected into next turn (F-impl-2 + F-impl-3)

- **Status proposal:** `[x]` flip-ready — **LIVE evidence reproducible**.
- **Evidence:**
  - PR #6 (4391fc5) — carry-forward fields on `RelaySessionState` +
    `LastHandoffHash` populated in `CompleteHandoffAsync`.
  - PR #7 (bd8f60f) — `TryBuildCarryForwardBlock` renders
    `## Carry-forward` markdown; `RelayPromptBuilder` prepends it.
  - PR #8 (99a077c) — `CompleteHandoffAsync` populates Goal/Pending
    from handoff envelope.
  - PR #9 — `HandoffEnvelope` gains `completed`/`constraints` arrays.
  - PR #10 — parser + broker wire all four sections.
  - **PR #15 (05e02fa)** — `RotationSmokeRunner` + Desktop
    `--rotation-smoke` switch.
  - **PR #18 (ab8a80d)** — extracted `CarryForwardRenderer` into
    `CodexClaudeRelay.Core.Protocol` so `RotationSmokeRunner` now
    drives the production `State → markdown` serializer directly and
    asserts `- prior_handoff_hash:` / `- goal:` /
    `### Completed|Pending|Constraints` shape (not just pass-through
    marker ordering). Reproduction:
    `dotnet run --project CodexClaudeRelay.Desktop -c Release --
    --rotation-smoke` → exit 0, **29/29 PASS** (as of PR #19), turn
    prompt 2261B rendered, log at
    `%LocalAppData%\CodexClaudeRelayMvp\rotation-smoke.log`.

## G8 — Rotation live exercise crossing MaxTurnsPerSession

- **Status proposal:** `[~]` (partial — semantic evidence via PRs
  #15/#18/#19 now covers both halves of the rotation artifact
  pipeline; full jsonl-backed live run across the rotation threshold
  is still pending).
- **Evidence:** PRs #15/#18 prove carry-forward renders correctly on
  a synthetic post-rotation turn; PR #19 proves the rolling-summary
  durable-write side of the same rotation actually produces an
  on-disk file with the expected markdown shape. `phase-f-live-1-plan.md`
  (PR #11) documents the remaining live-run plan. Operator decides
  whether the combined semantic+IO evidence satisfies G8 or whether
  UI Automation is still required (open question).

---

## Summary for scorecard-flip PR

Proposed single-commit edit to `.autopilot/MVP-GATES.md`:

- G1 `[ ]` → `[x]` — build green
- G4 `[ ]` → `[x]` — destructive approval QA (predates scorecard)
- G5 `[ ]` → `[~]` — repair prompt tightened (PR #12) + asserted every
  build (PR #17); live malformed-handoff trigger still pending
- G6 `[ ]` → `[x]` — summary write shipped (PR #5) + live reproducible
  on-disk evidence (PR #19)
- G7 `[ ]` → `[x]` — live reproducible via `--rotation-smoke`
  (PRs #15/#18)
- G8 `[ ]` → `[~]` — combined semantic + IO evidence covered
  (PRs #15/#18/#19); live jsonl rotation still pending

Gate tally: 0/8 → 4/8 flipped + 2/8 partial. G2/G3 remain `[ ]`
pending UI-driven operator run.

Single smoke reproduction covers G5/G6/G7 simultaneously:

    dotnet build CodexClaudeRelay.sln -c Release --nologo
    dotnet run --project CodexClaudeRelay.Desktop -c Release -- --rotation-smoke

Expected: exit 0, 29/29 PASS, `rotation-smoke.log` + `smoke-session-segment-99.md`
under `%LocalAppData%\CodexClaudeRelayMvp\`.
