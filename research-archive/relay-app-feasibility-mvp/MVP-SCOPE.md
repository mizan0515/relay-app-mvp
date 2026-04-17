# MVP Scope

Date: 2026-04-15

## Core promise

The MVP must let one person run one mostly automatic relay session on Windows without manually copy-pasting every turn.

## In scope

### Session model

- exactly 1 active relay session at a time
- exactly 2 sides: `codex` and `claude`
- local machine only

### Broker behavior

- create or resume session
- track active side
- track turn number
- parse strict handoff JSON
- relay accepted handoff exactly once
- detect missing or invalid handoff
- inject repair prompt
- stop after repair budget is exceeded

### User interface

- start session
- pause session
- stop session
- view current side
- view latest parsed handoff
- view last error
- click to retry or take over manually

### Persistence

- lightweight local persistence of session state
- append-only per-session event log

### Adapters

- Codex adapter using native machine-readable surface where available
- Claude adapter using CLI JSON or stream-json plus resume

## Explicitly out of scope

- multiple simultaneous sessions
- cross-machine sync
- cloud backend
- RBAC or shared operator workflows
- Store packaging
- enterprise installer
- OCR
- image-based clicking
- self-healing selector engine
- plugin ecosystem
- analytics dashboard

## MVP quality bar

The MVP is acceptable only if all of these are true:

- relay works end-to-end for one session
- handoff parsing is deterministic
- duplicate relay is blocked
- missing handoff triggers repair automatically
- repair failure pauses instead of looping forever
- app restart does not lose the current session record

## Hard constraints

### 1. No heuristic handoff detection

Only a valid JSON handoff object is accepted.

### 2. No mandatory UI automation in the core path

If the core relay only works when visible desktop windows are focused, the MVP is too brittle.

### 3. No silent auto-approval of risky actions

If an adapter reports an approval gate, the app must pause and tell the user.

## Preferred technical cut

- UI shell: WPF
- broker: .NET background worker or sibling process
- persistence: SQLite or JSONL + tiny state file
- Codex adapter: app-server if practical, otherwise machine-readable fallback
- Claude adapter: `claude` CLI JSON/stream-json + resume

## Minimum operator workflow

1. Open the app.
2. Choose or create one relay session.
3. App starts both adapters.
4. One side runs.
5. App detects final handoff.
6. App submits it to the other side.
7. Repeat until paused or stopped.
8. If handoff is missing, app performs repair.
9. If repair fails twice, app pauses and asks for human action.
