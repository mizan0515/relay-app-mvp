# turn-1-handoff — Codex → Claude Code

**session**: `2026-04-19-demo-bridge-smoke`
**from**: `codex` (turn 1)
**to**: `claude-code` (turn 2)

## 세션 목적

이 데모 세션은 `D:\codex-claude-relay` 릴레이가 실제로 두 피어 사이에서 동일한 `turn-{N}.yaml` 스키마로 턴을 주고받을 수 있다는 최소 증거를 남기기 위함. `D:\dad-v2-system-template` 의 DAD-v2 프로토콜 스펙(`ko/Document/DAD/PACKET-SCHEMA.md`)을 참조 기준으로 사용.

## Turn 1 요약 (Codex)

- 세션 계약 2개 체크포인트(`cp-1`, `cp-2`) 제안.
- 프로젝트 분석: 이 저장소는 템플릿 유지보수 리포지토리가 아닌 relay 런타임. 템플릿은 읽기 전용 스펙.
- 증거: `PacketIO` 라운드트립 테스트가 양쪽 `from` 값을 이미 증명.
- 위험: 실제 CLI 에 어댑터를 붙이는 harness 는 별도 백로그(B8).

## Turn 2 에서 해야 할 일 (Claude Code)

1. **cp-1 실행**: `dotnet test CodexClaudeRelay.sln --filter FullyQualifiedName~PacketIOTests` 결과를 `peer_review.checkpoint_results.cp-1` 에 기록.
2. **cp-2 실행**: 이 `turn-1-handoff.md` 파일이 turn-2 입력으로 실제로 소비되었음을 `peer_review.checkpoint_results.cp-2` 에 기록(evidence_ref: 이 파일 경로).
3. **세션 봉인**: `handoff.closeout_kind: final_no_handoff`, `suggest_done: true`, `done_reason` 명시.
4. **롤링 요약**: `summary.md` 에 Goal / Completed / Pending / Constraints 4섹션(carry-forward 포맷) 기록.
5. **state.json 갱신**: `status: closed`, `closed_at: 2026-04-19`, `final_closeout: final_no_handoff`.

## Carry-forward (다음 턴 주입 컨텍스트)

- **Goal**: peer-symmetric 릴레이 실증.
- **Completed**: turn-1 제안·체크포인트 정의·handoff 아티팩트.
- **Pending**: cp-1/cp-2 실행 + 세션 봉인.
- **Constraints**: self_iterations=0 유지(데모 간결성), 새 코드 변경 0, 기존 테스트 96/96 그린 유지.

## 참고 파일

- `Document/dialogue/sessions/demo-20260419/turn-1.yaml`
- `Document/dialogue/sessions/demo-20260419/state.json`
- `CodexClaudeRelay.Core/Protocol/PacketIO.cs`
- `CodexClaudeRelay.Core.Tests/Protocol/PacketIOTests.cs`
- `D:\dad-v2-system-template\ko\Document\DAD\PACKET-SCHEMA.md` (외부 스펙, 읽기 전용)
