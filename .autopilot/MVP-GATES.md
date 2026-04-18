# MVP-GATES — codex-claude-relay DAD-v2 automation scorecard

Gate count: 8

Each gate is an OBSERVABLE completion criterion for **automating the DAD-v2
peer-symmetric dialogue protocol in CLI environments**. A gate flips to `[x]`
only when pointed at a runnable log / packet file / validator output.
`[~]` = partial/in-progress. `[ ]` = not started or regressed.

Evidence formats accepted:
- `Document/dialogue/sessions/{id}/turn-{N}.yaml` packet pointer
- `Document/dialogue/sessions/{id}/turn-{N}-handoff.md` artifact
- `logs/*.jsonl` event line (quote ≤3 lines)
- `tools/Validate-*.ps1` output snippet (PASS/FAIL tail)
- `dotnet build` exit-code + warning count
- PR number that introduced the capability

Regression protocol: a `[x]` gate reverts to `[~]` or `[ ]` only with cited
evidence (commit sha + failing validator / build / missing file). Silent
reversion is a rule break.

---

## G1 — Peer-symmetric packet I/O
- [ ] Broker can read and write `turn-{N}.yaml` conforming to
      `Document/DAD/PACKET-SCHEMA.md` with fields: `type`, `from`,
      `contract`, `peer_review`, `my_work`, `handoff`. Must round-trip
      without loss for both `from: codex` and `from: claude-code`.
- Evidence: a live turn-1.yaml + turn-2.yaml pair that pass
  `tools/Validate-DadPacket.ps1` and have symmetric structure.

## G2 — Handoff artifact generation
- [x] When a turn closes with `handoff.closeout_kind: peer_handoff`, the
      broker saves `turn-{N}-handoff.md` at the path named in
      `handoff.prompt_artifact`, containing the 7-part DAD prompt
      (references, state, prev packet, next_task, summary, tail, paste).
- Evidence: a generated handoff.md file + matching `handoff_written` log line.

## G3 — Checkpoint PASS/FAIL collection
- [~] The broker parses `peer_review.checkpoint_results` from a turn packet
      and emits a `checkpoint.verified` event per result with fields
      `{checkpoint_id, status, evidence_ref}`. Missing evidence for a
      non-PASS result blocks the turn from closing.
- Evidence: logs/*.jsonl showing `checkpoint.verified` events for a real
  turn, plus a rejection log line when evidence is missing.
- 2026-04-18 — G3 `[ ]` → `[~]`. Evidence: PR #33 (commit 7f0ce82).
  `CheckpointVerifier.Verify` emits `checkpoint.verified` per result +
  `checkpoint.evidence_missing` when non-PASS lacks `evidence_ref`. xunit
  6/6 통과. Remaining for `[x]`: enforce actual block-turn-close semantic
  in broker handoff flow (currently event-only).

## G4 — One full peer round-trip automated
- [ ] Starting from a committed turn-1 (from Codex), the broker routes the
      handoff to Claude Code (or back to Codex), produces a turn-2 packet,
      and writes state.json `current_turn = 2`. No manual copy-paste.
- Evidence: session directory with turn-1/2 packets + handoffs + state.json
  showing progression, all created within one broker-driven session.

## G5 — recovery_resume protocol
- [ ] When a turn ends with `closeout_kind: recovery_resume` (context
      overflow), the broker loads `.prompts/04-session-recovery-resume.md`,
      injects `open_risks` + empty `prompt_artifact` handling, and the next
      agent continues without loss of `my_work` state.
- Evidence: a resume cycle — turn with recovery_resume closeout → successful
  turn-{N+1} with `my_work.continued_from_resume: true`.

## G6 — Rolling summary + carry-forward injection
- [ ] At session rotation (turn/time/token trigger), broker writes a
      markdown summary and injects Goal/Completed/Pending/Constraints into
      the next session's first turn prompt under a `## Carry-forward`
      section. `summary.generated` event carries bytes + path.
- Evidence: pre-/post-rotation prompt diff showing carry-forward block +
  matching `summary.generated` log line + file on disk.

## G7 — Consensus convergence closeout
- [ ] When both peers mark `handoff.ready_for_peer_verification: true` AND
      `handoff.suggest_done: true` in consecutive turns with matching
      `checkpoint_results`, the broker seals the session as `converged` in
      state.json and links it in `Document/dialogue/backlog.json`.
- Evidence: state.json showing `session_status: converged` + backlog entry
  with `closed_by_session_id` filled.

## G8 — Audit log integrity
- [ ] Every turn packet I/O, handoff artifact write, and state transition
      emits a JSONL event with canonical SHA-256 hash. Replay of the same
      handoff does NOT produce duplicate state transitions (dedup via
      `AcceptedRelayKeys`). Event log survives process crash.
- Evidence: logs/*.jsonl tail showing hash-stamped events for a turn +
  demonstration of dedup (same packet submitted twice → one transition).

---

## Flip history

(Append an ISO-timestamped line when a gate flips, with commit sha + evidence
pointer. Never delete history lines — they are the regression audit trail.)

- 2026-04-18 — scaffolded post-reset; all gates `[ ]`. Previous scorecard
  (Codex-only G1-G8 from pre-reset state) archived in
  `archive/codex-broker-phase-f` tag.
- 2026-04-18 — G2 `[ ]` → `[~]`. Evidence: PR #28
  (commits 18a2b01 + 7de9709). HandoffArtifactWriter renders the 7-part
  DAD prompt; 9 xunit specs pass; peer-symmetric codex↔claude.
- 2026-04-18 — G2 `[~]` → `[x]`. Evidence: PR #31 (commits 374507a +
  d2fa1c7 + 6970c43, merged 13:05:28Z). `RelayBroker.CompleteHandoffAsync`
  now invokes `HandoffArtifactPersister.WriteAsync` through
  `TurnPacketAdapter`, emitting `handoff_written` log event on success and
  `handoff_write_failed` on exception. Artifact lands at
  `Document/dialogue/sessions/{sid}/turn-{N}-handoff.md`.
  Core tests: 3 new + 9 existing = 12/12 green (565 ms).
