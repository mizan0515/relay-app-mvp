## Phase E — E-live-1: edit → test → handoff (live evidence)

Audit date: 2026-04-17
Session: `phase-e-edit-test-qa-20260417-200500`
Workspace: `D:\dad-relay-mvp-temp` (TaskPulse seed)
Mode: WPF interactive transport, `AutoApproveAllRequests=true`

## Goal

Close the unproven leg of the Phase E exit criteria — confirm a single
turn can realistically perform **edit → test → inspect → summarize** on
the interactive transport, with the test runner exercised against a real
process inside Codex's sandbox.

## Setup

A minimal stdlib-only Python target was planted under
`D:\dad-relay-mvp-temp\phase_e_demo\`:

- `calc.py` — `add(a,b)` and `subtract(a,b)` where `subtract` had an
  intentional bug (`return a + b` instead of `a - b`).
- `test_calc.py` — `unittest.TestCase` with two cases; baseline run
  `python -m unittest phase_e_demo.test_calc` produced
  `AssertionError: 8 != 2` on `test_subtract`.
- `__init__.py` — empty.

Prompt asked Codex to (1) read both files, (2) run the unittest module
and observe the failure, (3) fix the bug, (4) re-run and confirm green,
(5) hand off.

## Live evidence

Event-type tally for the session JSONL:

| EventType | Count |
|---|---:|
| `tool.invoked` / `tool.completed` | 55 / 55 |
| `read.requested` / `read.completed` | 19 / 19 |
| `shell.requested` / `shell.completed` | 17 / 17 |
| `git.requested` / `git.completed` | 1 / 1 |
| `file.change.requested` / `file.change.completed` | 1 / 1 |
| `turn.completed` | 1 |
| `session.paused` | 1 |

Key observations from the log:

- Multi-step single turn: the sequence was reads → unittest run → more
  reads → `fileChange` (diff `-    return a + b` → `+    return a - b`)
  → re-read calc.py → re-run unittest → reasoning → final reply. All
  inside Turn 1 of one session.
- `fileChange` correctly emitted as `file.change.requested`/`.completed`
  with the unified-diff payload preserved for after-the-fact replay.
- The unittest invocations were classified as `shell` (Codex wraps them
  as `powershell.exe -Command "& 'python.exe' -m unittest …"`), as
  expected from the iter-8 read-classifier scope (compound/scripted
  PowerShell stays in `shell`).
- One stray `git status` attempt failed with `fatal: detected dubious
  ownership` because Codex's sandbox runs under a different SID than the
  workspace owner. That was correctly classified as `git.requested` /
  `.completed` (status `failed`, exit 1) — Windows-specific, not a
  classifier bug, and Codex did not retry.

## Handoff leg outcome

Codex ended Turn 1 with `session.paused` rather than `handoff.accepted`:

> "Promoting the requested phase-e session as the active relay would
> require closing or superseding the unrelated active session
> destructive-qa-20260417-125954 and updating its backlog linkage /
> closeout artifacts. I did not mutate that unrelated DAD state without
> explicit direction."

This is the DAD-state-aware operator path, not a transport failure: the
TaskPulse workspace's `Document/dialogue/state.json` still has the
prior `destructive-qa-20260417-125954` session marked active, so Codex
correctly refused to silently clobber it. The relay broker accepted
the pause cleanly (`turn.completed` + `session.paused`).

## Phase E E1 status

E1 ("tool-rich interactive contract") is now substantively confirmed
end-to-end on the interactive transport for the **edit → test →
inspect → summarize** chain. The remaining gap on the **handoff
boundary** leg is not an E1 product gap — it is the workspace's prior
active-session state. A follow-up exercise against a freshly-bootstrapped
DAD workspace would close it.

## Recommended next slices

1. **E-live-2** (next): deliberate repair-flow trigger on the
   interactive transport. Prompt the model to do real tool calls but
   omit the marker block, verify `repair.requested` / `.completed`
   land, and confirm action history from the failed turn is preserved.
2. Re-run E-live-1 against a freshly-bootstrapped DAD workspace (no
   pre-existing active session) so the handoff-boundary leg also
   produces `handoff.accepted` — purely a workspace setup item, not a
   product gap.

## Files

- `phase_e_demo/calc.py`, `test_calc.py`, `__init__.py` — left in
  TaskPulse workspace as untracked fixtures for re-use.
- Session log: `%LocalAppData%\RelayAppMvp\logs\phase-e-edit-test-qa-20260417-200500.jsonl`.
