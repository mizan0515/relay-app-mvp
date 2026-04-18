# 04. Session Recovery / Resume

## Purpose

Resume an interrupted DAD session from the last valid state without incorrectly stitching it back together.

## When To Use

- When a conversation broke and you need to continue
- When another agent's result arrives late and you need to resume
- When you need to check whether state and packets are out of sync

## Procedure

1. Read `Document/dialogue/state.json`.
2. Check the current `session_id`, `session_status`, `current_turn`, and `last_agent`.
3. Read the latest `turn-{N}.yaml` in the session directory.
4. Run `tools/Validate-DadPacket.ps1 -Root . -AllSessions` or validation against the target session.
5. If the status is not `active`, do not stitch on; decide whether a new session is needed.
6. If there is drift or a missing required artifact for that turn, recover it first and then start the next turn. Do not treat a final converged no-handoff turn as broken only because it has no prompt artifact.
7. If `state.json` is broken or the validator itself has failed, switch to the `.prompts/09-emergency-session-recovery.md` procedure instead of normal resume.

## Forbidden

- Continuing to write the next turn while the validator is failing
- Stitching onto a superseded / abandoned session as if it were active
- Trusting state alone without reading the packet
