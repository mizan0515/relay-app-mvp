# DAD State And Lifecycle

Use this file for shared state, session directories, and lifecycle rules.

## Shared State

State file: `Document/dialogue/state.json`

Session directory: `Document/dialogue/sessions/{session-id}/`

Backlog file: `Document/dialogue/backlog.json`

Root `state.json` tracks the currently active session only. When a new session is created, the root state is overwritten. Previous session state is preserved in `sessions/{session-id}/state.json`.

## Operating Preference

- Prefer outcome-scoped sessions: each session should target a concrete artifact, verified decision, or explicit risk disposition.
- Treat backlog as admission metadata, not as execution truth. Current-session continuation remains in `handoff.next_task`.
- Do not start a fresh session only for wording correction, state/summary sync, closure seal, or validator-noise cleanup unless the DAD system itself is being repaired.
- Prefer short, session-scoped slices over one long umbrella session when the goal, verification surface, or ownership changes materially.
- When a new session replaces the current one, mark the previous session `superseded` or otherwise closed explicitly before moving on.
- Session closeout and backlog closeout move together. When a session leaves `active`, update the linked backlog item in that same closeout path instead of leaving a stale `promoted` linkage behind.

## Expected Session Contents

- `turn-{N}.yaml`
- `turn-{N}-handoff.md` for each turn that actually emits a peer prompt at closeout
- `state.json`
- `summary.md` for the session-scoped summary
- `YYYY-MM-DD-{session-id}-summary.md` for the named summary on closed sessions

On a final converged turn, summary/state artifacts close the dialogue lifecycle, but they do not automatically close repository git work. If verified changes exist, the same final turn still needs the commit/push/PR closeout required by `PROJECT-RULES.md`, or a concrete blocker.

Normal active turns should close as `peer_handoff`. The only no-prompt exceptions are `final_no_handoff` session closeout turns and explicit `recovery_resume` packets for interruption or context overflow.

Summary/state sync, closure confirmation, and final wording cleanup are same-turn closeout work, not a follow-on seal session.

When a session closes or is superseded:

- clear the linked backlog item's `active_session_id` unless another active session has already taken ownership
- set `closed_by_session_id` when the linked backlog item reaches `done`
- avoid creating a duplicate sibling backlog item for the same outcome unless you are intentionally splitting the outcome

## State Schema

`state.json` (both root and session-scoped) must contain:

| Field | Required | Values |
|-------|----------|--------|
| `protocol_version` | always | `"dad-v2"` |
| `session_id` | always | string |
| `session_status` | always | `active` \| `converged` \| `superseded` \| `abandoned` |
| `relay_mode` | always | `"user-bridged"` |
| `mode` | always | `autonomous` \| `hybrid` \| `supervised` |
| `scope` | always | `small` \| `medium` \| `large` |
| `current_turn` | always | integer (0 before first turn) |
| `max_turns` | always | integer |
| `last_agent` | after first turn | `codex` \| `claude-code` |
| `origin_backlog_id` | always on new sessions | backlog item id string |
| `contract_status` | always | `proposed` \| `accepted` \| `amended` |
| `packets` | always | array of relative paths |
| `closed_reason` | when status != `active` | string |
| `superseded_by` | when status == `superseded` | session-id string |

## Context Overflow

When an agent's context window fills mid-session:

1. Save the current work as a partial turn packet.
2. Set `confidence: low` and record the overflow in `open_risks`.
3. Set `handoff.closeout_kind: recovery_resume` and leave `handoff.prompt_artifact` empty.
4. Start a fresh context and use `.prompts/04-session-recovery-resume.md` to resume safely.
