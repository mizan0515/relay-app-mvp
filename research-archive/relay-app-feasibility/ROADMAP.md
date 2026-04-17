# Production Roadmap

Date: 2026-04-15

## Goal

Turn the relay concept into a production-usable Windows product, not just a prototype.

## Phase 0: Architectural spike

Deliverables:

- validate `codex app-server` connection lifecycle
- validate `claude` JSON or stream-json session lifecycle
- prove one end-to-end brokered relay without UI automation
- prove missing-handoff repair loop

Exit criteria:

- one stable local session
- one accepted handoff
- one successful repair
- durable event log

## Phase 1: Core broker alpha

Deliverables:

- broker engine process
- session persistence in SQLite + JSONL
- Codex adapter
- Claude adapter
- strict JSON handoff parser
- duplicate suppression
- simple desktop shell

Exit criteria:

- broker survives UI restart
- one-click session create/resume/stop
- no duplicate relay after restart
- approvals surfaced in UI

## Phase 2: Reliability beta

Deliverables:

- adapter health monitoring
- timeout and backoff policies
- recovery after auth expiry
- diagnostics export
- richer session timeline
- operator pause/takeover controls

Exit criteria:

- 24-hour soak with repeated turns
- broker recovers from intentional adapter crashes
- broker pauses safely on unresolved approvals
- support bundle is sufficient to diagnose failures

## Phase 3: Desktop assist layer

Deliverables:

- optional UI assist worker
- bring visible session window to front
- open or spotlight native session from broker context
- fallback paste/submit only where necessary

Exit criteria:

- service remains correct with UI assist disabled
- UI assist failures do not corrupt broker state

## Phase 4: Packaging and deployment

Deliverables:

- packaged Windows build
- installer/update strategy
- versioned configuration migration
- release notes and rollback plan

Exit criteria:

- clean install on fresh Windows user profile
- in-place upgrade keeps sessions and settings
- rollback procedure documented

## Phase 5: Team-grade features

Deliverables:

- multiple saved relay profiles
- policy templates
- import/export settings
- richer approval rules
- session archive browser

Exit criteria:

- real users can run daily workflows without engineering support

## Nice-to-have later

- remote monitoring view
- cross-machine resume
- visual selector repair UI
- plugin adapter model for additional coding agents

## Recommended implementation order

1. build the broker and adapters first
2. lock the handoff schema
3. add repair loop
4. add persistence and recovery
5. add desktop UI
6. add UI automation only if still needed

## Definition of production-ready

Call the product production-ready only when:

- core relay correctness does not depend on visible-window automation
- recovery after crash is deterministic
- approval handling is explicit and safe
- update/install path is stable
- diagnostics are strong enough for support
- the app runs for long-lived sessions without manual babysitting
