# Claude Approval Decision

## Current Decision

The current product path keeps Claude on the existing `claude -p --output-format stream-json` runtime and treats Claude approval as **audit-only**, not broker-routed approval.

In practical terms:

- Codex interactive approval is broker-routed today.
- Claude interactive approval is **not** broker-routed today.
- Claude tool use and permission denials are still logged into broker-visible audit events.

## Why

The current relay prototype already has a working approval round-trip for Codex through `codex app-server` server requests.

Claude is different:

- `stream-json` gives useful structured output for tool activity and permission denials.
- the current relay does **not** have a proven structured callback surface for turning a Claude tool request into a live broker approval prompt and then returning that decision back into the same turn
- permission modes and allowed-tool policy can still constrain Claude, but that is different from a broker-owned approval queue

Because of that, the current product stance is:

1. keep Claude on `stream-json` for now
2. log tool activity and permission denials as first-class broker events
3. do not pretend that Claude already has the same approval UX as Codex

## Product Meaning

This means the relay currently has asymmetric approval behavior:

- **Codex**: broker-routed approval with operator buttons, session approval rules, and dangerous auto-approve mode
- **Claude**: audit-first behavior with structured tool activity and permission-denial logging

That asymmetry is acceptable in the current prototype as long as it is visible to the operator.

## Next Gate

The next product decision is binary:

### Option A: Stay on `stream-json`

Keep Claude on the current runtime if:

- audit visibility is good enough
- broker-routed approval is not strictly required for Claude yet
- the team prefers lower implementation complexity

### Option B: Add Agent SDK path

Evaluate a separate Claude runtime path if the product requires:

- broker-routed approval parity with Codex
- programmable tool approval callbacks
- stronger policy ownership in the broker

If this path is explored, it should be treated as a separate product option and reviewed against current Claude documentation and account/auth constraints at implementation time.

## Current Rule

Until that gate is revisited, the product must describe Claude approval as:

> audit-visible, but not broker-routed
