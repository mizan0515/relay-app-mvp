## Phase E — Deep Tool-Chain Runtime: Opening Survey

Audit date: 2026-04-17

Maps the Phase E deliverables from `IMPROVEMENT-PLAN.md` (E1 tool-rich
interactive contract, E2 failure-to-handoff recovery) to behavior already
present in the prototype, so the next concrete slice can target real gaps
rather than re-implementing what is already covered.

## E1. Tool-rich interactive contract

**Spec**: allow exploration, edits, tests, shell, MCP tool use within a single
turn; require a final handoff boundary at the end.

**Covered**:
- Bounded prompt was relaxed to marker-based handoff extraction (A7) — the
  contract no longer biases the model toward returning one JSON handoff
  immediately. `RelayPromptBuilder` now instructs both sides to keep reasoning
  / tool use **above** the start marker and emit a single `dad_handoff` JSON
  object only between the markers.
- Interactive transports already surface multi-step work in one turn:
  - `git-classify-qa-20260417-115929` — Codex Turn 1 emitted 5
    `git.requested`/`.completed` + 4 `shell.requested`/`.completed` events
    before handoff.
  - `destructive-qa-20260417-131500` — Codex Turn 1 chained `git add` →
    `git commit` → `git push` (with broker approval round-trip) inside a
    single turn.
  - `dad-asset-band-qa-20260417-190000` — three `fileChange` operations in one
    turn (2× `dad.asset.*`, 1× `file.change.*`) followed by handoff.
- Categories implemented in `RelayApprovalPolicy` cover the full tool-rich
  surface: `shell`, `read`, `file-change`, `dad-asset`, `git*`, `pr`, `mcp`,
  `web`, `permissions`, `tool`, `command`.
- MCP tool use is observed and policy-classified (Phase G2 bridge already
  landed); MCP read-only resource discovery and telemetry ping/status
  auto-clear, other MCP activity pauses for review.

**Gaps**:
- The marker contract is documented in the prompt but is not validated as a
  product invariant by any automated test — only the live QA sessions
  exercise it.
- No explicit "tool budget" per turn (max edits / shell calls / MCP calls
  before forced summarize). Today the only ceiling is the turn timeout +
  cache-budget rotation.
- No explicit signalling that the model is **mid-work** vs **ready to hand
  off**. The broker waits for the marker-block; it cannot tell if a long
  silent stretch is productive work or a stuck adapter (turn timeout is the
  only safety net).

## E2. Failure-to-handoff recovery

**Spec**: if the side does real work but fails to emit a valid handoff,
preserve action history, run a repair prompt, avoid losing the fact that
work already occurred.

**Covered**:
- `RelayBroker` already emits a `repair.requested` event with the failure
  reason and the last invalid output (`RelayBroker.cs:499`), then calls the
  source adapter's `RunRepairAsync` with a `RelayRepairContext` carrying the
  session handle and a builder-generated repair prompt
  (`RelayBroker.cs:509-518`).
- `RelayPromptBuilder.BuildInteractiveRepairPrompt` (line ~150) tells the
  side: "your previous interactive reply did not end with a valid relay
  handoff" and re-states the marker contract, preserving the work that
  already occurred above the markers.
- Repair attempts are counted (`State.RepairAttempts`), observed actions
  from the repair turn are logged through `LogObservedActionsAsync`, and a
  successful repair emits `repair.completed`
  (`RelayBroker.cs:583`).
- Cache-budget regressions during repair fall back through
  `TryDowngradeRepairAsync` → bounded fallback adapter
  (`RelayBroker.cs:1421-1437`) so a repair on the interactive transport can
  degrade to the bounded one rather than fail the turn outright.
- Action history (the JSONL event log) is durable per session and survives
  a repair turn — every `*.requested`/`.completed` event from the original
  failed turn is preserved.

**Gaps**:
- Repair-attempt cap and downgrade thresholds are not currently surfaced in
  the operator UI — the user cannot see "we are on repair attempt 2/3"
  without tailing the JSONL.
- No structured "carry the partial work forward" path: the repair prompt
  re-asks for the marker block but does not echo back a summary of what
  the side already did. For long tool chains this risks the side either
  re-doing work or losing track of side-effects (e.g. files already
  written).
- No live evidence yet — repair flow is exercised by unit / integration
  paths but no QA session in this audit cycle has deliberately triggered
  it on the interactive transport.

## Exit criteria assessment

Phase E exit criteria from `IMPROVEMENT-PLAN.md`:

> a turn can realistically perform edit → test → inspect → summarize → handoff

**Status**: substantially met for **edit → inspect → handoff** and
**shell-chain → handoff** (live evidence above). The unproven leg is
**test** — no QA session in this cycle has exercised a real test runner
inside one turn (e.g. `pytest`, `dotnet test`) followed by inspection of
results and a summarizing handoff.

## Recommended Phase E slices (in order)

1. **E-live-1: edit → test → handoff exercise.** Drive a single-turn QA
   session against the TaskPulse workspace where Codex (a) edits a Python
   source file, (b) runs the relevant `pytest` subset, (c) inspects the
   output, (d) summarizes in the handoff. Confirms the tool-rich contract
   on the test leg.
2. **E-live-2: deliberate repair trigger.** Send a prompt that produces
   real tool calls but instructs the model to omit the marker block.
   Verify the broker emits `repair.requested`, the repair turn lands a
   valid handoff, and the original observed actions remain in the log.
3. **E-spec-1: turn-budget signal.** Add a structured "turn progress"
   surface (e.g. observed-action count + elapsed time + last activity
   timestamp) so the operator can distinguish productive long turns from
   stuck adapters before the turn timeout fires. Docs + UI panel only —
   no protocol change.
4. **E-spec-2: carry-partial-work into repair prompt.** Extend
   `BuildInteractiveRepairPrompt` to include a one-line summary per
   already-observed action so the repair prompt is "you already did X, Y,
   Z — now please emit only the marker block" rather than a blind
   re-ask. Reduces the risk of duplicate side-effects.

E1 can be declared substantively done once E-live-1 lands; E2 once
E-live-2 lands. E-spec-1 and E-spec-2 are quality-of-life follow-ups that
are not strictly required to clear the exit criteria but are the natural
next slices after the live evidence is in.
