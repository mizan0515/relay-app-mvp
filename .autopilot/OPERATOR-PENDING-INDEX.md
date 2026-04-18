# 👋 관리자님 대기 목록 — 한 페이지 인덱스

> **이 파일 하나만 보시면 됩니다.** 로봇이 답변 대기 중인 모든 결정을 여기 모았습니다.
> 각 항목에 "어느 파일을 열면 되는지" + "무슨 한 줄 답변이 필요한지" 정리.
> 답변은 해당 파일 직접 수정 / PR 댓글 / 채팅 어디든 OK.

*작성: iter59 (2026-04-19) · 이후 결정 도착 시 로봇이 자동 갱신*

---

## ✅ 1. PR #51 머지 완료 (MVP-GATES.md Flip digest) — 감사합니다!

- **결과**: iter60(2026-04-19 02:30경) MERGED 확인. commit `f21b617` (main).
- **B15 DONE**. 남은 대기 항목은 아래 2·3·4.

---

## 🟠 2. G1 게이트 해제 — 3 결정 (validator / cost / 서두 정정)

- **문서**: `.autopilot/G1-UNBLOCKING-RUNBOOK.md` (iter51 작성)
- **결정 3개**: ① validator 포팅 (a/b/c) · ② Codex/Claude 비용 경로 (d/e/f) · ③ CLAUDE.md 서두 정정 승인/보류
- **관리자님 답변 형식**: 해당 파일 맨 아래 한 줄
  ```
  1: (a/b/c)
  2: (d/e/f)
  3: (승인/보류)
  ```
- **영향**: 답변 도착 즉시 G1 스프린트 착수(2~4 iter) → MVP **8/8** 완주 경로.

---

## 🟠 3. B14 — tools/ smoke wrapper 선택

- **문서**: `.autopilot/ADR-TOOLS-SMOKE-NEED.md` (iter52 작성)
- **선택지**: A(5 LOC `dotnet test` 래퍼 · 권고) · B(full PS harness) · C(드랍)
- **관리자님 답변 형식**: `B14: A/B/C` 한 줄
- **영향**: A 승인 시 iter53+ 에서 `tools/run-smoke.ps1` 착수(protected path, PR 필수).
  답변 미도착 → 일정 기간 후 Option C(드랍)로 간주.

---

## 🟡 4. B10 — blocked-state 규칙 배선 승인

- **문서**: `.autopilot/DECISION-BLOCKED-STATE.md` (iter58 작성)
- **결정 3개**: R-BLOCKED · R-REVIEW · R-DRIFT 각각 승인/수정요청/보류
- **관리자님 답변 형식**: 해당 파일 §6 양식 한 줄
- **영향**: 승인 도착 시 `.autopilot/PROMPT.md` mutable 섹션 PR 로 실제 배선.

---

## 🟢 참고 — 로봇이 스스로 처리 중

- **MVP 7/8 완료** (G2·G3·G4·G5·G6·G7·G8 `[x]`) · 테스트 81/81 · 누적 머지 23건.
- **idle-upkeep / 리뷰대기 병행 모드** 로 작은 개선 계속 진행 중 (iter54 이후):
  B15 (리뷰대기) · B13.1 regen script (DONE) · B10 spec (SPEC-LANDED) · 본 인덱스 (iter59).
- 관리자 답변 **없어도** 로봇은 계속 작은 개선을 자동 착륙시킵니다. 단, 위 4개가 해결돼야
  MVP 8/8 + 규칙 정식 배선이 가능.

---

*이 인덱스는 DECISION-BLOCKED-STATE.md R-REVIEW/R-BLOCKED 제안의 실질적 적용:
관리자 대기 항목을 한 곳에 모아 운영자 로딩 비용을 최소화.*
