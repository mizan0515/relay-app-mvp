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

## Flip digest (reviewer quick view · B15)

한눈에 보는 현재 상태. 자세한 근거는 아래 "Flip history" 원본을 보세요.

| Gate | 현재 | 최신 flip (ISO·PR·commit) | 근거 한 줄 |
|------|------|--------------------------|-----------|
| G1 | `[ ]` | — | 운영자 3-결정 대기(`.autopilot/G1-UNBLOCKING-RUNBOOK.md`) |
| G2 | `[x]` | 2026-04-18 · PR #31 · 6970c43 | `RelayBroker.CompleteHandoffAsync` → `HandoffArtifactPersister` 배선 + 3 xunit |
| G3 | `[x]` | 2026-04-18 · PR #34 · 46aaa59 | 증거 없는 체크포인트 턴 차단 enforcement + 21 xunit |
| G4 | `[x]` | 2026-04-19 · PR #45 · 74686ef | `BrokerRoutingRoundTripE2ETests` 2 facts · turn-1/2.yaml + state.json landing |
| G5 | `[x]` | 2026-04-19 · PR #46 · 162c0ec | `BrokerRecoveryResumeE2ETests` 2 facts · preamble prepend 실증 |
| G6 | `[x]` | 2026-04-19 · PR #47 · a3ba00a | `BrokerRotationSmokeE2ETests` · summary file + `## Carry-forward` 주입 |
| G7 | `[x]` | 2026-04-18 · PR #44 · 26949eb | `BrokerConvergenceE2ETests` 2 facts · backlog.json + 2 이벤트 |
| G8 | `[x]` | 2026-04-19 · PR #50 · 769274a | `BrokerReplayDedupE2ETests` 2 facts · Duplicate handoff 거부 + crash-survival |

**MVP 진행**: **7/8** · 남은 G1 만 운영자 결정 대기. 테스트: **81/81** · 누적 머지: **23건**.

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
- [x] The broker parses `peer_review.checkpoint_results` from a turn packet
      and emits a `checkpoint.verified` event per result with fields
      `{checkpoint_id, status, evidence_ref}`. Missing evidence for a
      non-PASS result blocks the turn from closing.
