# ADR — 모순 F · `.githooks/protect.sh` 가 `MISSION-AMEND:` trailer 를 존중하지 않음

- **Status**: Proposed (operator decision pending)
- **Date**: 2026-04-19
- **Context owner**: autopilot
- **Trigger**: PR #60 리뷰 중 발견 (IMMUTABLE `mission` 블록 수정 경로가 문서상 존재하나 훅이 차단)

## 1. 배경 한 줄

`.autopilot/PROMPT.md:53-55` 는 *"기존 IMMUTABLE 블록을 고치려면
`MISSION-AMEND:` 트레일러가 있어야 한다"* 로 기술하지만, 실제
`.githooks/protect.sh:135-142` 는 트레일러 존재 여부와 **무관하게**
모든 IMMUTABLE 블록 수정을 거부한다. 즉 현재는 **정문이 닫혀
있고 사양은 정문 열쇠가 존재한다고 주장** 하는 상태.

## 2. 왜 이 상태가 됐나

`protect.sh` 는 pre-commit 훅으로 커밋 메시지를 **보지 못한다** —
트레일러는 commit-msg 단계에만 안정적으로 존재함. IMMUTABLE 블록 변경
비교 로직이 pre-commit 에 있으므로 트레일러를 조회할 수단이 없음.
`commit-msg-protect.sh` 는 트레일러를 본다(Gate A 가 이미 IMMUTABLE-ADD
트레일러를 읽음).

## 3. 영향도

- **실질 영향**: MVP 시점까지 `mission` 블록 수정 요구가 0건이어서
  아무도 막히지 않음. PR #60 도 새 블록 `mission-clarification` 을
  추가(IMMUTABLE-ADD 경로)해 우회함.
- **잠재 영향**: 운영자가 향후 기존 IMMUTABLE 블록을 정당한 이유로
  수정하려 할 때, 문서대로 `MISSION-AMEND:` 를 써도 거부됨 → 혼란.
- **드리프트 영향**: 문서와 훅이 어긋나면 자율 로봇이 문서만 믿고
  헛된 PR 을 만들 위험.

## 4. 선택지

### Option A — 훅을 사양에 맞춘다 (기능 보강)

- 구현: IMMUTABLE 블록 diff 판정을 `protect.sh` → `commit-msg-protect.sh`
  로 이관. `commit-msg-protect.sh` 는 트레일러를 읽을 수 있으므로
  `MISSION-AMEND: <block>` 존재 시 허용.
- 장점: 문서와 훅이 일치. 운영자 경로 실현.
- 단점: `.githooks/` 는 protected_paths — 관리자 한국어 리뷰 1회 필수.
  단위 시험 없는 쉘 스크립트라서 리뷰 부담이 비교적 큼.
- 비용: 1 iter, PR 1건, 훅 양쪽 약 30 LOC 이동.

### Option B — 사양을 훅에 맞춘다 (현실 반영)

- 구현: `.autopilot/PROMPT.md:53-55` 의 "MISSION-AMEND 로 수정 가능"
  문구를 "IMMUTABLE 블록은 append-only 이며 IMMUTABLE-ADD 로만 확장"
  으로 바꿈. 기존 블록 수정은 **금지** 로 공식화.
- 장점: 훅이 이미 enforces 하는 정책을 문서가 따라감. 변경 최소.
- 단점: 운영자가 진짜로 기존 mission 을 고쳐야 할 때 경로가 없어짐
  → 블록 전체 폐기 후 새 블록 추가라는 우회만 남음.
- 비용: 1 iter, PR 1건, PROMPT.md 몇 줄. **PROMPT.md 가 protected 이라
  IMMUTABLE-ADD/MISSION-AMEND 중 하나의 trailer 가 필요**. 여기선
  `core-contract` 블록(규칙 정의)이 수정 대상이라 Option B 자체가
  자기모순(고치려면 `MISSION-AMEND` 필요하지만 그게 없다). 실질적으로
  **mutable 영역에만 문서를 재배치**하는 형태가 현실적.

### Option C — 현상 유지, 주석만 추가

- 구현: `protect.sh` 에 "MISSION-AMEND 는 현재 미지원 — 기존 블록
  수정을 원하면 운영자가 훅을 임시 비활성하고 직접 커밋" 주석만 추가.
- 장점: 실질 변경 0. 리스크 0.
- 단점: 드리프트 지속. 문서와 훅의 불일치가 기록만 됨.
- 비용: 0 iter (또는 주석 1 PR).

## 5. 권고

**Option B 를 기본 권고** — 단, 문서 재배치 형태로.

근거:
1. MVP 기간 `MISSION-AMEND` 사용 사례 0건. 실제 수요 없음.
2. Option A 의 훅 로직 이관은 **보호 경로 코드 변경** 이라 리스크·리뷰
   비용이 수요 대비 높음.
3. "IMMUTABLE 은 진짜 IMMUTABLE 이다 (append-only)" 로 단순화하면
   자율 로봇의 안전한 휴리스틱과 일치.
4. 기존 블록을 고쳐야 할 극히 드문 상황에는 운영자가 훅을 수동
   비활성하고 직접 개입하는 것이 **오히려 적절한 차단점** (self-
   evolution 이 스스로 charter 를 고치지 못하게).

Option B 의 구체적 착지안: `PROMPT.md` 의 core-contract 블록은 **손대지
않고**, 그 아래 mutable 섹션에 *"실무 메모: 기존 IMMUTABLE 블록 수정은
운영자 수동 개입만 허용, MISSION-AMEND 트레일러는 예약어로만 남김"*
을 추가. 기존 블록 차단 규칙과 트레일러 문구 두 곳의 충돌이 사라짐.

## 6. 응답 양식 (관리자님)

이 파일이나 PR 댓글에 한 줄:

```
OPERATOR: f = a | b | c
```

- `a` = 훅을 고쳐 트레일러 지원 (Option A)
- `b` = 문서를 현실에 맞춰 재배치 (권고)
- `c` = 현상 유지, 주석만

승인 도착 즉시 로봇이 해당 Option 으로 이동.

---

*근거: `.autopilot/PROMPT.md:53-55` · `.githooks/protect.sh:135-142` ·
`.githooks/commit-msg-protect.sh:46-54` · PR #60 회고 (iter60+)*
