# G4 PLAN — One full peer round-trip automated

Gate definition (from MVP-GATES.md):
> Starting from a committed turn-1 (from Codex), the broker routes the handoff
> to Claude Code (or back to Codex), produces a turn-2 packet, and writes
> state.json `current_turn = 2`. No manual copy-paste.
>
> Evidence: session directory with turn-1/2 packets + handoffs + state.json
> showing progression, all created within one broker-driven session.

## What already exists (verified 2026-04-18)

- `RelayBroker.AdvanceAsync` submits `RelayTurnContext` to the active
  adapter and receives a handoff (RelayBroker.cs:388).
- `CompleteHandoffAsync` parses envelope, mutates State (CurrentTurn++,
  ActiveAgent=target), and persists artifacts (RelayBroker.cs:626).
- `WriteHandoffArtifactAsync` writes turn-{N}-handoff.md per turn.
- Adapters registered per `AgentRole` (Codex, Claude). `State.ActiveAgent`
  alternates on each accepted handoff.
- G2 persister produces handoff.md; G3 verifier blocks turn close on
  missing evidence.

## What is missing for G4 [x]

1. **Turn packet persister** — handoff.md exists, but a separate
   `turn-{N}.yaml` (DAD PACKET-SCHEMA) is not written per turn. G4 evidence
   requires packet files, not just handoffs.
2. **state.json writer** — RelayState is serialized to the state store,
   but whether it lands on disk as `session/<sid>/state.json` with
   `current_turn = 2` after one full cycle needs confirmation.
3. **Integration smoke test** — no end-to-end test drives Codex→Claude→
   turn-2 with fake adapters. Need a fixture-driven xunit that:
   - registers two fake `IAgentAdapter` that return canned handoffs,
   - calls `StartSessionAsync` + `AdvanceAsync` + `AdvanceAsync`,
   - asserts session directory contains turn-1, turn-2 packets + handoffs,
     state.json shows current_turn=2 and alternating ActiveAgent.

## iter 27+ execution order — STATUS

- **iter 27 DONE**: `TurnPacketYamlPersister` pure fn + 6 facts (PR #35 b64c2e9).
- **iter 28 DONE**: broker hook + `SessionStatePersister` + 3 facts (PR #36 b59c959).
- **iter 29 DONE**: `RoundTripArtifactSmokeTests` 3 facts, artifact-layer
  round-trip + peer-symmetric check (PR #37 43fedba).
- **iter 30 DONE**: G4 `[ ]` → `[~]` (this iter). Artifact-layer evidence
  stack documented in MVP-GATES.md.

## Follow-up for G4 `[~]` → `[x]`

The artifact layer is proven. The remaining gap is that those artifacts
are currently emitted by direct persister calls in tests, not by driving
`RelayBroker` end-to-end. The full `[x]` needs:

1. **Fake `IRelayAdapter` pair** — codex-fake + claude-fake, each returns
   a canned `RelayAdapterResult` whose `Output` contains a valid
   `dad_handoff` JSON envelope parseable by `HandoffParser.TryParse`.
2. **In-memory `IRelaySessionStore` + `IEventLogWriter`** — state kept in
   dicts / files under a temp dir.
3. **Test harness** — construct `RelayBroker` with the fakes,
   `StartSessionAsync(codex, "prompt")`, `AdvanceAsync` x2, assert session
   dir contains turn-1/2 handoff.md + yaml + state.json with
   `current_turn=2` and alternating `ActiveAgent`.
4. **Peer-symmetric fact** — repeat with Claude as first role.

Scope ≈150 LOC (one test file + fakes). Best done as single PR with
careful budget. Schedule: next non-autopilot iter after G5/G6 sequencing
is decided, OR immediately if backlog stays empty.

## Peer-symmetry check

All four steps must treat Codex and Claude as interchangeable. Fake adapters
in the smoke test MUST be swappable — either order of turn-1 source (Codex
or Claude) must produce a valid round-trip. No branch "only one agent can
take" (IMMUTABLE:mission rule 3).

## Risk flags

- **Adapter contract**: if current `IAgentAdapter` API surface requires
  real process invocation, fake adapters may need a thin abstraction.
  Resolve in iter 27 before writing the persister.
- **State.json path convention**: check existing session store to ensure
  we write to the DAD-expected location, not a broker-internal one.
