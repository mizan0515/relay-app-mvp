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
