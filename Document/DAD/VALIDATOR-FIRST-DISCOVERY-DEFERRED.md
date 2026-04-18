# Validator-First, Discovery-Deferred

Use this file to understand why the current DAD v2 priority is closeout-enforcement over discovery-automation, what was adopted, what was deferred, and under which conditions a `/discover` command may be reopened.

## 1. Why Validator Reinforcement Is The Current Priority

DAD v2 encodes three ownership rules:

- session owns execution
- `handoff.next_task` owns current-session continuation
- `Document/dialogue/backlog.json` owns admission of future sessions

Before this adjustment, the rule "`handoff.next_task` is for current-session continuation only; work that needs a different session goes to the backlog" was documented in `PACKET-SCHEMA.md` but was not enforced by the packet validator. A session could close with `handoff.closeout_kind: final_no_handoff` while still carrying a non-empty `handoff.next_task`, producing an orphan continuation pointer that contradicts closing-session semantics.

This was an enforcement gap, not a missing feature. The system was under-enforced, not under-featured.

## 2. The `final_no_handoff` + `handoff.next_task` Rule

Current rule, enforced by `tools/Validate-DadPacket.ps1` and documented in `PACKET-SCHEMA.md`:

- A closeout packet with `handoff.closeout_kind: final_no_handoff` must keep `handoff.next_task` empty.
- A closing session has no continuation. Any remaining follow-up work must be admitted to `Document/dialogue/backlog.json` before sealing the session.
- Validator level: error.

Rationale for error rather than warning:

- The other two `final_no_handoff` structural checks (`ready_for_peer_verification: false`, empty `prompt_artifact`) are already errors. Parity of severity keeps the rule set coherent.
- A warning would allow the orphan pattern to persist silently and reintroduce the exact gap this rule closes.
- The template source repository has no live packets that would be retroactively broken. Downstream adopters may need a one-time migration (clear `next_task`, or admit the remaining work to the backlog), which is a bounded cost, not an ongoing tax.

### Migration Note For Existing Downstream Packets

If a downstream repository already contains `final_no_handoff` packets with a non-empty `handoff.next_task`, fix them one of two ways:

1. If the text is only a stale continuation marker, clear `handoff.next_task` and keep the session closeout unchanged.
2. If real follow-up work remains, create or reuse the matching backlog item first, then clear `handoff.next_task` before revalidating the packet.

Do not keep the text in place and rely on a local convention. A closing session has no continuation pointer.

## 3. `handoff.next_task` Is Closeout Enforcement, Not Discovery Source

Earlier iterations briefly considered `handoff.next_task` as a discovery signal. That treatment is rejected.

- `handoff.next_task` is part of the closeout contract, not a standing backlog.
- Scanning `handoff.next_task` for unreflected follow-up would create a parallel execution log outside session ownership.
- Correct path: if continuation is needed, it lives in the `handoff.next_task` of a session that continues with `peer_handoff`; if it does not belong in the current session, it becomes a backlog item. There is no third bucket.

## 4. Why `/discover` Is Deferred

A manual `/discover` slash command that scans the repository for follow-up candidates was evaluated repeatedly. The current decision is to defer it.

Reasons:

- After excluding `validator ERROR`, `open_risks`, and `handoff.next_task` (all of which belong to enforcement, not discovery), the remaining input surface is narrow:
  - newly added `TODO` / `FIXME` / `HACK` markers
  - structured `blocked` backlog item blocker resolution (and only if `blocked_by` becomes structured; it is currently free-text)