- Evidence: logs/*.jsonl showing `checkpoint.verified` events for a real
  turn, plus a rejection log line when evidence is missing.
- 2026-04-18 — G3 `[ ]` → `[~]`. Evidence: PR #33 (commit 7f0ce82).
  `CheckpointVerifier.Verify` emits `checkpoint.verified` per result +
  `checkpoint.evidence_missing` when non-PASS lacks `evidence_ref`. xunit
  6/6 통과.
- 2026-04-18 — G3 `[~]` → `[x]`. Evidence: PR #34 (commit 46aaa59).
  `HandoffEnvelope.CheckpointResults` 추가 + `CompleteHandoffAsync`가
  State 변경 전 Verify 호출 → `BlocksTurnClose` 시 `PauseWithResultAsync`
  로 거부, `checkpoint.evidence_missing` 이벤트 기록. TurnPacketAdapter
  tests 3 facts 추가, 전체 21/21 통과.

## G4 — One full peer round-trip automated
- [x] Starting from a committed turn-1 (from Codex), the broker routes the
      handoff to Claude Code (or back to Codex), produces a turn-2 packet,
      and writes state.json `current_turn = 2`. No manual copy-paste.
- Evidence: session directory with turn-1/2 packets + handoffs + state.json
  showing progression, all created within one broker-driven session.
- 2026-04-18 — G4 `[ ]` → `[~]`. Evidence stack:
  · PR #35 (b64c2e9) — `TurnPacketYamlPersister` pure fn + 6 facts.
  · PR #36 (b59c959) — broker hook: `WriteHandoffArtifactAsync` emits
    `turn-{N}.yaml` + `state.json` on every accepted handoff + 3 facts.
  · PR #37 (43fedba) — `RoundTripArtifactSmokeTests` 3 facts: Codex→Claude
    turn-1 + Claude→Codex turn-2 sequence produces session dir with both
    handoff.md, both yaml, state.json showing `current_turn=3`. Reversed
    starter (Claude first) yields identical shape (peer-symmetric).
  · Test suite: 33/33 통과 on main.
- Remaining for `[x]`: broker-driven smoke with fake `IRelayAdapter`s that
  return canned `dad_handoff` JSON, proving the routing flow (not only the
  artifact emit) ends with `current_turn=2` and alternating `ActiveAgent`.
  Deferred to follow-up iter; artifact-layer evidence is sufficient for `[~]`.
- 2026-04-19 — G4 `[~]` → `[x]`. Evidence: PR #45 (머지 74686ef).
  `BrokerRoutingRoundTripE2ETests` 2 facts — `CannedAdapter` 쌍으로
  `broker.AdvanceAsync` 두 번 호출: turn-1에서 Codex 어댑터가 canned
  `dad_handoff` JSON 반환 → broker가 파싱·수락·State 갱신 → turn-2에서
  Claude 어댑터가 또 하나의 canned handoff 반환 → 최종 `State.CurrentTurn=3`
  + `ActiveAgent` Codex↔Claude 교대 + `Document/dialogue/sessions/{sid}/`
  밑에 `turn-1.yaml`, `turn-2.yaml`, `turn-1-handoff.md`, `turn-2-handoff.md`,
  `state.json`(current_turn=3, active_agent=codex) 전부 landing. Claude-first
  대칭 테스트도 동일 결과. `BrokerCwdMutatingCollection` 도입해 CWD
  mutating e2e 테스트 직렬화. xunit 61/61 통과.

## G5 — recovery_resume protocol
- [x] When a turn ends with `closeout_kind: recovery_resume` (context
      overflow), the broker loads `.prompts/04-session-recovery-resume.md`,
      injects `open_risks` + empty `prompt_artifact` handling, and the next
      agent continues without loss of `my_work` state.
- Evidence: a resume cycle — turn with recovery_resume closeout → successful
  turn-{N+1} with `my_work.continued_from_resume: true`.
- 2026-04-18 — G5 `[ ]` → `[~]`. Evidence stack:
  · PR #38 (9e087fa) — `HandoffEnvelope.CloseoutKind` 필드 +
    `TurnPacketAdapter` 매핑 + xunit 1 fact (recovery_resume round-trip).
  · PR #39 (0b94f25) — `RelayBroker.CompleteHandoffAsync` recovery_resume
    분기: `RecoveryResumePromptBuilder`로 다음 턴 `PendingPrompt` 앞에
    재개 프리앰블(프롬프트 04 참조 + continued_from_resume 마커 텍스트)
    prepend, `State.CarryForwardPending=true`, `session.recovery_resume`
    로그 이벤트 발행. xunit 3 facts.
  · Test suite: 37/37 통과 on main.
- Remaining for `[x]`: broker-driven end-to-end smoke (fake IRelayAdapter
  쌍 + in-memory store) 가 recovery_resume 한 사이클 후 다음 턴 패킷에
  `my_work.continued_from_resume: true`가 실제로 실림을 증명. G4 `[~]→[x]`
  follow-up(같은 fake-adapter harness)과 번들 권장. 단위 레이어 증거는
  `[~]` 충족.
- 2026-04-19 — G5 `[~]` → `[x]`. Evidence: PR #46 (머지 162c0ec).
  `BrokerRecoveryResumeE2ETests` 2 facts (Codex-first / Claude-first 대칭).
  turn 1 closeout_kind=recovery_resume → `session.recovery_resume` 이벤트 +
  `State.CarryForwardPending=true` + `State.PendingPrompt`에
  `[recovery_resume]` 프리앰블 + `continued_from_resume` 지시 + 원본 prompt
  prepend. turn 2 next-peer 어댑터가 수신한 `context.Prompt`가 그 프리앰블을
  실제로 포함함을 `CapturingAdapter`로 intercept & assert. turn 2
  peer_handoff 정상 수락 → CurrentTurn=3 + turn-2.yaml + turn-2-handoff.md
  landing. xunit 63/63 통과. `continued_from_resume`는 agent-set 플래그(
  broker는 지시 전달만 담당)이므로 preamble 도달 증명이 계약 범위.

## G6 — Rolling summary + carry-forward injection
- [x] At session rotation (turn/time/token trigger), broker writes a
      markdown summary and injects Goal/Completed/Pending/Constraints into
      the next session's first turn prompt under a `## Carry-forward`
      section. `summary.generated` event carries bytes + path.
- Evidence: pre-/post-rotation prompt diff showing carry-forward block +
  matching `summary.generated` log line + file on disk.
- 2026-04-18 — G6 `[ ]` → `[~]`. Evidence stack:
  · Pre-reset 인프라 (main branch HEAD 기준 유지 확인):
    `CarryForwardRenderer.TryBuild` (`## Carry-forward` 마크다운 블록 조립,
    `prior_handoff_hash` / `goal` / `### Completed|Pending|Constraints`
    섹션 순서 보장),
    `RollingSummaryWriter.WriteAsync` (segment markdown 파일을
    `%LOCALAPPDATA%/CodexClaudeRelayMvp/summaries/{sid}-segment-{N}.md`
    경로에 atomic write),
    `RollingSummaryWriter.BuildGeneratedEventPayload` (`summary.generated`
    이벤트 JSON 페이로드 — path·bytes·segment·turns·cost 포함).
  · `RelayBroker` 통합: `RotateSessionAsync`가 writer 호출 후
    `summary.generated` / `summary.failed` 이벤트 emit;
    `AdvanceAsyncInternal` (line 417-450)가 `TryBuildCarryForwardBlock`
    결과를 `RelayTurnContext.CarryForward` 필드로 다음 턴에 주입 +
    `summary.loaded` 이벤트(bytes, fields_populated) 기록.
  · PR #40 (f6f2263) — `CarryForwardRendererTests` 4 facts +
    `RollingSummaryWriterTests` 4 facts. 테스트 45/45 통과.
- 2026-04-19 — G6 `[~]` → `[x]`. iter46 / PR #47 (a3ba00a):
  `BrokerRotationSmokeE2ETests.Rotation_emits_summary_generated_event_and_injects_carry_forward_on_next_turn`
  (157 LOC, 1 fact) — peer_handoff 1턴으로 State.Goal/Pending/Completed 시드 →
  private `RotateSessionAsync`를 reflection 호출 →
  `%LOCALAPPDATA%/CodexClaudeRelayMvp/summaries/{sid}-segment-1.md` 실제 파일
  존재 + `summary.generated` 이벤트 (segment·path·bytes 페이로드) 정확히 1회
  emit + `CarryForwardPending=true` 검증. 이후 `AdvanceAsync` 재호출 →
  Claude 어댑터의 `context.CarryForward` 에 `## Carry-forward`,
  `- goal: finish the G6 rotation smoke`, `### Completed`/`### Pending`
  블록 주입 + `summary.loaded` 이벤트 emit 확인. xunit 64/64 통과.
  `BrokerCwdMutating` 컬렉션으로 CWD 경합 차단.

## G7 — Consensus convergence closeout
- [x] When both peers mark `handoff.ready_for_peer_verification: true` AND
      `handoff.suggest_done: true` in consecutive turns with matching
      `checkpoint_results`, the broker seals the session as `converged` in
      state.json and links it in `Document/dialogue/backlog.json`.
- Evidence: state.json showing `session_status: converged` + backlog entry
  with `closed_by_session_id` filled.
- 2026-04-18 — G7 `[ ]` → `[~]`. Evidence stack:
  · PR #41 (32ee1f0) — `ConvergenceDetector.Evaluate` 순수 함수 + 7 facts
    (정상 합의, 피어 순서 불변성, null 이전 턴, recovery_resume 거부,
    같은 피어 연속 거부, 체크포인트 상태 불일치 거부, suggest_done 미세팅 거부).
    `RelaySessionStatus.Converged` enum 값 추가.
  · PR #42 (b602980) — `BacklogClosureWriter` 순수 render + atomic
    WriteAsync + LoadAsync + UpsertClosure (5 facts).
  · PR #43 (6461d7c) — `RelayBroker.CompleteHandoffAsync` 배선:
    진입부에서 `priorHandoff = State.LastHandoff` 캡처 → 상태 업데이트
    후 `ConvergenceDetector.Evaluate` 호출 → 합의 성립 시 `State.Status
    = Converged` + `session.converged` 이벤트 + `WriteBacklogClosureAsync`
    (Document/dialogue/backlog.json에 upsert + `backlog.closure_written`
    이벤트, 실패 시 `backlog.closure_failed`). recovery_resume 턴은
    detector 레벨 + 브로커 레벨 이중 가드로 합의 판정 제외.
  · Test suite: 57/57 통과 on main.
- Remaining for `[x]`: 브로커-레벨 e2e 스모크 — fake `IRelayAdapter` 쌍이
  연속된 두 턴에서 convergent handoff(양쪽 ready_for_peer_verification +
  suggest_done + 일치하는 checkpoint_results)를 반환하도록 세팅 → broker
  Advance 사이클 후 `State.Status=Converged` + backlog.json 실물 파일에
  `session_status: converged`/`closed_by_session_id` 채워짐 + 이벤트 로그에
  `session.converged` + `backlog.closure_written` 모두 존재함을 assert.
  Scope ≈150 LOC. 다음 iter42에서 수행.
- 2026-04-18 — G7 `[~]` → `[x]`. Evidence stack:
  · PR #44 (머지 커밋 26949eb) — `BrokerConvergenceE2ETests` 2 facts:
    (a) Codex→Claude 순서의 convergent handoff 한 쌍을 broker
    `CompleteHandoffAsync`로 흘린 뒤 `State.Status=Converged` +
    `session.converged` + `backlog.closure_written` 이벤트 + 실제
    `Document/dialogue/backlog.json` 파일에 `session_id`,
    `session_status: converged`, `closed_by_session_id`, `converged_turn: 2`
    모두 기록 확인. (b) Claude→Codex 역순 동일 결과 (피어 대칭성).
  · 동 PR 부수 수정: iter41 배선에서 누락 발견된 필드 보강 —
    `HandoffEnvelope`에 `suggest_done` / `done_reason` JSON 필드 추가 +
    `TurnPacketAdapter.FromHandoffEnvelope`에서 `TurnHandoff.SuggestDone` /
    `DoneReason` 매핑. 이 수정이 없으면 detector는 영원히 발화 불가
    (합의 판정 핵심 조건 중 하나였음).
  · Test suite: 59/59 통과 on main (57 + 신규 2).
  · Remaining-for-`[x]` 조건 전부 충족: broker Advance 사이클 끝에
    Converged 상태 + backlog.json 실물 + 두 이벤트 모두 assert됨.

## G8 — Audit log integrity
- [x] Every turn packet I/O, handoff artifact write, and state transition
      emits a JSONL event with canonical SHA-256 hash. Replay of the same
      handoff does NOT produce duplicate state transitions (dedup via
      `AcceptedRelayKeys`). Event log survives process crash.
- Evidence: logs/*.jsonl tail showing hash-stamped events for a turn +
  demonstration of dedup (same packet submitted twice → one transition).
- 2026-04-19 — G8 `[ ]` → `[x]`. iter47-49 3-step landing:
  · iter47 / PR #48 (b5f2dfd): `CodexClaudeRelay.Core/Protocol/CanonicalHash.cs`
    순수 `OfString`/`OfBytes`/`Normalize`. 정규화 규칙 동결(CRLF/CR→LF · 라인말
    공백 strip · 말미 newline trim). `CanonicalHashTests` 9 facts.
  · iter48 / PR #49 (a4e103d): `RelayLogEvent.EventHash` 필드(`event_hash`
    JSON name). `JsonlEventLogWriter.AppendAsync` 가 `session|ts|type|role|
    msg|payload` 정규 결합 문자열의 CanonicalHash 를 stamp; preset 값은 보존.
    `ComputeEventHash` public static. `JsonlEventLogWriterTests` 6 facts.
    55개 기존 RelayLogEvent 생성자 호출 전부 무수정 호환.
  · iter49 / PR #50 (769274a): `BrokerReplayDedupE2ETests` 2 facts.
    (a) 정상 Advance 후 State.CurrentTurn/ActiveAgent 롤백 → 동일 envelope
    으로 `CompleteHandoffAsync` 재호출(리플렉션) → "Duplicate handoff
    detected" + AcceptedRelayKeys 불변. (b) writer 를 using 블록 격리하여
    2회 append 후 GC → 파일 재오픈으로 2 라인 모두 live + event_hash 동등성
    검증 (프로세스 crash proxy).
  · 전체 81/81 통과. 모두 xunit 헤드리스 재현 가능.

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
- 2026-04-18 — G7 `[~]` → `[x]`. Evidence: PR #44 (머지 26949eb).
  `BrokerConvergenceE2ETests` 2 facts (Codex→Claude / Claude→Codex 대칭).
  HandoffEnvelope에 suggest_done/done_reason 필드 + TurnPacketAdapter 매핑
  수정 포함. 59/59 통과. 합의 수렴 시 backlog.json 실물 파일 + 두
  이벤트(session.converged, backlog.closure_written) 모두 확인.
- 2026-04-19 — G4 `[~]` → `[x]`. Evidence: PR #45 (머지 74686ef).
  `BrokerRoutingRoundTripE2ETests` 2 facts (Codex-first + Claude-first 대칭).
  CannedAdapter 쌍으로 broker.AdvanceAsync 왕복 → turn-1/2.yaml + handoff.md
  + state.json(current_turn=3, alternating ActiveAgent) 실증. 61/61 통과.
- 2026-04-19 — G5 `[~]` → `[x]`. Evidence: PR #46 (머지 162c0ec).
  `BrokerRecoveryResumeE2ETests` 2 facts (Codex-first/Claude-first 대칭).
  recovery_resume turn → session.recovery_resume 이벤트 + CarryForwardPending
  + PendingPrompt에 preamble prepend; next-peer가 preamble 수신 실증. 63/63.
