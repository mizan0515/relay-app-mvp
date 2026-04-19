# ADR G — recovery_resume 턴의 패킷/핸드오프 산출물 누락

- **발견 시점:** iter140 (2026-04-19), `examples/real-project-sim` 8턴 구동 중
- **상태:** 🟠 관측 확정, 설계 결정 대기

## 관측된 사실

`examples/real-project-sim/RealProjectSim/Program.cs` 를 통해 브로커를 8턴 돌렸을 때, **턴 5 (`CloseoutKind.RecoveryResume`)** 에 대해:

- ✅ `session.recovery_resume` 이벤트는 정상 emit (`RelayBroker.cs:751`)
- ✅ `checkpoint.verified` 이벤트 emit (writer 진입 직후, `RelayBroker.cs:838`)
- ❌ `turn-5.yaml` **패킷 미생성**
- ❌ `turn-5-handoff.md` **미생성**
- ❌ `state.json` **해당 턴 스냅샷 미기록**
- 📝 `handoff_write_failed` 이벤트 1건 (감사 로그 노이즈):
  > `HandoffArtifactWriter only renders peer_handoff closeouts; got 'recovery_resume'`

## 원인

`RelayBroker.cs:788` 이 모든 턴에서 `WriteHandoffArtifactAsync` 호출 →
동 메서드 `833~886` 가 `HandoffArtifactPersister.WriteAsync` 를 먼저 실행 →
작성기가 `peer_handoff` 외 closeout 에 대해 `InvalidOperationException` → `catch` 로 폴백되며 **YAML 패킷/상태 write 도 같이 건너뜀** (같은 try 블록 내 순차 실행).

`final_no_handoff` 도 같은 경로일 것으로 추정되나, 기존 `deep-integration` 에서는 턴 4 가 final 이라 "설계" 로 간주돼 이슈 제기되지 않음.

## 3 안

### 안 A (권고) — recovery_resume 에서 writer 스킵
- `RelayBroker.cs:788` 전에 closeout 검사 → `peer_handoff` 만 아트팩트 write.
- 장점: 최소 diff, 에러 이벤트 제거.
- 단점: recovery 턴이 디스크에 물리적 흔적 없음 (이벤트 로그로만 추적).
- 예상 변경: ~3 LOC.

### 안 B — YAML 패킷은 항상, handoff.md 는 peer_handoff 만
- `WriteHandoffArtifactAsync` 분해: `WriteTurnPacketAsync` + `WriteHandoffMarkdownAsync`.
- recovery/final 에서도 `turn-N.yaml` + `state.json` 은 기록.
- 장점: 세션 재생(replay) 시 recovery 턴까지 완전 복원.
- 단점: 중규모 리팩터, 테스트 가드 필요.

### 안 C — writer 확장 (recovery preamble 렌더)
- `HandoffArtifactWriter` 에 recovery_resume 렌더 경로 추가.
- 장점: 산출물 완전성 최대화.
- 단점: 렌더 스펙 결정 필요 (템플릿 `PACKET-SCHEMA.md` 확장 필요 가능성).

## 권고

**안 A 먼저, 안 B 후속** 2단 착륙.

1. 안 A 로 즉시 노이즈 이벤트 제거 (작은 PR).
2. 필요 시 안 B 로 물리적 기록 보강 (테스트 포함 중규모 PR).

안 C 는 템플릿 스펙 확정 후로 보류.

## 관리자 결정 양식

이 파일 하단에 한 줄:

```
OPERATOR: g = a|b|c
```

또는 "2단계: a → b" 같은 조합도 가능.

## 참고

- 재현: `dotnet run --project examples/real-project-sim/RealProjectSim/`
  (턴 5 로그에서 `handoff_write_failed` 확인)
- `examples/real-project-sim/` 은 git 추적 대상 아님 (session-workspace/ 동일). 필요 시 코드만 커밋하는 별도 PR 로 예시 자체를 착륙 가능.
