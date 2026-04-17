## Phase F — Broker-owned Continuity: Opening Survey

Audit date: 2026-04-17

Maps the Phase F deliverables from `IMPROVEMENT-PLAN.md` (F1 rolling
summary, F2 carry-forward state, F3 prompt assembly, F4 summary events)
to existing prototype behavior. Phase F is largely greenfield in the
current code — the broker has rotation infrastructure but no
durable-summary or carry-forward mechanism.

## F1. Rolling summary

**Spec**: before planned rotation, summarize relevant state and write a
durable summary file (e.g. `%LocalAppData%\RelayAppMvp\summaries\{sessionId}-segment-{n}.md`).

**Covered**:
- Rotation infrastructure exists in `RelayBroker.cs`:
  `EvaluateRotationReason` (line 1283) checks `MaxTurnsPerSession` and
  `MaxSessionDuration`; `RotateSessionAsync` (line 1299) emits
  `rotation.triggered` and resets per-rotation state.
- Rotation is also force-triggered by cache-budget regressions (lines
  457, 463, 554, 560 — "Cache regression triggered planned rotation").

**Gaps**:
- No `summaries/` directory write anywhere in the codebase.
- No summarization step before `RotateSessionAsync` resets state — the
  rotation happens immediately, dropping all per-segment context except
  what `LastHandoff` happens to carry.
- No segment-numbering scheme (`segment-{n}`).

## F2. Carry-forward state

**Spec**: structured state fields `Goal`, `Completed`, `Pending`,
`Constraints`, `LastHandoffHash`, `UpdatedAt`.

**Covered**:
- `RelaySessionState.LastHandoff` (line 25 of `RelaySessionState.cs`)
  preserves the most recent accepted `HandoffEnvelope` across
  rotations (`RotateSessionAsync` does not clear it).
- `UpdatedAt` (line 75) is maintained.
- `RotationCount` and `TurnsSinceLastRotation` track segment lifecycle.
- A canonical handoff hash is already computed on every accept via
  `HandoffParser.ComputeCanonicalHash(handoff)` (RelayBroker.cs:623)
  and stored as part of `AcceptedRelayKeys` (the dedup set).

**Gaps**:
- No first-class `Goal`, `Completed`, `Pending`, `Constraints` fields
  on `RelaySessionState`. The handoff schema has a `summary` array
  (1-10 short strings, per `RelayPromptBuilder.cs:34`), so per-turn
  intent is captured inside `LastHandoff.Summary`, but it is not
  promoted into a structured carry-forward record.
- `LastHandoffHash` is not stored as a discrete field — it lives only
  inside the composite `AcceptedRelayKeys` strings.

## F3. Prompt assembly

**Spec**: inject latest accepted handoff + rolling summary +
carry-forward state + selected recent events into the next turn's
prompt.

**Covered**:
- After every accepted handoff, `RelayBroker.CompleteHandoffAsync`
  sets `State.PendingPrompt = handoff.Prompt` (line 632), so the next
  turn's prompt explicitly includes the previous side's intent.
- `RelayPromptBuilder` builds turn prompts from `PendingPrompt`,
  reasserts the marker contract, and (for repair) re-states the
  failure context.

**Gaps**:
- No injection of rolling summary (because no summary exists).
- No injection of structured carry-forward state.
- No injection of "selected recent events" — observed actions stay in
  the JSONL log only; the next turn's prompt never references them.

## F4. Summary events

**Spec**: `summary.generated`, `summary.loaded`, `summary.failed`,
`summary.bytes`, `summary.cost`.

**Covered**: none.

**Gaps**: no event of any of these types is emitted today. `grep -r
"summary.generated"` etc. returns zero hits in source code.

## Exit criteria assessment

> session rotation no longer feels like memory loss

**Status**: not met. Today's rotation drops everything except the
single most-recent `LastHandoff`. There is no per-segment summary, no
structured carry-forward record, and no replay of recent events into
the next turn. Long sessions or forced cache-regression rotations
will still feel like memory loss.

## Recommended Phase F slices (in order)

1. **F-impl-1: durable rolling-summary file.** In
   `RotateSessionAsync`, before resetting per-rotation state, build a
   short markdown summary (latest handoff + last N observed actions +
   cumulative usage) and write it to
   `%LocalAppData%\RelayAppMvp\summaries\{sessionId}-segment-{n}.md`.
   Emit `summary.generated` (with bytes + cost) and
   `summary.failed` on IO error. Smallest viable F1 + F4 slice.
2. **F-impl-2: carry-forward state on RelaySessionState.** Add
   `Goal`, `Completed`, `Pending`, `Constraints`, `LastHandoffHash`
   fields. Populate `LastHandoffHash` from
   `HandoffParser.ComputeCanonicalHash(handoff)` in
   `CompleteHandoffAsync`. Promote handoff.summary into
   `Completed`/`Pending` heuristically (or leave a TODO and let the
   next handoff explicitly fill them). Smallest viable F2 slice.
3. **F-impl-3: prompt assembly that consumes the summary.** Extend
   `RelayPromptBuilder` so the next turn's prompt (after a rotation)
   includes a "## Carry-forward" section with the rolling summary +
   carry-forward fields. Emit `summary.loaded` when the file is
   successfully injected. Smallest viable F3 + F4 completion slice.
4. **F-live-1: rotation-with-summary live exercise.** Drive a session
   that crosses `MaxTurnsPerSession` (or force a small per-session
   cap) so a real rotation fires; verify
   `rotation.triggered` → `summary.generated` → next turn's prompt
   contains the carry-forward section → `summary.loaded` is emitted.

F1+F4 land together in F-impl-1; F2 lands as F-impl-2; F3 closes in
F-impl-3; live evidence comes in F-live-1. Each slice is small enough
to fit in one iteration including build, QA, and PR.
