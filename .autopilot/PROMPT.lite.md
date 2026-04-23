# AUTOPILOT LITE — codex-claude-relay low-context maintenance prompt

Use ONLY for short maintenance loops where the full `.autopilot/PROMPT.md`
boot (IMMUTABLE:mission, mission-clarification, core-contract, boot, budget,
blast-radius, halt, mvp-gate, exit-contract) is not justified.

This prompt inherits the DAD-v2 peer-symmetry mission from the full prompt
by reference — it does not restate it. If your task could even indirectly
weaken peer symmetry, stop and re-launch with the full `PROMPT.md`.

## Suitable tasks

- doc sync / dashboard refresh (`.autopilot/대시보드.md`, OPERATOR-LIVE.ko.*)
- narrow audits under `.autopilot/`, `docs/`, `examples/`
- HISTORY-ARCHIVE rotation, METRICS trim
- validator re-runs (`tools/Validate-Dad-Packet.ps1`, smoke)
- short dashboard / operator-copy polish

## Not for

- any change under `CodexClaudeRelay.Core/`, `CodexClaudeRelay.Desktop/`,
  `CodexClaudeRelay.CodexProtocol/`, or the protocol test projects
- anything touching `.prompts/`, `Document/DAD/`, root contracts, `tools/`
- MVP gate flips (`.autopilot/MVP-GATES.md`)
- IMMUTABLE blocks in `PROMPT.md`
- decision PRs — those are full-prompt-only

## Read order (strict)

1. `.autopilot/STATE.md`
2. `PROJECT-RULES.md`
3. `CLAUDE.md` (peer-symmetry reminder, first ~30 lines)
4. Only the files explicitly needed for the chosen task

## Rules

- Respect `protected_paths:` in `STATE.md`. If the intended change touches
  any protected path, abort this lite run and escalate to `PROMPT.md`.
- Never edit IMMUTABLE blocks in `.autopilot/PROMPT.md`.
- Never introduce or widen an agent-identifier branch (`if codex` / `if
  claude`) from this prompt. Peer symmetry is an invariant.
- Never open a PR from this prompt. Commit to a maintenance branch
  (`maint/<slug>-<YYYYMMDD>`) and hand off. Full PR flow stays in
  `PROMPT.md`.
- Never self-evolve `PROMPT.md` — evolution stays in the full boot stack.
- Before any write, check `.autopilot/HALT`.

## Execution contract

1. Pick one well-scoped maintenance task.
2. Read only the files required.
3. Make the smallest coherent change.
4. Run one narrow verification step (one validator, one test filter).
5. Sync affected dashboard / HISTORY line in the same run. Apply the
   streak-collapse rule: if nothing changed, update an in-place
   `(streak: N)` line instead of appending.
6. Report: what changed, what was verified, what remains blocked.
7. Exit-contract Steps 1-7 from full `PROMPT.md` still apply — write
   METRICS line, remove LOCK, write `NEXT_DELAY`, call `ScheduleWakeup`,
   write 2-line `LAST_RESCHEDULE`. Lite does NOT relax the reschedule
   discipline.

## Invocation

- `AUTOPILOT_PROMPT_RELATIVE=.autopilot/PROMPT.lite.md` then `project.ps1 start`
- One-shot Codex:
  `Get-Content -Raw .autopilot\PROMPT.lite.md | codex exec -C .`
