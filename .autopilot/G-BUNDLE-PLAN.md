# G4/G5/G6 `[~]→[x]` 번들 플랜 (iter43 작성)

## 배경

G7 완결(iter42) 시점에서 MVP 3/8. G4/G5/G6 모두 `[~]`이고
"Remaining for `[x]`" 조건이 **공통 패턴 — fake-adapter e2e 스모크**를
요구한다. G7의 `BrokerConvergenceE2ETests`가 그 패턴의 prior art.

## 공통 자산 (iter42에서 확보)

- `NoopAdapter : IRelayAdapter` (in-test, 역할 문자열만 보유)
- `InMemorySessionStore`, `InMemoryEventLog` (fake persistence)
- 리플렉션으로 `CompleteHandoffAsync` 직접 호출 패턴
- 임시 디렉토리 CWD 전환 → 실제 파일 landing 검증 → cleanup

→ 셋 다 `CodexClaudeRelay.Core.Tests/Broker/` 밑에 새 파일 1개씩으로
확장 가능. 공통 테스트 헬퍼 분리는 **하지 않는다** (3개 파일 각자
독립 + 명시적 setup이 읽기 더 쉬움; 본 프로젝트 CLAUDE.md의
"premature abstraction 금지" 원칙).

## Gate별 Remaining 명세 (각 iter 1개씩)

### iter44 — G4 `[~]→[x]` 브로커 routing e2e

**요구:** fake `IRelayAdapter` 쌍이 canned `dad_handoff` JSON을 반환 →
broker Advance 한 사이클 → `turn-1.yaml` + `turn-2.yaml` + 각
`turn-N-handoff.md` + `state.json` 생성 + `state.current_turn=3` +
`ActiveAgent`가 Codex↔Claude 교대.

**Scope:** `BrokerRoutingRoundTripE2ETests.cs` ~180 LOC, 2 facts
(Codex-first + Claude-first 대칭). `CannedAdapter`는 RunTurnAsync에서
미리 준비된 `RelayAdapterResult` (handoff JSON 문자열) 리턴.

**위험:** broker Advance 경로가 CompleteHandoffAsync를 내부적으로
호출하는지 / RunTurnAsync 결과를 어떻게 HandoffEnvelope로 파싱하는지
코드 경로 확인 필요. 필요 시 `RelayBroker.AdvanceAsync` 시그니처
읽어보고 조정.

### iter45 — G5 `[~]→[x]` recovery_resume 라운드트립 e2e

**요구:** 턴 1이 `closeout_kind: recovery_resume`으로 종료 →
턴 2 패킷의 `my_work.continued_from_resume: true` 확인.

**Scope:** `BrokerRecoveryResumeE2ETests.cs` ~150 LOC, 1-2 facts.
iter44에서 만든 CannedAdapter 재사용(복붙; 추상화 없이).

**위험:** `continued_from_resume` 필드가 실제 TurnPacket/PeerReview/
MyWork 중 어디에 붙는지 확인 필요. `TurnPacketAdapter`/`TurnPacket`
모델 재검토.

### iter46 — G6 `[~]→[x]` rolling summary rotation e2e

**요구:** 세션 rotate 트리거 → summary 파일 디스크 landing +
`summary.generated` 이벤트 emit + 다음 턴 prompt에 `## Carry-forward`
블록 prepend 확인.

**Scope:** `BrokerRotationSmokeE2ETests.cs` ~100 LOC, 1-2 facts.
`RotateSessionAsync` 직접 호출 → 파일 + 이벤트 assert.

**위험:** rotate trigger가 수동 호출 가능한지 / carry-forward 주입이
다음 RunTurnAsync context에 실제 실리는지 확인.

## 순서 정당화

G4 → G5 → G6 순서는 **선행 자산 축적** 순:
- G4가 CannedAdapter 패턴 확립
- G5가 G4 adapter를 recovery_resume 버전으로 복제
- G6가 rotation 트리거까지 얹음

## 리스크 / 중단 조건

만약 iter44에서 broker Advance 경로가 예상보다 복잡하면 (예: 어댑터
결과 파싱이 JSON 디시리얼라이즈 + validation 여러 단계) G4만 진행하고
G5/G6은 다음 스프린트로 분리. 번들이 강제는 아님.

## 예상 결과

성공 시 MVP **3/8 → 6/8** (iter43~46 4-iter 스프린트). G8만 남고
G1은 operator 차단 상태 유지.
