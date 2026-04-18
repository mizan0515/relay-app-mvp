# 02. Session Start / Contract Drafting

## Purpose

When starting a new DAD v2 session, establish the first Turn's Contract and execution scope without being under-scoped or drifting into meta-only work.

## When To Use

- When starting a new session
- When superseding an existing session and moving to a new one
- When the user's request is too short or ambiguous to turn directly into checkpoints

## Procedure

1. Read `PROJECT-RULES.md`, `AGENTS.md` or `CLAUDE.md`, and `DIALOGUE-PROTOCOL.md`.
2. Inspect live file state and the current branch.
3. Decide whether the proposed session is outcome-scoped. If the work is only wording correction, summary/state sync, closure seal, or validator-noise cleanup, fold it into the active execution session unless you are explicitly repairing broken DAD state.
4. Check `Document/dialogue/backlog.json` when it exists. Link the new session to one backlog item, or auto-bootstrap a new item only if the task is fresh and no `now` item is already waiting. If another active session still owns the current work, queue the new candidate instead of forcing immediate promotion.
5. Build a `task_model` first if needed.
6. Produce 3–5 checkpoints anchored on `success_shape`, `major_risks`, and `out_of_scope`.
7. In Turn 1, also execute the first feasible vertical slice.
8. Record the Contract draft and first execution evidence together in the Turn Packet.
9. If another peer turn remains, set `handoff.closeout_kind: peer_handoff`, save the relay prompt artifact, and paste the exact same prompt in the same final reply. Do not wait for the user to ask for the next prompt.

## Checkpoint Quality Standards

- Must be judgeable against a concrete implementation artifact.
- At least one checkpoint should name the concrete session outcome or artifact that justifies opening the session.
- The verification method must live alongside the checkpoint itself.
- Do not silently pull `out_of_scope` items back in.
- If project documentation drift is visible, surface it as a separate checkpoint or as a follow-up task.
