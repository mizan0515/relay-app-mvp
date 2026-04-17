# Phase A Spec

## Goal

Phase A establishes the first product-grade safety floor for the relay prototype.

This phase does **not** try to finish the whole product. It focuses on the minimum set of broker-owned controls required before deeper machine work is trustworthy:

- approval routing
- action-level observability
- git safety defaults

If these are missing, the app can relay handoffs, but it cannot honestly claim that users control what Claude and Codex do on the machine.

## Scope

Phase A covers:

1. broker-visible action events
2. approval queue design
3. Codex approval request routing
4. Claude approval feasibility split
5. git safety defaults
6. UI surfaces for pending approvals and recent action events
7. operator-visible latest tool-activity summary

Phase A explicitly does **not** cover:

- full MCP policy management
- rolling summary
- carry-forward state
- sub-agent orchestration
- PR automation polish

## Product Outcome

After Phase A:

- the broker sees raw action events instead of only final handoff JSON
- Codex approval requests can be surfaced instead of dropped
- dangerous git operations have a default policy
- the UI can show operators what happened in a turn at the action level

## Current Code Reality

Relevant files:

- `RelayApp.CodexProtocol/CodexProtocolConnection.cs`
- `RelayApp.CodexProtocol/CodexProtocolTurnRunner.cs`
- `RelayApp.Core/Broker/RelayBroker.cs`
- `RelayApp.Core/Adapters/RelayAdapterResult.cs`
- `RelayApp.Desktop/Interactive/CodexInteractiveAdapter.cs`
- `RelayApp.Desktop/Interactive/ClaudeInteractiveAdapter.cs`
- `RelayApp.Desktop/Adapters/CodexCliAdapter.cs`
- `RelayApp.Desktop/Adapters/ClaudeCliAdapter.cs`
- `RelayApp.Desktop/MainWindow.xaml`
- `RelayApp.Desktop/MainWindow.xaml.cs`

Known gaps at the start of this phase:

- unsupported Codex server-initiated requests still fall back to `-32601 Not Implemented`
- Claude headless mode does not currently provide broker-routed approval UX
- broker event log is dominated by turn-level events
- git actions are not first-class broker concepts

## Phase A Deliverables

### A1. Action Event Transport

Adapters must be able to send observed action events to the broker.

Current implementation direction:

- `RelayObservedAction`
- `RelayAdapterResult.ObservedActions`
- broker appends action events into the existing JSONL event log

Initial event types:

- `adapter.event`
- `tool.invoked`
- `tool.completed`
- `tool.failed`
- `approval.requested`

Event payload rules:

- keep the raw tool/protocol payload when available
- prefer raw JSON over lossy summaries
- never block the turn because an action event could not be parsed

### A2. Approval Queue

The broker needs an in-memory approval queue with durable logging.

Current implementation baseline:

