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

- **Status proposal:** `[~]` (partial — prompt-text contract shipped,
  live run pending).
- **Evidence:** PR #12 tightened `BuildInteractiveRepairPrompt` with
  `ready=true` requirement + Do-NOT echo rules. Unit-level assertions
  against the prompt text are the next harness step (F-live-2). No
  live malformed-handoff trigger has been run yet.

## G6 — Rolling summary written durably (F-impl-1)

- **Status proposal:** `[x]` flip-ready by code-path inspection;
  live-exercise pending.
- **Evidence:** PR #5 (ae0f220) added `WriteRollingSummaryAsync` in
  `RelayBroker.cs:1664`. Emits `summary.generated` with
  `path`/`bytes`/`segment`/`session_id`/`turns`/cost payload; emits
  `summary.failed` on IOException/UnauthorizedAccess/Security/
  PathTooLong. Writes to
  `%LocalAppData%\CodexClaudeRelayMvp\summaries\{sessionId}-segment-{n}.md`
  **before** per-rotation state reset (called at top of private
  `RotateSessionAsync`).
- **Live status:** no rotation-triggering live run exercised
  autopilot-side yet. Rotation-smoke extension (BACKLOG P1
  `[rotation-smoke-extend]`) will give this a reproducible self-test.

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
    `--rotation-smoke` switch. Reproduction:
    `dotnet run --project CodexClaudeRelay.Desktop -c Release --
    --rotation-smoke` → exit 0, 8/8 assertions PASS, 2261 rendered
    bytes, log at `%LocalAppData%\CodexClaudeRelayMvp\rotation-smoke.log`.

## G8 — Rotation live exercise crossing MaxTurnsPerSession

- **Status proposal:** `[~]` (partial — semantic evidence via PR #15
  covers the prompt-rendering half; full jsonl-backed live run across
  the rotation threshold is still pending).
- **Evidence:** PR #15 proves carry-forward renders correctly on a
  synthetic post-rotation turn. `phase-f-live-1-plan.md` (PR #11)
  documents the remaining live-run plan. Operator decides whether
  semantic evidence satisfies G8 or whether UI Automation is required
  (open question).

---

## Summary for scorecard-flip PR

Proposed single-commit edit to `.autopilot/MVP-GATES.md`:

- G1 `[ ]` → `[x]` — build green
- G4 `[ ]` → `[x]` — destructive approval QA (predates scorecard)
- G5 `[ ]` → `[~]` — repair prompt tightened, live pending
- G6 `[ ]` → `[x]` — summary write shipped + code-path verified
- G7 `[ ]` → `[x]` — live reproducible via `--rotation-smoke`
- G8 `[ ]` → `[~]` — semantic half covered, live half pending

Gate tally: 0/8 → 4/8 flipped + 2/8 partial. G2/G3 remain `[ ]`
pending UI-driven operator run.
