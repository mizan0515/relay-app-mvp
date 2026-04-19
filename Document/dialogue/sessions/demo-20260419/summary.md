# Rolling Summary — demo-20260419 peer-bridge smoke

**session**: `2026-04-19-demo-bridge-smoke`
**status**: closed · `final_no_handoff`
**turns**: 2 (codex → claude-code)

## Goal

Codex ↔ Claude Code peer-symmetric relay 가 실제로 동일한 `turn-{N}.yaml` 스키마로 턴을 주고받을 수 있음을 최소 증거로 착륙.

## Completed

- **turn-1 (codex)**: 세션 계약 + 2 체크포인트(`cp-1`: packet round-trip, `cp-2`: handoff 전달) + project_analysis + handoff prompt artifact.
- **turn-2 (claude-code)**: `cp-1` pass(PacketIOTests), `cp-2` pass(handoff.md 소비 실증), `final_no_handoff` 로 세션 봉인.
- **아티팩트**: `turn-1.yaml`, `turn-1-handoff.md`, `turn-2.yaml`, `state.json`, `summary.md` — MVP 게이트 G1·G4 증거 라인에 인용 가능.
- **검증**: `dotnet test` 96/96 그린 (PacketIOTests 2 facts 포함).

## Pending

- B8 — Headless smoke harness (실제 두 CLI 를 자식 프로세스로 연결하고 이 데모와 같은 세션을 자동 생성). 현 어댑터 `ClaudeCliAdapter`/`CodexCliAdapter` 가 있으나 end-to-end runner 없음.
- 포스트-MVP — MCP 지원(백로그 B17 예상) 설계 3안 중 택1 대기.

## Constraints

- C# 코드 변경 0, 기존 테스트 96/96 불변.
- 외부 `D:\dad-v2-system-template` 는 **읽기 전용 스펙** — 이 릴레이가 해당 저장소를 읽거나 쓰지 않음.
- `self_iterations: 0` 유지(데모 간결성).
- 이 데모는 **사람이 직접 두 CLI 를 띄워 주고받는 상황의 기계적 아날로그** — 실제 CLI 실행은 operator 가 별도로 준비해야 함.

## 후속 세션 힌트

다음 세션이 "실제 CLI 실행" 쪽으로 갈 경우 진입점:
- `examples/demo-session-dad-v2/README.md`
- `CodexClaudeRelay.Desktop/Adapters/ClaudeCliAdapter.cs`
- `CodexClaudeRelay.Desktop/Adapters/CodexCliAdapter.cs`
