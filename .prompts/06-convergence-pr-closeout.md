# 06. Convergence Closeout / PR Cleanup

## Purpose

When the session is almost over, prevent missing the convergence verdict, summary artifacts, validation, and branch/PR cleanup. This includes the final turn where no peer handoff will follow.

## When To Use

1. When both agents report all major checkpoints as PASS
2. When about to record `handoff.suggest_done: true`
3. When closing the session as `converged` and proceeding with commit, push, PR, and merge follow-ups
4. When the current turn is the last dialogue turn and the same turn owner must finish git closeout

## Closeout Checklist

1. Confirm every Contract checkpoint in the latest turn packet is PASS.
2. If `handoff.suggest_done` is true, confirm `handoff.done_reason` is filled in concretely.
3. Confirm `Document/dialogue/state.json` and the session-scoped `state.json` accurately reflect `converged` status.
4. Confirm both `summary.md` and the named summary for the closed session exist.
5. Re-run minimum validation.
- `tools/Validate-Documents.ps1 -Root . -IncludeRootGuides -IncludeAgentDocs -Fix`
   - `tools/Validate-DadPacket.ps1 -Root . -AllSessions`
6. Check branch state and confirm no unrelated changes were mixed in.
7. Handle commit, push, PR, and merge per the policy in the root contract docs.
8. On a final converged turn with verified changes, do not stop after summary/state/validators. Complete task-branch commit + push + PR in the same turn, or report a concrete blocker with the exact missing step.
9. Treat "no next turn" as a closeout condition, not as permission to defer PR creation.

## Output Format

- `PASS`: closeout requirements satisfied, cite file/line evidence, validation, and branch/PR evidence or the explicit `PROJECT-RULES.md` exemption
- `FAIL`: missing closeout artifact, skipped PR without policy support, or procedural violation; show current/expected/fix diff
- `WARN`: closeout is possible now, but follow-up operational risk remains
