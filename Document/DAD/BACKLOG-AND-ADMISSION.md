# DAD Backlog And Admission

Use this file for backlog scope, admission rules, and session linkage.

## Purpose

`Document/dialogue/backlog.json` is a lightweight admission layer for future session candidates.

It is not:

- an execution log
- a peer handoff buffer
- a replacement for packets, state, or summaries

## Ownership Model

- Session artifacts own current execution truth.
- `handoff.next_task` owns continuation inside the current session.
- `Document/dialogue/backlog.json` owns future session candidates.

If these roles overlap, the backlog has become too heavy.

## File And Policy

Backlog file: `Document/dialogue/backlog.json`

Skeleton:

```json
{
  "schema_version": "dad-v2-backlog",
  "policy": {
    "max_now_items": 1,
    "allow_active_session_without_backlog_link": false
  },
  "items": []
}
```

## Item Meaning

Each backlog item is a candidate session outcome, not a turn plan.

Minimum fields:

- `id`
- `title`
- `status`
- `workstream`
- `desired_outcome`
- `session_warrant`
- `acceptance_signal`
- `risk_class`
- `recommended_scope`

Helpful tracking fields:

- `active_session_id`
- `closed_by_session_id`
- `derived_from_ids`
- `blocked_by`
- `why_not_now`
- `evidence_refs`
- `session_history`

## Allowed Statuses

- `now`: next session candidate when no active session exists
- `next`: near-term candidate, but not yet admitted
- `later`: useful candidate, intentionally deferred
- `blocked`: candidate waiting on an external blocker or missing condition
- `promoted`: currently linked to the active session
- `done`: closed by a session outcome
- `dropped`: intentionally not pursued

## Admission Rules

- Every new non-recovery session must link to exactly one backlog item.
- `tools/New-DadSession.ps1` may auto-bootstrap a backlog item only for fresh user-started work when no reusable `now` candidate already exists.
- If an active session exists, do not keep a separate `now` item.
- If an active session already exists, queue newly discovered future work as `next`, `later`, or `blocked` instead of promoting it immediately.
- If no active session exists, keep at most one `now` item.
- If a `now` item already exists, reuse or reprioritize it instead of auto-bootstrapping a duplicate fresh item.
- Do not auto-bootstrap a sibling candidate for work already represented by a queued or promoted item; reuse, reprioritize, or split explicitly instead.
- Promotion happens when a session is created, not when a peer handoff is written.
- Do not open backlog-only sessions or peer debates just to groom prioritization unless `dad-system-repair` is itself the concrete outcome.
- Treat backlog priority as user-facing admission metadata, not as an autonomous scheduler that overrides explicit user intent.

## Hard Separation From Handoffs

Use `handoff.next_task` when:

- the work stays inside the same session
- the remaining work is the next slice of the same outcome

Use the backlog when:

- the work needs a different session
- the work becomes out-of-scope for the current session
- a blocker forces later re-entry
- a separate artifact or verified decision is needed

Short rule:

- same session => `handoff.next_task`
- different session => backlog

## Product vs System Repair

Allowed `workstream` values:

- `product`
- `dad-system-repair`

Only `dad-system-repair` items may justify documentation/validator/prompt repair as the primary outcome.

Product backlog items must not be ceremony-only work such as:

- wording correction
- summary/state sync
- closure seal
- validator-noise cleanup
- verify-only ritual

## Closeout Rules

- `recovery_resume` is not a backlog promotion event.
- The same closeout that changes a session away from `active` should also resolve the linked backlog item or re-queue it deliberately. Do not leave a dangling `promoted` item behind after session closeout.
- Session convergence usually resolves the linked backlog item as `done`.
- Session abandonment may leave the linked item `blocked`, `next`, or `dropped`.
- Session supersede does not automatically drop the linked item.
- Non-`promoted` items must clear `active_session_id`. Terminal `done` items must record `closed_by_session_id`.
- If a successor session continues the same outcome, reuse the existing linked item or split it intentionally; do not bootstrap an unrelated sibling just because the session id changed.

## Canonical Truth

Backlog state is reconstructible metadata.

Session packets, session state, and session summaries remain the canonical execution evidence.
