# Relay App Service Blueprint

Date: 2026-04-15

## Objective

Define a production-usable Windows product that automates relay between Codex and Claude while:

- preserving long-lived sessions
- enforcing a strict handoff protocol
- recovering from missing-handoff cases automatically
- surfacing approvals and failures safely
- remaining maintainable under desktop and API drift

This document assumes the product is local-first and Windows-first.

## First design decision

Do not build this as a classic Windows Service that tries to control desktop UI.

Microsoft documents that:

- services run in Session 0
- Session 0 does not support interactive desktop UI
- services cannot directly interact with user-session applications the way a foreground desktop app can

That means a usable relay product should be:

- a per-user desktop application
- plus optional helper/background processes inside the same user session
- plus optional non-UI broker daemons only for machine-readable adapters

Not:

- a LocalSystem service that tries to click visible apps

## Product form

The real product should have three runtime components.

### 1. Desktop shell

Responsibilities:

- visible operator UI
- login/health visibility
- session dashboard
- approval queue
- recovery controls
- logs and diagnostics
- local settings

Technology:

- WinUI 3 or WPF

Recommendation:

- WinUI 3 if you want a modern packaged app and better long-term Windows integration
- WPF if your team values faster delivery and mature tooling more than visual modernization

### 2. Broker engine

Responsibilities:

- session orchestration
- state machine
- adapter supervision
- event normalization
- handoff validation
- repair loops
- duplicate suppression
- durable persistence
- upgrade-safe replay and recovery

Technology:

- .NET background worker hosted inside the desktop app process or a sibling broker process

Recommendation:

- separate broker process for fault isolation

Reason:

- a crashed UI should not kill live relay sessions
- a restarted UI should be able to reconnect to the broker state

### 3. Adapter workers

Responsibilities:

- speak Codex protocol
- speak Claude protocol
- optionally automate visible windows when required

Recommendation:

- one worker per active agent session
- isolate workers from the main broker via typed IPC

## Target architecture

```text
+---------------------+
| Desktop Shell       |
| - session UI        |
| - approvals         |
| - logs              |
+----------+----------+
           |
           | IPC
           v
+---------------------+
| Broker Engine       |
| - state machine     |
| - persistence       |
| - repair policy     |
| - dedupe            |
| - metrics           |
+----+-----------+----+
     |           |
     |           |
     v           v
+---------+   +---------+
| Codex   |   | Claude  |
| Adapter |   | Adapter |
+----+----+   +----+----+
     |             |
     |             |
     v             v
 codex app-     claude CLI
 server /       json / stream-json /
 exec json      hooks / resume
```

Optional:

```text
+---------------------+
| UI Fallback Worker  |
| - UIA selectors     |
| - window focus      |
| - selector repair   |
+---------------------+
```

## Production principles

### 1. Native automation first

Use protocol surfaces before UI automation.

Priority order:

1. native machine-readable protocol
2. local CLI JSON stream
3. local hook/event surface
4. UI Automation selectors
5. OCR or image fallback

### 2. Broker is the source of truth

Neither visible desktop app should be trusted as the source of workflow truth.

The broker must own:

- authoritative session state
- accepted handoff hashes
- completion status
- retry counts
- approval state
- crash recovery metadata

### 3. No handoff inference

The broker only accepts handoff when it matches the explicit schema.

Never infer handoff from:

- "looks like a prompt"
- "contains Read PROJECT-RULES.md"
- "has bullet points"

### 4. Replayability

Every important decision must be replayable from disk:

- incoming prompt
- streamed assistant output
- accepted final output
- parsed handoff
- repair prompts
- approval events
- adapter errors

### 5. Human interruptibility

Production software must assume that users will interrupt, close, switch, or override at any time.

The relay must survive:

- desktop shell restarts
- adapter worker crashes
- suspended laptops
- agent-side auth expiration
- visible desktop window closure

## Required capabilities for a usable service

### Session management

Must support:

- create session
- resume session
- stop session
- pause auto-relay
- fork session
- export session transcript
- recover after crash

Should support later:

- multiple concurrent sessions
- queueing and prioritization

### Turn management

Must support:

- detect turn start
- detect turn completion
- detect waiting-for-approval
- detect waiting-for-human
- detect machine-readable handoff
- detect missing handoff
- inject repair prompt
- cap repair attempts

### Reliability controls

Must support:

- timeouts per turn
- backoff after adapter failures
- duplicate relay protection
- operator takeover
- safe stop

### Security controls

Must support:

- encrypted storage for local secrets where possible
- separation of auth material from transcript logs
- redaction of secrets in exported logs
- allow/deny lists for filesystem paths if UI automation is ever added

### Observability

Must support:

