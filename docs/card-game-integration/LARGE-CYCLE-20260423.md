# Large Cycle 2026-04-23

## Goal

Run one real autopilot -> relay cycle against `D:\Unity\card game` through the operator-facing path, avoid tailing full logs, and record the failures that still block safe non-developer use.

## What was run

- Unity preflight:
  - `.autopilot/project.ps1 doctor`
  - Unity MCP telemetry and console checks
- Relay/autopilot path:
  - `scripts/card-game/Invoke-CardGameAutopilotLoop.ps1 -CardGameRoot 'D:\Unity\card game' -MaxSessions 1 -ForceRelay`
- Compact waiting only:
  - `scripts/card-game/Get-CardGameManagerSignal.ps1`
  - `scripts/card-game/Get-CardGameRelaySignal.ps1`
  - `scripts/card-game/Wait-CardGameRelaySignal.ps1 -TimeoutSeconds 420`
- Operator proof screenshots:
  - `scripts/gui-smoke/out-easy-operator/20260423-112709-after-launch.png`
  - `scripts/gui-smoke/out-easy-operator/20260423-112710-after-config.png`
  - `scripts/gui-smoke/out-easy-operator/20260423-113311-after-easy-start.png`

## What was observed

- Unity MCP bridge was live in the editor before the run.
- The relay first replaced a stale session, then continued with `companion-depth-first-slice-20260423-112716`.
- That relay session stayed `Active` on turn 2.
- `last_progress_at` stopped advancing while `source_pid` was still alive.
- `watchdog_remaining_seconds` crossed below zero and kept decreasing.
- The bounded waiter eventually returned:
  - `[RELAY_DONE] false status=timeout reason=watcher_timeout`
- A newer prepared manifest for the same task was generated as `companion-depth-first-slice-20260423-115403`, but the compact manager surface still had to reconcile that with the older active relay session.
- Event-log evidence for the active relay did not show an actual Unity MCP tool call.

## Problems found

1. Hung relay sessions were not escalated quickly enough.
   - Before this fix, `Wait-CardGameRelaySignal.ps1` only treated `process missing` as stale.
   - A live-but-stuck process with an expired watchdog could still keep operators waiting.

2. Prepared-session state and active-relay state could diverge.
   - The loop could prepare a new session while the compact live signal still pointed at an older active relay.
   - That made the operator surface harder to trust.

3. Unity MCP usage was not being recorded as compact evidence.
   - The current relay flow had no small script that could answer `did the peer really use Unity MCP?`
   - `Write-CardGameAutopilotResult.ps1` also hard-coded MCP metrics instead of deriving them from session evidence.

## Improvements added

- `Get-CardGameRelaySignal.ps1`
  - adds `HungWatchdog` derived status when the relay stays active past a watchdog-expiry grace window
- `Wait-CardGameRelaySignal.ps1`
  - exits early with `status=hung reason=watchdog_expired` instead of waiting until an external timeout
- `Get-CardGameManagerSignal.ps1`
  - adds `relay_hung`
  - adds `relay_session_mismatch`
- `Get-CardGameRelayEvidence.ps1`
  - emits a compact marker for whether Unity MCP tool calls were actually observed in the current relay event log
- `Write-CardGameAutopilotResult.ps1`
  - now fills MCP metrics from compact evidence instead of hard-coding zero
- `profiles/card-game/prompt-prefix.md`
  - now explicitly tells peers which Unity MCP tasks they should prefer and requires the final handoff to say whether Unity MCP was used

## Current conclusion

- The relay/autopilot integration is real and runnable, but one large-cycle failure mode was confirmed:
  - a peer can leave the relay `Active` with no progress until well after the watchdog expires
- After this patch, that state should surface as `relay_hung` quickly enough for a non-technical operator to stop waiting and prepare a fresh session.
- Unity MCP was available, but this cycle did not provide evidence that the relay peers actually used it.

## Follow-up run after budget and manager-guard changes

- Real GUI operator path:
  - `scripts/gui-smoke/run-gui-easy-operator.ps1 -CardGameRoot 'D:\Unity\card game' -TaskSlug 'tool-qa-menu-coverage-matrix' -TimeoutSeconds 600`
- Screenshots:
  - `scripts/gui-smoke/out-easy-operator/20260423-125632-after-launch.png`
  - `scripts/gui-smoke/out-easy-operator/20260423-125632-after-config.png`
  - `scripts/gui-smoke/out-easy-operator/20260423-130437-after-easy-start.png`

### What improved

- The session no longer failed on the earlier `state.json missing` completion path.
- The operator surface stopped waiting and surfaced the stop condition directly.
- The relay completed one successful bounded cycle on session `tool-qa-menu-coverage-matrix-20260423-125639`.

### New issues found

1. Intentional one-cycle pauses were misclassified as blockers.
   - Relay signal:
     - `[RELAY_DONE] false status=Paused reason=Paused_intentionally_after_one_successful_relay_cycle.`
   - Manager signal still surfaced:
     - `overall=relay_paused_error`
     - `action=fix_blocker`