- a single pending approval now lives in session state as `PendingApproval`
- broker state now also keeps a durable approval queue/history as `ApprovalQueue`
- the relay can enter `AwaitingApproval`
- the desktop UI and exported diagnostics show the latest pending approval directly
- the desktop UI, diagnostics export, and automatic readable snapshots now also show an `Approval Queue` summary with pending/approved/denied/expired counts and recent entries
- session-scoped approval rules can now be saved into broker state when the operator chooses `Approve Session`
- matching follow-up approvals can auto-resolve from broker state without reopening the approval panel
- the desktop UI now exposes saved session approval rules, lets the operator clear them during a live session, and highlights when the current pending approval already matches a saved rule
- command approvals now carry a policy hint so operators can see whether the default broker policy would allow, deny, or require approval for the requested action
- git- and PR-related command approvals now carry a short structured summary so operators can review commits, pushes, and PR creation with more context than a raw shell line
- the desktop app now supports a dangerous operator-only auto-approve mode that skips the approval panel while preserving approval audit logs
- Claude `stream-json` action capture now preserves more structured tool and permission-denial signals, improving audit value even before full Claude approval routing exists
- Claude `stream-json` action capture now mirrors more of the Codex-side event taxonomy by emitting category-specific requested/denied events for classified Bash commands and permission denials
- generic Claude tool calls are now classified into broker-visible categories such as `mcp`, `web`, `file-change`, and `tool`, so non-Bash tool activity can be audited without collapsing into a single generic event
- Codex interactive and bounded `exec` item events now also preserve category/title metadata and emit category-specific audit events for non-agent item activity, including MCP- and web-shaped item types when exposed by the transport
- broker-owned policy-gap advisories now fire once per session segment when `mcp` or `web` activity is observed without broker-routed approval, and those first-seen activities now also create broker pending-review items so operators can approve once or approve for the rest of the session
- the first MCP-specific default review policy is now active: read-only resource discovery and telemetry ping/status actions auto-clear through broker policy, while other MCP activity still raises operator review items
- the desktop UI, diagnostics export, and automatic readable snapshots now expose a dedicated latest tool-activity summary alongside approvals and recent events
- the desktop UI, diagnostics export, and automatic readable snapshots now also expose a dedicated latest git/PR activity summary so repository-changing actions are visible without digging through general tool traffic
- the desktop UI, diagnostics export, and automatic readable snapshots now expose a categorized tool-activity summary so operators can quickly see session-level counts for categories such as `git.push`, `mcp`, `web`, and `shell`
- the desktop UI, diagnostics export, and automatic readable snapshots now also expose a dedicated `Policy Gap Summary` so unmanaged MCP/web activity is visible without reading the full event log
- the desktop UI now applies risk-aware highlighting to pending approvals and warning highlighting to the policy-gap summary, so operators can spot critical approval and unmanaged MCP/web activity faster during live turns
- the desktop UI, diagnostics export, and automatic readable snapshots now also expose a `Current Session Risk Summary`, aggregating pending approval risk, saved risky session rules, policy-gap categories, and dangerous auto-approve state into one operator-facing panel
- the `Current Session State` surface now includes a compact `Session Risk` badge so operators can glance the overall live risk posture before reading the detailed risk panel
- basic desktop operator settings now persist in a local `ui-settings.json` so runtime mode, working directory, and dangerous auto-approve state survive app restarts
- the current product decision for Claude approval is now written down explicitly: interactive Claude remains audit-only on `stream-json` until a separate Agent SDK path is approved
- bounded turn prompts and repairs now use the same marker-based handoff boundary as interactive turns, and the bounded Claude/Codex adapters no longer force raw-JSON-only output through CLI schema flags

What is still missing:

- richer multi-item concurrent approval handling beyond the current sequential queue/history model
- deeper MCP-specific policy beyond the current review-item/session-rule bridge
- a final product decision on whether Claude approval stays audit-only or gains a separate Agent SDK runtime option

Proposed item shape:

- `id`
- `session_id`
- `turn`
- `side`
- `category`
- `title`
- `message`
- `payload`
- `created_at`
- `status`

Categories for Phase A:

- `shell`
- `git`
- `pr`
- `approval-transport`
- `unknown-risk`

Statuses:

- `pending`
- `approved_once`
- `approved_session`
- `denied`
- `expired`

### A3. Codex Approval Routing

Codex approval handling is the first concrete broker-routed approval path.

Current implementation baseline:

