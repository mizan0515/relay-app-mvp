# Relay State Machine

Date: 2026-04-15

## Goal

Define the broker state machine for a production-usable relay app.

The broker must distinguish:

- a turn that is still generating
- a turn that completed successfully
- a turn that completed without handoff
- a turn that is blocked on approval
- a broken adapter
- a UI assist failure that does not invalidate the session

## Top-level session states

### `Idle`

No active session.

### `Starting`

Broker is creating or attaching to native agent sessions.

### `Active`

One side is currently expected to produce the next turn.

### `WaitingApproval`

A native adapter reported an approval or permission gate.

### `Repairing`

The side completed, but handoff was missing or invalid, and the broker is running a repair pass.

### `Paused`

Human or policy paused auto-relay.

### `Degraded`

Core session still exists, but at least one adapter is unhealthy.

### `Stopped`

Session terminated normally.

### `Failed`

Session terminated due to unrecoverable error.

## Turn substates

Each side also has a turn substate:

- `Ready`
- `Submitting`
- `Streaming`
- `Completed`
- `ApprovalPending`
- `RepairPending`
- `RepairStreaming`
- `Error`

## State transitions

### Session lifecycle

```text
Idle
  -> Starting
  -> Active

Active
  -> WaitingApproval
  -> Repairing
  -> Paused
  -> Degraded
  -> Stopped
  -> Failed

WaitingApproval
  -> Active
  -> Paused
  -> Failed

Repairing
  -> Active
  -> Paused
  -> Failed

Degraded
  -> Active
  -> Failed

Paused
  -> Active
  -> Stopped
```

### Active side lifecycle

```text
Ready
  -> Submitting
Submitting
  -> Streaming
  -> ApprovalPending
  -> Error
Streaming
  -> Completed
  -> ApprovalPending
  -> Error
Completed
  -> Ready (other side)
  -> RepairPending
ApprovalPending
  -> Streaming
  -> Error
RepairPending
  -> RepairStreaming
RepairStreaming
  -> Completed
  -> Error
```

## Event model

The broker should normalize all adapter-specific signals into these events.

### Session events

- `SessionStartRequested`
- `SessionAttached`
- `SessionPaused`
- `SessionResumed`
- `SessionStopRequested`
- `SessionStopped`
- `SessionFailed`

### Turn events

- `TurnSubmitRequested`
- `TurnStarted`
- `AssistantDeltaReceived`
- `AssistantCompleted`
- `ApprovalRequested`
- `ApprovalResolved`
- `HandoffParsed`
- `HandoffRejected`
- `RepairRequested`
- `RepairBudgetExceeded`

### Adapter events

- `AdapterHealthy`
- `AdapterDegraded`
- `AdapterRecovered`
- `AdapterExited`
- `AuthExpired`

### UI assist events

- `WindowFocusFailed`
- `SelectorNotFound`
- `VisualSyncLost`

These should never directly corrupt core session state.

## Completion rules

Do not treat "no more streamed tokens right now" as completion.

A turn is only `Completed` when the native adapter confirms completion.

Examples:

- Codex adapter emits end-of-turn event
- Claude adapter emits final structured output record

If only UI automation is available in a fallback mode, completion must use a more conservative heuristic:

- explicit final-state UI marker
- stable idle period
- no pending approval indicator

That fallback mode should be marked lower confidence.

## Approval rules

On `ApprovalRequested`:

1. pause relay for that side
2. move session to `WaitingApproval`
3. show exact approval in UI
4. require explicit resolve action or policy decision

Never:

- auto-advance after an unresolved approval timeout

## Repair rules

On `AssistantCompleted`:

1. parse final output for handoff
2. if valid, accept and relay
3. if invalid or missing, emit `RepairRequested`

Repair loop:

- repair attempt count starts at 0
- max default attempts: 2
- each repair uses a narrow prompt
- accepted repair output must still pass normal handoff validation

If repair budget is exceeded:

- move session to `Paused` or `Failed` based on policy
- require human review

## Crash recovery rules

On broker restart:

1. load last persisted session state
2. inspect active adapters
3. query native session/thread state where possible
4. reconcile the last durable event

Rules:

- if a handoff was accepted but not yet relayed, relay exactly once
- if a handoff relay is uncertain, stop and request human review
- never guess that a relay already happened

## Duplicate suppression rules

Before relaying a handoff:

1. compute canonical handoff hash
2. check if `(session_id, target, turn, hash)` already exists
3. if yes, reject as duplicate

If the same `turn` produces a different valid handoff hash during repair:

- accept the newest valid handoff
- mark older one superseded if it was never relayed
- forbid replacement after a relay commit

## Relay commit point

The broker must define an exact point after which the target side is considered to have received the prompt.

Recommended commit rule:

- only mark relay committed after the target adapter acknowledges successful submission

Not:

- when the broker merely decides to send
- when UI focus succeeds
- when paste succeeds without submit confirmation

## Error policy

### Recoverable

- adapter child process crash
- temporary auth refresh needed
- missing handoff
- selector failure in optional UI assist

### Unrecoverable

- persistent malformed handoff after repair budget
- session identity mismatch
- duplicate submission uncertainty after crash
- data corruption in broker persistence

Unrecoverable errors should stop auto-relay and preserve diagnostics.

## Operational defaults

Recommended defaults:

- turn timeout: 30 minutes
- repair timeout: 5 minutes
- max repair attempts: 2
- adapter restart attempts: 3 with backoff
- duplicate relay tolerance: zero
- concurrent auto sessions: 1 in first production release

## Human takeover

At any non-Idle state, the operator should be able to:

- pause auto-relay
- resend last repair prompt
- mark current side done manually
- force stop the session
- export diagnostics

Manual actions must be persisted as first-class events.
