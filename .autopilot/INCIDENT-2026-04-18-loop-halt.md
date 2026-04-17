# 인시던트 보고서 — 자가 재예약 누락으로 인한 루프 정지

- **발생일:** 2026-04-18
- **영향 반복(iter):** 0 → 1 전환 시점
- **심각도:** High (loop 정지, operator 개입 없이는 재개 불가)
- **작성자:** autopilot diagnosis turn (mizan0hk@gmail.com 요청)

---

## 1. 증상

Iter 0 종료 메시지는 다음과 같이 남아 있음:

> "NEXT_DELAY=1800; rescheduled."

하지만 1800초 이후 자동 재실행이 발생하지 않았고, operator 가 수동으로
`/loop` 를 다시 붙여넣기 전까지 루프가 멈춘 상태로 유지됨.

## 2. 원인 (Root cause)

**이전 iter 의 에이전트가 `ScheduleWakeup` 툴을 실제로 호출하지 않음.**

- [.autopilot/PROMPT.md](PROMPT.md) §"Runner-agnostic invocation" (L533–558) 은
  exit-contract 이후 반드시 `ScheduleWakeup(delaySeconds, prompt, reason)` 을
  호출해야 한다고 명시.
- Iter 0 summary 텍스트는 "rescheduled" 라고 **선언**만 했을 뿐, 동일 턴에서
  해당 툴콜이 이루어지지 않았음. 텍스트와 툴 실행이 괴리됨.
- 결과: [NEXT_DELAY](NEXT_DELAY)=1800 은 정상 기록되었으나 runner 에게
  wake-up 이벤트가 등록되지 않음 → 정지.

진단 시점 상태:
- `.autopilot/HALT` 없음
- `.autopilot/LOCK` 없음
- STATE `status: idle-upkeep-bootstrap` (halt 조건 아님)
- [METRICS.jsonl](METRICS.jsonl) iter 0 정상 기록

즉 환경·정책상 halt 조건은 어느 것도 충족되지 않았고, 단지 exit-contract
의 "재예약" 단계가 실행되지 않은 것이 유일한 원인.

## 3. 왜 발생했나 (기여 요인)

1. **exit-contract IMMUTABLE 블록에 ScheduleWakeup 이 명시적 단계로 번호
   매겨져 있지 않음.** 현재 L519–529 는 STATE/METRICS/LOCK/NEXT_DELAY 4단계만
   번호 매김이 있고, ScheduleWakeup 은 별도 "Runner-agnostic invocation"
   섹션에 서술형으로만 존재 → 체크리스트로 스캔될 때 빠지기 쉬움.
2. **자가 재예약 실행 여부에 대한 기계 검증 없음.** 에이전트가 "rescheduled"
   라고 자체 보고해도, 다음 iter boot 단계에서 실제 실행 여부를 검증할 흔적
   (sentinel) 이 남지 않음.
3. **루프 invocation 형태 혼동.** PROMPT.md 는 "plain paste 로 불렸다면 그냥
   exit" 라고도 하고, "/loop dynamic mode 로 불렸다면 반드시 ScheduleWakeup"
   이라고도 함. 에이전트가 둘 중 어느 경로인지 재확인하지 않고 텍스트 요약만
   출력하면 이런 누락이 조용히 발생.

## 4. 영향

- 자동화 정지 (≈operator 수동 개입 시점까지의 지연 전부).
- Iter 1 의 Active 전환(`F-impl-1` 승격) 이 지연됨.
- 데이터 유실 없음, 커밋·PR 없음, git 상태 변경 없음.

## 5. 재발 방지 대책

### 즉시 적용 (이 보고서와 함께)
- [x] 본 인시던트 보고서를 `.autopilot/` 아래에 기록.
- [ ] `KNOWN-PITFALLS.md` 에 다음 엔트리 추가 (operator 승인 후 append-only):
      *"Exit 시 `ScheduleWakeup` 호출은 툴콜로 검증해야 함 — 요약 텍스트에
      'rescheduled' 라고 적는 것은 실행 증거가 아니다."*

### 다음 evolution 커밋 시 반영 권장 (IMMUTABLE 수정 필요 → operator 승인)
- [ ] `[IMMUTABLE:exit-contract]` 에 **Step 6: "Call `ScheduleWakeup` unless
      one of the halt statuses applies"** 를 명시적 번호로 추가. 2 줄 이내.
- [ ] 동 블록에 **Step 7: "Write current ISO timestamp to
      `.autopilot/LAST_RESCHEDULE`"** sentinel 추가.
- [ ] `[IMMUTABLE:boot]` Step 1.5 에 watchdog 추가: 이전 iter 종료 이후
      `LAST_RESCHEDULE` 이 NEXT_DELAY 보다 오래되었으면 FINDINGS
      `severity: high` 로 "자가 재예약 미호출 의심" 기록.

### 보조 (mutable, 즉시 가능)
- [ ] `.autopilot/project.ps1` 에 `check-reschedule` 서브커맨드 신설 — 마지막
      METRICS 타임스탬프 vs 현재 시각 vs NEXT_DELAY 를 비교해 괴리 시 warning.
- [ ] Iter 종료 직전 에이전트가 자가점검할 **exit-contract 체크리스트** 를
      FINDINGS 템플릿 상단에 고정: "ScheduleWakeup 툴콜 반환값을 받았는가?"
      체크박스 포함.

## 6. 당장의 복구 절차

1. Operator 가 `/loop <RUN.txt 본문>` 을 재붙여넣기 → 한 iter 가 실행되며
   끝에서 정상적으로 `ScheduleWakeup` 호출.
2. 혹은 `.autopilot/project.ps1 start` 로 재시작.

## 7. 교훈

- **"말했다 ≠ 했다"**: LLM 요약 텍스트는 실행 증거가 아니다. 중요 exit 단계는
  툴콜 자체로 검증해야 하며, sentinel 파일로 다음 iter 가 검증 가능해야 한다.
- **IMMUTABLE 체크리스트는 번호가 핵심**: 서술형 문장은 조용히 누락될 수
  있다. exit-contract 같은 안전 핵심 단계는 반드시 enumerate.
- **자가 재예약은 단일 실패점**: 이 한 번의 누락으로 전체 자율 루프가
  정지한다. dual-channel (툴콜 + sentinel) 로 이중화해야 복원력이 생긴다.
