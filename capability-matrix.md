# Capability Matrix

Audit date: 2026-04-17

This matrix records what is confirmed locally, what is conditional, and what is still pending audit.

## Current Status

| Area | Codex | Claude | Relay/Broker status | Evidence |
|---|---|---|---|---|
| Bounded turn handoff | Working | Working | Working | existing smoke path + current desktop default runtime |
| Interactive transport | Working (app-server one-turn) | Working (stream-json one-turn) | Experimental | existing prototype implementation |
| Broker-routed approval | Working | No | Asymmetric by design | `CLAUDE-APPROVAL-DECISION.md` |
| Command/git policy | Working | Audit-only | Working for Codex approval path | current `RelayApprovalPolicy.cs` |
| MCP config discovery | Working | Conditional | Partial | `mcp-audit.md` |
| MCP tool call execution | Working | Conditional | Partial | `mcp-audit.md` |
| MCP action classification | Working | Working | Working | `mcp-audit.md` + current policy/adapters |
| MCP review bridge and low-risk defaults | Working | Working | Working | `mcp-audit.md` + current broker policy |
| MCP pre-execution broker approval | No | No | Not implemented | current product gap |
| Shell/PowerShell audit | Working | Working | Partial | `shell-audit.md` |
| Git audit (read-only) | Working | Working | Partial | `git-audit.md` |
| Git audit (destructive / push / PR) | Working (add/commit/push) | Pending live | Partial | `git-audit.md` destructive-tier section; session `destructive-qa-20260417-131500`; PR live exercise still pending |
| Git category classification on Windows | Working | Working | Working | verified live in QA session `git-classify-qa-20260417-115929`; `RelayApprovalPolicy.ClassifyCommandCategory` now unwraps `powershell`/`pwsh`/`cmd /c` wrappers and strips `git -c`/`-C` option pairs; Codex adapter refines `commandExecution` items into the specific git class |
| DAD asset classification | Working (writes surfaced as `dad-asset`) / Working (pure reads) | Pending live | Working | `dad-asset-audit.md`; session `dad-asset-qa-20260417-145500`; iteration-8 classifier adds `read` category verified in session `read-classify-qa-20260417-180000`; iteration-9 adds dedicated `dad-asset` band verified in session `dad-asset-band-qa-20260417-190000` (2× `dad.asset.requested`/`.completed` for Document/dialogue/**, 1× `file.change.*` for repo-root write) |
| Read category classification | Working | Working | Working | session `read-classify-qa-20260417-180000` — pure `Get-Content 'path'` routed to `read.requested`/`.completed`; piped/scripted compound PowerShell correctly stays `shell` |
| Codex Windows compatibility matrix | Working | n/a | Working | `codex-windows-matrix.md` — consolidated from shell/git/MCP/DAD live sessions |
| Phase E tool-rich interactive contract (E1) | Working (edit/test/inspect/summarize all in one turn) | Pending live | Working (handoff-boundary leg pending fresh DAD workspace) | `phase-e-live-1-audit.md`; session `phase-e-edit-test-qa-20260417-200500` — read×19/shell×17/fileChange×1 in Turn 1, unittest fixed and re-run green |
| Phase E failure-to-handoff recovery (E2) | Working (live `repair.requested`/`.completed` end-to-end) | Pending live | Working | `phase-e-live-2-audit.md`; session `phase-e-repair-qa-20260417-210800` — Turn 1 emits bare `done`, broker repairs via `RunRepairAsync`, Codex produces valid marker block on retry; action history preserved |
| Phase F rolling summary (F1) + summary events (F4) | n/a | n/a | Not implemented | `phase-f-survey.md` — rotation infrastructure exists, but no `summaries/` write and no `summary.*` events |
| Phase F carry-forward state (F2) | n/a | n/a | Partial (LastHandoff + UpdatedAt + RotationCount only) | `phase-f-survey.md` — Goal/Completed/Pending/Constraints/LastHandoffHash not surfaced as first-class fields |
| Phase F prompt assembly (F3) | n/a | n/a | Partial (PendingPrompt = LastHandoff.Prompt only) | `phase-f-survey.md` — no rolling-summary or carry-forward injection |

## Interpretation

### Working

- feature is confirmed locally with real commands or existing relay smoke coverage

### Conditional

- feature works only when workspace configuration or external runtime state is present

### Partial

- the relay can observe and govern part of the feature, but not the full desired product behavior yet

## Next Audit Priorities

1. ~~Shell/PowerShell audit~~ (done)
2. ~~Git audit (read-only)~~ (done)
3. ~~Git audit — live destructive add/commit/push end-to-end~~ (done — `gh pr create` live exercise still pending, blocked on real remote)
4. ~~Fix Codex/Windows PowerShell-wrapping classifier gap~~ (done)
5. ~~Honour `AutoApproveAllRequests` for server-originated `item/commandExecution/requestApproval` events in the Codex interactive transport~~ (done — adapter now resolves auto-approve synchronously and emits the correct `accept` decision)
6. ~~DAD asset classification~~ (done — partial: writes classified via `file-change`, reads collapse into `shell` on Codex/Windows because reads are wrapped as `powershell.exe -Command "Get-Content …"`)
7. ~~Add a dedicated DAD-asset band in `RelayApprovalPolicy`~~ (done — iteration 9 adds `dad-asset` category + Codex adapter fileChange refinement; backlog/state paths escalate to critical; verified live in session `dad-asset-band-qa-20260417-190000`)
8. ~~Extend `ClassifyCommandCategory` to recognise `Get-Content`/`cat`/`type <path>` inside unwrapped PowerShell payloads so DAD-asset reads are not indistinguishable from arbitrary shell~~ (done — `read` category added and auto-allowed; compound/piped commands correctly remain `shell`)
9. ~~Codex Windows compatibility matrix~~ (done — see `codex-windows-matrix.md`)
10. Git sh.exe / msys pipe-creation failure under the relay's Job Object sandbox (surfaced by the auto-approve push QA — non-blocking for approval flow, but a real Windows compatibility gap)
