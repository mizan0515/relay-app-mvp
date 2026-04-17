## Phase F — F-live-1: rotation-with-summary live exercise (plan)

Plan date: 2026-04-18
Target session id: `phase-f-live-1-<YYYYMMDD-HHMMSS>`
Workspace: same TaskPulse seed used by E-live-1 (`D:\dad-relay-mvp-temp`)
Mode: WPF interactive transport, `AutoApproveAllRequests=true`
Gate: G8 (rotation survives with carry-forward)
Predecessors: F-impl-1 ([PR #5](https://github.com/mizan0515/codex-claude-relay/pull/5)),
F-impl-2 ([PR #6](https://github.com/mizan0515/codex-claude-relay/pull/6)),
F-impl-3 ([PR #7](https://github.com/mizan0515/codex-claude-relay/pull/7)),
F-impl-3b ([PR #8](https://github.com/mizan0515/codex-claude-relay/pull/8)),
F-impl-3c part 1 ([PR #9](https://github.com/mizan0515/codex-claude-relay/pull/9)),
F-impl-3c part 2 ([PR #10](https://github.com/mizan0515/codex-claude-relay/pull/10)).

## Goal

Close the F-live-1 exit leg of Phase F — confirm that crossing
`MaxTurnsPerSession` (or any rotation trigger) fires the full pipeline
end-to-end on a real live session:

```
rotation.triggered
  → summary.generated       (durable write, F-impl-1)
  → State.Goal/Completed/Pending/Constraints populated from handoff
                            (F-impl-3b + F-impl-3c)
  → next-turn prompt prepended with "## Carry-forward" block
                            (F-impl-3 renderer)
  → summary.loaded          (bytes + fields_populated payload)
```

Success = the post-rotation turn resumes coherently against the pre-rotation
context, not "fresh-start memory loss" (phase-f-survey.md §"session rotation
no longer feels like memory loss").

## Setup

1. Fresh DAD workspace. Clean or reset `%LocalAppData%\CodexClaudeRelayMvp\`
   `state.json` / `summaries\` / session JSONL from the prior live runs —
   the E-live-1/E-live-2 audit sessions left lingering state that will
   confuse the rotation trigger. `tools/reset-live-state.ps1` if it exists,
   otherwise delete the three subtrees by hand.
2. Configure `RelayBrokerOptions.MaxTurnsPerSession` to a small value
   (**3**) so rotation fires inside a short scripted turn budget without
   needing a long natural conversation. Revert after the run.
3. Confirm F-impl-3c part 2 (PR #10) is on the local `main` HEAD:
   `git log --oneline | head -1` should show the squash commit for PR #10.
4. Plant a multi-step target under `D:\dad-relay-mvp-temp\phase_f_demo\`
   structured so a coherent "continue" makes sense post-rotation:
   - `spec.md` — 3-step algorithmic task (e.g. sliding-window median over
     integer stream: step 1 parse input, step 2 compute, step 3 summarize).
   - `input.txt` — the data to process.
   - `expected.txt` — the expected summary output to diff against.

## Drive sequence (UI Automation)

Turn 1 — Goal set, step 1.
  Prompt: "Read `phase_f_demo/spec.md` and `phase_f_demo/input.txt`,
  implement step 1 (parse the input stream into a list), and hand off
  back to `codex` with the list in `completed` and steps 2+3 in
  `pending`. Set `reason` to the overall 3-step goal."

Turn 2 — step 2 (rotation expected after this turn under
  `MaxTurnsPerSession=3`).
  Prompt: re-issued from Codex's acknowledgment of the handoff. The
  interactive transport will decide when rotation fires based on
  `EvaluateRotationReason` (L1346 — `>= MaxTurnsPerSession`).

Turn 3 — the rotation-split turn. The broker SHOULD emit
  `rotation.triggered` → `summary.generated` right after the handoff is
  accepted, and the NEXT prompt going out MUST carry the `##
  Carry-forward` block. Verify:
  - `grep "rotation.triggered\|summary.generated\|summary.loaded" <session-jsonl>`
  - inspect `%LocalAppData%\CodexClaudeRelayMvp\summaries\<session>-segment-1.md`
    exists and is non-empty.
  - inspect the outgoing prompt bytes on the transport side (the
    `RelayPromptBuilder` prepend path) for the literal `## Carry-forward`
    heading with `Goal:`, `Completed:`, `Pending:`, `Constraints:`
    subheadings.

Turn 4 — step 3 on the post-rotation session. Did Claude/Codex resume
  against the spec coherently, or did it ask "what were we doing?"
  The former is G8 pass; the latter is regression.

## Verification matrix

| Leg | Event / file | Expected | Pass criterion |
|---|---|---|---|
| F-impl-1 | `summary.generated` | emitted once per rotation | count=1 |
| F-impl-1 | `summaries\{session}-segment-1.md` | file exists, >0 bytes | stat |
| F-impl-2 | `State.LastHandoffHash` | non-null post-rotation | inspect STATE |
| F-impl-2 | `State.Goal/Completed/Pending/Constraints` | non-empty where envelope carried them | inspect STATE |
| F-impl-3 | outgoing prompt | contains `## Carry-forward` | byte-inspect |
| F-impl-3 | `summary.loaded` | emitted once per post-rotation turn | count=1 |
| F-impl-3c | `completed`/`constraints` arrays | round-trip from handoff into rendered block | prompt contains items |
| G8 | post-rotation semantic continuity | step 3 executes against step 1+2 artifacts | expected.txt diff |

## Risks / known pitfalls

- Rotation-fire timing is **after** turn.completed of the Nth turn, so the
  "carry-forward prompt" assertion must be made on turn **N+1**, not N.
  Watch for off-by-one when reading the JSONL.
- `AutoApproveAllRequests=true` bypasses the approval ring; any
  destructive request in `phase_f_demo` would auto-land. Keep the target
  read-only except for one `file.change` per turn.
- If `MaxTurnsPerSession` is lowered globally it affects any other live
  runs happening in parallel — revert immediately after capture.
- `summary.generated` requires IO to `%LocalAppData%` — if the dir is
  locked (antivirus quarantine, OneDrive sync) the write fails and
  `summary.failed` fires instead. Freshly reset state before the run.

## Exit criteria

- All rows of the verification matrix pass.
- Audit doc `phase-f-live-1-audit.md` authored in the pattern of
  `phase-e-live-1-audit.md`: setup, event tally, key observations,
  outstanding gaps.
- [.autopilot/MVP-GATES.md](.autopilot/MVP-GATES.md) G8 `[ ]`→`[x]` flip
  queued as an operator-review PR (MVP-GATES.md is `protected_paths`).
- [.autopilot/BACKLOG.md](.autopilot/BACKLOG.md) F-live-1 line removed.

## Out of scope

- Failure-mode rehearsal (rotation mid-approval, rotation during
  `handoff.invalid` repair). Queue as F-live-2 if G8 lands cleanly.
- Cross-session rotation (rotation from a resumed-from-disk state).
  Queue as F-live-3.
