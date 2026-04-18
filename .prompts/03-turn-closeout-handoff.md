# 03. Turn Closeout / Handoff Cleanup

## Purpose

Close out a turn consistently across the Turn Packet, state, handoff prompt, and remaining risks.

## When To Use

- Just before ending each turn
- After finishing self-iteration and preparing the peer handoff
- Also on a final converged no-handoff turn when you need to close the packet/state cleanly without fabricating a peer prompt

## Procedure

1. Organize the real changes made in this turn and the verification evidence.
2. Evaluate each checkpoint as PASS / FAIL / FAIL-then-FIXED / FAIL-then-PASS.
3. Separate `issues_found`, `fixes_applied`, and `open_risks`.
4. Before planning another peer turn, ask whether the remaining work is still outcome-scoped. Do not create a follow-up session or handoff whose only purpose is wording correction, summary/state sync, closure seal, or validator-noise cleanup unless the DAD system itself needs repair.
5. If the work now needs a different session, write it into `Document/dialogue/backlog.json` instead of smuggling it through `handoff.next_task`. Keep `handoff.next_task` for same-session continuation only, do the backlog update inside this closeout rather than opening a separate backlog-grooming session, and avoid auto-bootstrapping a duplicate sibling item when an equivalent candidate already exists.
6. If another peer turn remains, write `handoff.next_task` and `handoff.context` so the next agent can pick up directly. On a final converged no-handoff turn, `handoff.next_task` must stay empty: a closing session has no continuation, and any remaining follow-up work must be admitted to `Document/dialogue/backlog.json` in the same closeout path. `handoff.context` may carry a brief wrap-up note on a final no-handoff turn, but must not be used as an alternate continuation pointer.
7. If another peer turn remains, save the exact peer prompt to `Document/dialogue/sessions/{session-id}/turn-{N}-handoff.md`, then store that path in `handoff.prompt_artifact`. On a final converged no-handoff turn, leave `handoff.prompt_artifact` empty.
8. Set `handoff.closeout_kind` explicitly: use `peer_handoff` for normal relay, `final_no_handoff` for same-turn session closeout, and `recovery_resume` only for interruption or context overflow.
9. Treat dedicated peer-verify-only handoffs as risk-gated exceptions. Use them only for remote-visible mutations, config/runtime decisions, high-risk measurements, destructive cleanup, or provenance/compliance-sensitive work.
10. If system-doc drift remains, record it as the first item in `handoff.next_task`.
11. Run the validators after saving the turn packet and, when present, the handoff prompt artifact. If this closeout ends, blocks, or supersedes the session, resolve or re-queue the linked backlog item in the same path and validate again before writing `suggest_done: true`.
12. Set `handoff.ready_for_peer_verification: true` only when another peer turn remains and `handoff.next_task`, `handoff.context`, and `handoff.prompt_artifact` are all final.
13. When a peer prompt exists, verify the required prompt elements, the mandatory tail block, and that the same closeout reply pastes the same text as the saved handoff artifact. A response that only says "prompt saved" is incomplete.

## Done Gate Check

- Is there a peer PASS after the most recent change?
- Is the evidence reproducible?
- Did validators pass?
- Are remaining risks hidden implicitly?
- If a next turn is being proposed, is it still outcome work rather than a verify-only / wording-only / sync-only / seal-only relay?