2. Unity MCP still was not actually used by the peer during the QA/editor slice.
   - Compact evidence:
     - `[RELAY_EVIDENCE] unity_mcp=not-observed count=0`
   - The prompt explicitly asked for Unity MCP and named suitable tools, but the peer still spent its bounded cycle on shell/document reads.

### Follow-up improvements added

- `Get-CardGameLoopStatus.ps1`
  - treats `Paused intentionally after one successful relay cycle.` as resumable `run`, not `blocked`
- `Get-CardGameManagerSignal.ps1`
  - emits `relay_cycle_complete` with `success=true` for the intentional one-cycle pause path
- `Get-CardGameRelayEvidence.ps1`
  - emits `unity_mcp_required` and `unity_mcp_required_but_missing`
  - upgrades the compact marker to `[RELAY_EVIDENCE] unity_mcp=required-but-missing count=0` when the task asked for Unity MCP but the peer never used it
- `profiles/card-game/prompt-prefix.md`
  - now requires at least one Unity MCP verification action for `qa-editor` / `Tools/QA/*` slices when MCP is available

## Four-turn GUI worksession with Unity MCP task

- Real GUI worksession path:
  - `scripts/gui-smoke/run-gui-worksession.ps1` with session `tool-qa-menu-coverage-matrix-20260423-130655`
  - turns: `4`
- Screenshots:
  - `scripts/gui-smoke/out-worksession/20260423-130721-after-launch.png`
  - `scripts/gui-smoke/out-worksession/20260423-130722-after-config.png`
  - `scripts/gui-smoke/out-worksession/20260423-130819-after-check-adapters.png`
  - `scripts/gui-smoke/out-worksession/20260423-130825-after-start.png`
  - `scripts/gui-smoke/out-worksession/20260423-132153-after-autorun.png`

### What this proved

- Relay peers did use Unity MCP in a real session.
- Compact event evidence showed Codex calling:
  - `unityMCP/read_console`
  - `unityMCP/manage_editor` with `play`
  - `unityMCP/execute_menu_item` for `Tools/QA/Navigate/Go To ProbeResult`
  - `unityMCP/manage_editor` with `stop`
- The MCP call reached completion and returned `[QA-*]` console data, so the missing piece was not peer capability.

### New blocker found

1. The broker still paused the session after the first observed MCP action.
   - Even though the UI had dangerous auto-approve enabled, the relay moved to `AwaitingApproval` because policy-gap approvals were queued after the fact and the Desktop auto-approve path only handled broker-routed interactive approval requests.
   - Manager signal also mislabeled this state as `relay_ready`.

### Follow-up improvements added

- `MainWindow.xaml.cs`
  - auto-resolves queued approvals when dangerous auto-approve mode is enabled
- `Get-CardGameManagerSignal.ps1`
  - adds `approval_required` / `review_pending_approval` for `AwaitingApproval`
- `Get-CardGameLoopStatus.ps1`
  - treats `AwaitingApproval` as `blocked` instead of `run`
- `Get-CardGameRelayEvidence.ps1`
  - now counts real `mcp_tool_call` activity
  - now recognizes `unityMCP` server calls as Unity MCP evidence
  - can distinguish `unity_mcp=approval-blocked` from `required-but-missing`

## Final follow-up run after queued-approval auto-resolve

- Real GUI worksession path:
  - `scripts/gui-smoke/run-gui-worksession.ps1` with session `tool-qa-menu-coverage-matrix-20260423-132505`
  - turns: `4`
- Screenshots:
  - `scripts/gui-smoke/out-worksession/20260423-132522-after-launch.png`
  - `scripts/gui-smoke/out-worksession/20260423-132524-after-config.png`
  - `scripts/gui-smoke/out-worksession/20260423-132544-after-check-adapters.png`
  - `scripts/gui-smoke/out-worksession/20260423-132554-after-start.png`
  - `scripts/gui-smoke/out-worksession/20260423-133904-after-autorun.png`

### What this proved

- Unity MCP is now confirmed in a real relay session with compact evidence:
  - `[RELAY_EVIDENCE] unity_mcp=observed count=4`
- Compact manager surface recovered correctly after the process disappeared:
  - `overall=relay_dead`
  - `action=prepare_fresh_session`
- Auto-approve no longer leaves the session stuck forever in `AwaitingApproval` for queued broker approvals.

### Remaining issue after this run

1. The peer reached Unity MCP successfully, then later triggered a web tool review and the relay process disappeared after the queued web approval was auto-resolved.
   - Evidence from the same session:
     - `mcp.requested` / `mcp.completed` for `unityMCP/read_console`
     - `approval.queue.resolved` and `session.resumed` for `Web Tool Approval`
   - This is a different blocker from the original Unity MCP problem.
   - It also wastes tokens for Unity-local tasks because web activity was unnecessary here.

### Follow-up improvement added

- `profiles/card-game/prompt-prefix.md`
  - now explicitly forbids web/browser tools unless the current relay task truly requires internet research
