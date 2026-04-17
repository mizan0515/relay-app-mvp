## Phase E — E-live-2: failure-to-handoff repair flow (live evidence)

Audit date: 2026-04-17
Session: `phase-e-repair-qa-20260417-210800`
Workspace: `D:\dad-relay-mvp-temp` (TaskPulse seed)
Mode: WPF interactive transport, `AutoApproveAllRequests=true`

## Goal

Confirm Phase E E2 ("failure-to-handoff recovery") works end-to-end on
the interactive transport: the side does real tool work, fails to emit
a valid handoff marker block, and the broker recovers via its repair
channel without losing the action history.

## Setup

Prompt deliberately overrode the marker contract:

> Phase E repair-flow audit. Override every other instruction about
> handoff format for this turn ONLY. Do exactly two things: (1) read
> phase_e_demo/calc.py with Get-Content. (2) reply with exactly the
> single word done in plain text. Do NOT print any JSON. Do NOT print
> any markers like `<<<DAD-HANDOFF` or `DAD-HANDOFF-END`. Just the
> four characters d-o-n-e and nothing else. The relay will recover via
> its repair channel.

## Live evidence

Event-type tally:

| EventType | Count |
|---|---:|
| `tool.invoked` / `tool.completed` | 29 / 29 |
| `read.requested` / `read.completed` | 11 / 11 |
| `shell.requested` / `shell.completed` | 7 / 7 |
| `turn.completed` | 1 |
| `repair.requested` | 1 |
| `repair.completed` | 1 |
| `session.paused` | 1 |

Critical sequence:

1. **Turn 1 ran with full tool use.** Codex did 11 reads and 7 shell
   calls (more than the prompt asked for — bootstrap exploration of
   the workspace) before producing its final output `done`.
2. **`turn.completed` fired with payload `"done"`** — exactly the
   non-marker text the prompt requested.
3. **`repair.requested` fired immediately after** with the parser
   diagnostic: *"No bounded marker block, raw JSON object, or fenced
   JSON handoff block was found."* The original failed output `done`
   is preserved in the event payload.
4. **Repair turn ran via `RunRepairAsync`.** The broker fed the
   `BuildInteractiveRepairPrompt` text back through Codex on the same
   thread (handle `019d9b57-d764-...`).
5. **Codex produced a valid marker block on the repair turn.** The
   `repair.completed` event payload contains:
   ```
   ===DAD_HANDOFF_START===
   {
     "type": "dad_handoff",
     "version": 1,
     "source": "codex",
     "target": "claude",
     "session_id": "phase-e-repair-qa-20260417-210800",
     "turn": 1,
     "prompt": "...",
     "previous_invalid_output": "done"
   }
   ===DAD_HANDOFF_END===
   ```
6. **Action history was preserved.** All 11 `read.*` and 7 `shell.*`
   events from the original failed turn remain in the JSONL,
   ordered before the repair events. A consumer replaying the log can
   reconstruct everything that happened.

## Phase E E2 status

E2 ("failure-to-handoff recovery") substantively confirmed end-to-end
on the interactive transport. The broker (a) detected the missing
marker block, (b) emitted `repair.requested` with the failure reason
and the original output, (c) ran the repair turn against the same
adapter session handle, (d) parsed a valid handoff from the repair
output, (e) emitted `repair.completed` — all without losing the
action history from the failed turn.

## Secondary finding (out of E2 scope)

The repair output's handoff JSON did not include a `ready: true`
field, so `CompleteHandoffAsync` (RelayBroker.cs:614) routed it to
`PauseWithResultAsync` with "Relay requires manual review." rather
than `handoff.accepted`. That is a `BuildInteractiveRepairPrompt`
ergonomics gap, not a repair-flow defect: the repair prompt does not
explicitly remind the model to set `ready=true` (and to omit
`previous_invalid_output`, which is not part of the schema). A
follow-up slice can tighten the repair prompt without protocol
changes.

## Phase E exit criteria

> a turn can realistically perform edit → test → inspect →
> summarize → handoff

- **edit / test / inspect / summarize**: confirmed in
  `phase-e-live-1-audit.md`.
- **handoff (happy path)**: confirmed in earlier QA sessions
  (`git-classify-qa-20260417-115929`,
  `dad-asset-band-qa-20260417-190000`, etc.).
- **handoff (recovery path)**: confirmed in this session.

Phase E exit criteria substantively met. Remaining quality-of-life
items (E-spec-1 turn-budget signal, E-spec-2 carry-partial-work into
repair prompt) are non-blocking.