- These signals are reachable with existing shell tools (`git log`, `grep`, `jq`). Availability does not require a new slash command.
- Claude Code official docs confirm capability is not the constraint:
  - Custom commands and skills both support shell preprocessing, arguments, and local file access ([code.claude.com/docs/en/skills](https://code.claude.com/docs/en/skills), accessed 2026-04-16).
  - Cloud Routines run against a fresh clone with no local file access ([code.claude.com/docs/en/desktop-scheduled-tasks](https://code.claude.com/docs/en/desktop-scheduled-tasks), accessed 2026-04-16), so any automated variant is ruled out by design.
- The closeout enforcement described in Section 2 covers the most common class of "missed follow-up" issue by making orphan continuation illegal.

## 5. Deferred `/discover` Minimum Design (for future reference)

If `/discover` is ever reopened, the following constraints apply:

- Manual slash command only. No scheduled, cron, or routine-based invocation.
- Local only. No web search, external lookup, or telemetry.
- No separate discovery-candidates.json staging registry. Use the existing `Document/dialogue/backlog.json` only.
- Optional backlog provenance field: `source: "user" | "session" | "discovery"`. The tolerance of unknown fields in `tools/Validate-DadBacklog.ps1` does not make this safe by itself; the validator must be extended to enforce the allowed set in the same work package.
- Discovery may create `next` or `later` items only. Never `now`, `promoted`, `done`, `dropped`, or `blocked`.
- Discovery must not run while an active session exists. The existing admission rules in `BACKLOG-AND-ADMISSION.md` still apply.
- Direct promotion is forbidden. Promotion always happens through session creation.
- Input sources limited to:
  - newly added `TODO` / `FIXME` / `HACK` markers since a commit baseline
  - structured `blocked` backlog item unblock status, only if `blocked_by` has been migrated to structured references
- Explicit exclusions:
  - validator ERROR (belongs to enforcement)
  - `open_risks` (belongs to session evidence)
  - `handoff.next_task` (belongs to closeout enforcement)
  - wording correction, summary/state sync, closure seal, validator-noise cleanup, and any other meta-only ceremony

## 6. Adopted, Deferred, Rejected

Adopted:

- `tools/Validate-DadPacket.ps1` error check: `final_no_handoff` with non-empty `handoff.next_task` is invalid (en/ko symmetric).
- `PACKET-SCHEMA.md` rule statement extended to match (en/ko symmetric).
- `.prompts/03-turn-closeout-handoff.md` tightened to remove the open-ended "unless the repository has a specific follow-up convention" escape for `final_no_handoff` (en/ko symmetric).
- `VALIDATION-AND-PROMPTS.md` cross-reference noting the new rule and pointing to this file (en/ko symmetric).

Deferred:

- `/discover` manual slash command.
- `-Source` parameter on `tools/Manage-DadBacklog.ps1` and `source` field enforcement in `tools/Validate-DadBacklog.ps1` (only needed if `/discover` is reopened).
- A warning on `handoff.context` leakage for `final_no_handoff` (only useful if operational data shows continuation pointer leak into `context`).

Rejected:

- Cloud Routines for discovery. Cloud routines run against a fresh clone with no local file access, per [code.claude.com/docs/en/desktop-scheduled-tasks](https://code.claude.com/docs/en/desktop-scheduled-tasks).
- Desktop Scheduled Tasks for automated discovery. Technically feasible but violates the "manual only, no automated invocation" requirement.
- A separate discovery-candidates.json staging registry. Duplicates backlog responsibility and reintroduces a parallel execution log outside session ownership.

## 7. Conditions To Reopen `/discover`

`/discover` should be reopened only if all of the following become true:

1. `blocked_by` has been migrated to a structured schema (id reference or URL), making "blocker resolution" a validatable signal rather than a free-text match.
2. After at least one release cycle with the closeout enforcement in Section 2, observed orphan-follow-up incidents continue at a rate that enforcement alone cannot explain away.
3. A concrete artifact or verified decision class is identified that `/discover` would produce, beyond a scan report. A scan report alone is not an admissible session outcome under `BACKLOG-AND-ADMISSION.md`.
4. The en/ko parity cost and the `tools/Validate-DadBacklog.ps1` extension (`source` field enforcement) are budgeted inside the same work package that introduces the command.

If any of these conditions is not met, do not reopen `/discover`.
