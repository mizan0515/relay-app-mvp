# 09. Emergency Session Recovery

## Purpose

When normal session resume is impossible, force-close the DAD v2 session or manually repair `state.json` while minimizing operational risk.

## When To Use

- When `Document/dialogue/state.json` or `sessions/{session-id}/state.json` is broken
- When turn packets remain but state is out of sync with the latest turn
- When `tools/Validate-DadPacket.ps1` itself fails, or a validator defect blocks normal resume
- When you need to force-close and move to a new session

## Emergency Procedure

1. Read the root `state.json`, every session-scoped `state.json`, and the latest `turn-{N}.yaml`.
2. Classify the damage scope.
   - Packets are intact but state is wrong
   - Packets are also partially damaged
   - The validator itself is wrong
3. If packets are more trustworthy, reconstruct state from packets.
4. If packets are also untrustworthy, force-close the session as `abandoned` or `superseded`.
5. On force-close, align all of the following together.
   - Root `Document/dialogue/state.json`
   - `Document/dialogue/sessions/{session-id}/state.json`
   - `summary.md`
   - Named summary for the closed session
6. After recovery, re-run `tools/Validate-DadPacket.ps1 -Root . -AllSessions`.
7. If the validator is still broken, state the validator defect explicitly and record the bypass justification in the session summary or handoff.

## Manual State Recovery Checklist

Minimum fields:

- `protocol_version: "dad-v2"`
- `session_id`
- `session_status`
- `relay_mode: "user-bridged"`
- `mode`
- `scope`
- `current_turn`
- `max_turns`
- `last_agent`
- `contract_status`
- `packets`

Recovery rules:

- `current_turn` must equal the `turn` in the latest `turn-{N}.yaml`.
- `last_agent` must equal the `from` in the latest packet.
- `packets` must equal the list of actually existing packet relative paths.
- Closed sessions require `closed_reason`.
- If `session_status: superseded`, `superseded_by` is required.

## Force-Close Criteria

- Packets and state are both partially damaged, with insufficient evidence to recover
- Even fixing the validator leaves existing session artifacts untrustworthy
- Restarting as a new session is safer for the user

Recommended status values:

- Give up on recovery: `abandoned`
- Replace with a new session: `superseded`

## Validator Outage Bypass Rules

1. Prove the error is a validator code defect, not session data, first.
2. Record the proof with file/line evidence.
3. Fix the validator in the same turn where possible.
4. If it can't be fixed in the same turn, record validator repair and revalidation as the first task in the next handoff.
5. Do not record `suggest_done: true` while in bypass state.

## Forbidden

- Editing state manually without reading the packets
- Closing after force-close without a summary
- Arbitrarily skipping validation without proving a validator defect
- Closing as `converged` without evidence
