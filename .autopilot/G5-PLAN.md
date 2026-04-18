# G5 PLAN — recovery_resume protocol

Gate definition (MVP-GATES.md):
> When a turn ends with `closeout_kind: recovery_resume` (context overflow),
> the broker loads `.prompts/04-session-recovery-resume.md`, injects
> `open_risks` + empty `prompt_artifact` handling, and the next agent
> continues without loss of `my_work` state.
>
> Evidence: a resume cycle — turn with recovery_resume closeout →
> successful turn-{N+1} with `my_work.continued_from_resume: true`.

## What already exists (verified 2026-04-18 iter 31)

- `CloseoutKind.RecoveryResume = "recovery_resume"` constant
  (CodexClaudeRelay.Core/Models/TurnPacket.cs:64).
- `.prompts/04-session-recovery-resume.md` — resume procedure prompt.
- `TurnPacket.Handoff.CloseoutKind` field exists in packet model.
- Broker artifact write path (G4 step 2) that already emits `turn-{N}.yaml`
  + `state.json` per accepted handoff.

## What is missing for G5 `[~]` → `[x]`

1. **HandoffEnvelope.CloseoutKind field** — the wire envelope
   (HandoffEnvelope.cs) has no `closeout_kind` property. recovery_resume
   cannot travel from adapter output to broker without it.
2. **CompleteHandoffAsync branch on closeout_kind** — broker currently
   treats every handoff identically. On `recovery_resume`, it must:
   - NOT bump `CurrentTurn` in the usual peer-handoff way, OR bump it but
     also mark `State.CarryForwardPending = true` and tag the next
     outgoing prompt with the recovery-resume preamble.
   - Load `.prompts/04-session-recovery-resume.md` text and prepend /
     inject into the next turn's `PendingPrompt`.
   - Emit a `session.recovery_resume` log event with session id + turn N.
3. **my_work.continued_from_resume flag** — when the resumed agent writes
   its next packet, `my_work.continued_from_resume: true` must be set.
   Either adapter responsibility (packet author sets it) or broker
   convention (broker injects a marker that next agent copies). Pick the
   minimally-invasive path in step 4.
4. **End-to-end smoke test** — xunit fact that:
   - submits a turn-1 handoff with `closeout_kind: recovery_resume`,
   - asserts broker logs `session.recovery_resume`,
   - asserts next prompt contains the recovery-resume preamble,
   - runs turn-2 where the fake adapter writes `continued_from_resume=true`,
   - asserts final state reflects uninterrupted session continuity.

## iter execution order — STATUS

- **iter 31 DONE**: G5-PLAN.md 작성.
- **iter 32 DONE**: `HandoffEnvelope.closeout_kind` 필드 + `TurnPacketAdapter`
  매핑 + xunit 1 fact (PR #38, 9e087fa).
- **iter 33 DONE**: 브로커 `CompleteHandoffAsync` recovery_resume 분기 +
  `RecoveryResumePromptBuilder` (프리앰블 상수 + Compose) + xunit 3 facts
  (PR #39, 0b94f25). 37/37.
- **iter 34 DONE**: G5 `[ ]` → `[~]` (this iter). MVP-GATES.md 증거 스택
  기록. 단위 레이어 전 구간 검증 완료.

## Follow-up for G5 `[~]` → `[x]`

The broker branch is proven by unit tests, but an end-to-end smoke driving
the full advance cycle (session start → turn-1 with recovery_resume →
turn-2 with continued_from_resume marker) has not been written. The harness
required is the same as G4 `[x]` follow-up:

1. Fake `IRelayAdapter` pair whose `RunTurnAsync` returns a canned
   `dad_handoff` envelope with `closeout_kind: recovery_resume`.
2. In-memory `IRelaySessionStore` + `IEventLogWriter`.
3. Test harness: `StartSessionAsync` → `AdvanceAsync` (returns
   recovery_resume handoff) → assert `session.recovery_resume` event fired +
   next `PendingPrompt` starts with preamble + `CarryForwardPending=true`.
4. Second `AdvanceAsync` with fake adapter that writes a packet containing
   `my_work.continued_from_resume: true`, assert final state matches gate
   definition.

Scope ≈180 LOC. **Bundle with G4 `[x]` follow-up** — same fakes/harness can
serve both gates (just vary closeout_kind in the turn-1 handoff).
Schedule: next iter block once plan is approved, OR defer until G6/G7
sequencing decided.

## --- historical plan (obsolete, preserved for audit) ---

## iter execution order (target 3 iters)

- **iter 32**: extend `HandoffEnvelope` with `closeout_kind` (default
  `peer_handoff` for backward compat); update `TurnPacketAdapter.FromHandoffEnvelope`
  to carry it into `TurnPacket.Handoff.CloseoutKind`; xunit regression
  fact for round-trip. ≤40 LOC. Peer-symmetric (codex ↔ claude equal).
- **iter 33**: broker branch on `CloseoutKind.RecoveryResume` inside
  `CompleteHandoffAsync` — load prompt 04 from disk, compose next
  prompt, log `session.recovery_resume`, skip normal state bump
  semantics if design says so. ≤80 LOC. 2-3 xunit facts (normal
  peer_handoff unchanged + recovery_resume event fires + next prompt
  contains preamble).
- **iter 34**: full xunit smoke test proving resume cycle end-to-end
  with fake IRelayAdapter pair. Flip G5 `[ ]` → `[~]` first, then
  `[~]` → `[x]` once the smoke passes. ≤100 LOC.

## Peer-symmetry check

recovery_resume can originate from either Codex or Claude. No branch
that presumes only one role triggers context overflow (IMMUTABLE mission
rule 3). Test must cover both starters.

## Risk flags

- **State-bump semantics**: spec is ambiguous on whether recovery_resume
  advances `current_turn` or retries turn N. Re-read prompt 04 +
  PACKET-SCHEMA.md carefully in iter 33 before deciding. Record choice
  in MVP-GATES.md evidence trail.
- **Prompt 04 loading path**: repo uses both `.prompts/` (root) and
  `en/.prompts/` / `ko/.prompts/` (variants). Broker should read root
  `.prompts/` for language-neutral injection; variant selection is a
  separate concern.
- **G4 `[~]` → `[x]` interleave**: G5 plan does not block G4 follow-up.
  If fake-adapter smoke harness is built in iter 34, it can be reused
  for G4 `[x]` with minor variant. Consider bundling.