- interactive Codex turns now run with `approvalPolicy=on-request`
- approval-needed operations can therefore surface into broker-visible state
- supported approval requests can now route through the desktop UI and return an explicit decision to `codex app-server`
- unsupported server-initiated requests still fall back to `Not Implemented`
- broker-side pause now only applies to unresolved approval requests, so already-decided approvals do not incorrectly leave the session in `AwaitingApproval`
- the first default git safety rules are now active for interactive Codex: read-only git inspection is auto-approved once, and destructive `git push --force` / `git reset --hard` are auto-denied
- direct pushes to protected branches such as `main`, `master`, `trunk`, `production`, `release/*`, and `hotfix/*` are now blocked by default, while PRs into those protected base branches remain approval-required but are elevated to critical risk
- the first default shell safety rules are now active for interactive Codex: clearly destructive machine-level commands are auto-denied before they reach operator approval
- Codex permission-escalation requests for additional network access or broad filesystem access are now blocked by default, and file-change approvals targeting protected git metadata paths are also blocked by default
- pending approvals and saved session approval rules now persist a broker-assigned risk level so the desktop UI can distinguish low-risk git inspection from high-risk push, permission, MCP, and protected-path activity
- approval-related observed actions now preserve category/title metadata into broker state so pending approvals are no longer flattened to a generic transport label
- interactive Codex approvals now also emit category-specific audit events and surface human-friendly approval titles in the desktop UI
- session approval rules for `git push` and `gh pr create` are now scoped by parsed remote/branch and head/base metadata, reducing the blast radius of `Approve Session`

Required behavior:

1. keep routing supported approval request methods through the desktop app
2. detect which request methods represent approval or blocked execution
3. enqueue or surface an approval item
4. block the active turn while approval is pending
5. after operator action, send the structured JSON-RPC response back to Codex

Important constraint:

- this phase only needs one supported approval path
- it is acceptable to reject unknown server requests while still handling approval requests properly

### A4. Claude Approval Baseline

Claude approval parity is likely harder than Codex approval parity.

Phase A requirement is not full parity yet.

Phase A must answer this question with evidence:

> Can `claude -p --output-format stream-json` expose enough structured approval or blocked-tool state for broker routing?

If yes:

- implement the same queue-and-pause pattern used for Codex

If no:

- record this clearly
- keep Claude in a policy-limited mode for now
- open a follow-up track for Agent SDK evaluation

This is a product decision gate, not an open-ended research note.

### A5. Git Safety Defaults

Git must stop being an undifferentiated shell command.

Phase A policy defaults:

- `git status` / `git diff` / `git log` -> allow
- `git add` -> ask
- `git commit` -> ask
- `git push` -> ask
- `gh pr create` -> ask
- `git push --force` -> deny
- `git reset --hard` -> deny

Phase A does not require full wrappers yet, but it does require:

- command classification
- event logging
- approval category assignment

### A6. UI Surfaces

Phase A UI additions:

1. pending approval panel
2. current approval detail area
3. approve / approve-for-session / deny buttons
4. recent action events section
5. saved session approval visibility

Minimum operator visibility:

- which side requested the action
- what category it belongs to
- the raw command or protocol request preview
- whether the relay is paused waiting for approval

## Logging Contract

Every approval-related state change must be logged.

Required events:

- `approval.requested`
- `approval.granted`
- `approval.denied`
- `approval.expired`

Recommended payload rules:

- include raw request payload where safe
- include summarized command text in `Message`
- keep secrets redacted if needed later, but Phase A can start with raw logging for local prototype use

## Acceptance Criteria

Phase A is complete when all of the following are true:

1. broker receives action events from at least:
   - Codex exec
   - Codex app-server
   - Claude stream-json
2. those events are written into the session JSONL log
3. Codex approval requests no longer disappear into `Not Implemented`
4. a pending approval visibly pauses the relay in the desktop UI
5. operator can approve or deny the pending request
6. dangerous git actions are not silently executed without classification

## Non-Goals

These are intentionally deferred:

- perfect command parsing
- exhaustive MCP policy control
- full Claude approval parity if the headless transport cannot expose it cleanly
- semantic diff previews
- full git/PR wrappers

## Next Step After Phase A

If Phase A lands successfully, the next phase should be:

1. git wrapper layer
2. MCP-aware action logging
3. bounded prompt contract relaxation toward marker-based handoff extraction
4. broker-owned continuity
