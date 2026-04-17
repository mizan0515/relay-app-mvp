# MVP Roadmap

Date: 2026-04-15

## Phase 1

Build the smallest working broker.

Deliver:

- local session state model
- strict handoff JSON parser
- minimal broker loop
- fake adapters for test runs

Done when:

- broker can simulate turn relay end-to-end in tests

## Phase 2

Connect real adapters.

Deliver:

- Codex adapter
- Claude adapter
- one real end-to-end relay

Done when:

- one real session can relay at least two turns automatically

## Phase 3

Add recovery behavior.

Deliver:

- repair prompt path
- max repair budget
- duplicate suppression
- persisted event log

Done when:

- missing handoff is repaired automatically
- repeated failure pauses safely

## Phase 4

Add operator UI.

Deliver:

- session screen
- health indicators
- pause/resume/stop controls
- log access

Done when:

- the app is usable without reading raw logs constantly

## Phase 5

Stabilize.

Deliver:

- restart recovery
- adapter crash handling
- clearer error messages

Done when:

- restarting the app does not make the session unusable

## First implementation order

1. handoff schema parser
2. broker state model
3. fake adapter contract
4. real Claude adapter
5. real Codex adapter
6. repair flow
7. minimal WPF shell
8. persistence polish