- per-session event log
- per-turn timeline
- adapter health view
- auth status view
- approval history
- exportable diagnostics bundle

## Production adapter strategy

### Codex adapter

Primary:

- `codex app-server`

Secondary:

- `codex exec --json`

Why:

- app-server is better for thread continuity, steering, and approvals
- exec JSON is a solid machine-readable fallback

Do not assume:

- Windows Codex hooks are available

### Claude adapter

Primary:

- `claude` CLI with JSON or stream-json plus resume

Enhancers:

- hooks for stop validation and approval automation
- remote-control only if it materially improves resume UX

Why:

- Claude currently offers stronger hook automation than Codex on Windows

### UI fallback adapter

Use only when needed for:

- opening the correct visible window
- spotlighting the active session for the operator
- reading or pasting in a desktop-only workflow mode

Do not make visible-window automation mandatory for core relay correctness.

## Storage design

Use SQLite for indexed operational state and JSONL for raw append-only event logs.

### SQLite

Tables:

- `sessions`
- `turns`
- `events_index`
- `handoffs`
- `approvals`
- `adapter_health`
- `settings`

Key fields:

- session id
- side
- agent-native session/thread id
- active state
- retry count
- accepted handoff hash
- created/updated timestamps

### JSONL event log

One file per session:

- append-only
- full raw events
- ideal for debugging and support

Why both:

- SQLite is good for product UX
- JSONL is good for forensic replay

## Packaging and deployment

For a real service product on Windows, packaging matters.

Recommended packaging choices:

- packaged desktop app with MSIX if you want clean install/update behavior
- AppInstaller or Store distribution if you want automatic updates

Microsoft documents that packaged WinUI 3 apps integrate best with clean install/update flows and that Store/AppInstaller support auto-updates, while raw MSI or setup.exe requires your own updater.

Recommendation:

- packaged app for v1 production if feasible
- unpackaged only if you have strong reasons tied to custom installers or enterprise deployment quirks

## Authentication model

Do not proxy model-provider credentials through your own server unless the product strategy requires that.

Local-first auth model:

- Codex auth remains owned by Codex tooling
- Claude auth remains owned by Claude tooling
- the relay app checks health and eligibility, not raw credential contents

The app should know:

- authenticated / not authenticated
- remote-control eligible / not eligible
- session resumable / not resumable

The app should not need:

- direct access to users' raw bearer tokens in ordinary flows

## Approval UX

Production relay will fail without explicit approval handling.

Required UI:

- queue of pending approvals
- source side
- exact reason
- allow once / deny once / pause auto-relay
- optional default policies

Never:

- silently auto-approve broad destructive actions

Claude hooks may allow more approval automation than Codex on Windows, but policy should still be explicit in the product UI.

## Failure model

Treat these as first-class failure categories:

### Category A: protocol failure

Examples:

- malformed handoff JSON
- missing required field
- wrong target
- session id mismatch

Action:

- broker rejects handoff
- inject repair prompt or stop

### Category B: adapter failure

Examples:

- process exited
- auth expired
- JSON stream broken
- server unavailable

Action:

- restart adapter if safe
- mark session degraded
- do not relay blindly

### Category C: semantic failure

Examples:

- repeated missing handoff
- repeated invalid handoff
- duplicate handoff
- approval requested but unresolved

Action:

- stop auto mode
- require operator review

### Category D: UI fallback failure

Examples:

- selector not found
- target window missing
- focus failure

Action:

- keep broker state alive
- mark only UI assist as degraded
- never lose session truth

## Multi-session policy

A real service should not begin with unconstrained parallelism.

Recommended release sequence:

- v1 production: one active auto-relay session at a time
- v1.5: multiple sessions, but one active turn per adapter process
- v2: scheduler with fairness, quotas, and operator priority

Reason:

- concurrency multiplies approval ambiguity
- desktop UX becomes confusing
- recovery logic becomes much harder

## Supportability requirements

To be production-usable, support must be designed in from the start.

Add:

- diagnostics export button
- "copy current session state" button
- last 200 events view
- adapter version report
- OS version and packaging mode report
- health check page

## Recommended release standard

Do not call it production-ready until it meets all of these:

- crash-safe restart
- no duplicate relay across restart
- deterministic missing-handoff repair
- durable logs and exported diagnostics
- approval deadlocks surfaced clearly
- update path documented
- operator can pause and take over within one click

## Final recommendation

Treat this as a desktop orchestration product, not a UI bot and not a Windows Service.

The correct production shape is:

- packaged Windows desktop app
- separate broker engine
- protocol-native adapters first
- UI automation only as a non-critical assist layer

That is the highest-probability path to a service that people can actually use every day without constant babysitting.
