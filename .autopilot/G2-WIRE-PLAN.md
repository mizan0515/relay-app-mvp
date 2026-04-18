# G2 wire-into-broker plan (post-PR #28 merge)

`HandoffArtifactWriter` renders. Nothing calls it yet. This plan closes the
remaining distance to G2 `[~]` → `[x]`.

## Two handoff systems coexist

The broker already has a **`HandoffEnvelope`** JSON path
(`CodexClaudeRelay.Core/Protocol/HandoffJson.cs` + `HandoffParser.cs`) that
adapters emit between turns. That is the **in-process relay envelope** —
machine-to-machine.

`HandoffArtifactWriter` produces the **human-bridged peer prompt** — the
markdown document saved to `handoff.prompt_artifact` per PACKET-SCHEMA.md.

They are not substitutes. G2 says "when a turn closes with
`handoff.closeout_kind: peer_handoff`, the broker saves turn-{N}-handoff.md".
So the wiring is **additive**: on every `peer_handoff` closeout, also emit
the artifact. JSON envelope stays for machine relay.

## Wiring surface (est. ≤50 LOC in Core)

1. **Translate**: add `TurnPacket FromHandoffEnvelope(HandoffEnvelope env, string from, int turn, string sessionId)` in
   `CodexClaudeRelay.Core/Protocol/TurnPacketAdapter.cs` (new file). Pure mapping.
2. **Persist**: add `HandoffArtifactPersister.WriteAsync(TurnPacket, string outPath, CancellationToken)`
   in `Protocol/HandoffArtifactPersister.cs` (new file). Creates parent dir
   if absent; writes rendered string atomically (temp file → rename).
3. **Invoke**: in `RelayBroker.CompleteHandoffAsync`, after the existing
   `AcceptedRelayKeys` dedup succeeds, compute expected artifact path
   (`Document/dialogue/sessions/{sid}/turn-{N}-handoff.md`), call persister,
   emit `handoff_written` log event with `{session_id, turn, path, bytes}`.

## Evidence for G2 `[x]`

- `handoff_written` log line in `logs/*.jsonl` from a real broker run.
- Matching file on disk at the expected path.
- New xunit spec in `Core.Tests` that constructs a `HandoffEnvelope`,
  drives `CompleteHandoffAsync`, asserts the file exists + content contains
  the 7 required parts.

## Out of scope

- Replacing JSON envelope with YAML packet — that is G1 (PacketIO); still
  operator-blocked.
- Backlog admission on final_no_handoff — G7.
- Any changes to protected_paths (contract docs / tools / .prompts).

## Dependencies

None external. Depends on PR #28 landing (TurnPacket + Writer).

## Risk

- `RelayBroker.CompleteHandoffAsync` is on the hot path. A write failure
  must log + continue, not fail the handoff. Wrap persister call in
  try/catch; emit `handoff_write_failed` on exception.
