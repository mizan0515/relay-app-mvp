# .autopilot/STATE.md — live state, keep ≤60 lines. Loaded every iteration.

root: .
base: main
iteration: 57
status: active
idle_upkeep_streak: 0  # PR #51 리뷰 대기 병행 · iter57 에 B13.1 regen script 착륙
next_iter_unblock_plan: iter58 — PR #51 폴링 4회째 + 추가 작은 개선(B15 후 보조 작업 or idle-upkeep). 여전히 reviewDecision=""/comments=[]. iter57 에서 B13.1 regen 스크립트 main 직접 커밋(non-protected .autopilot/regen-histogram.py). iter58 후보 — (a) 재생성 스크립트를 exit-contract 훅에 붙이는 후속(PROMPT 규칙 수정 필요 → protected 이라 미정) / (b) idle-upkeep 계속.
backlog: .autopilot/BACKLOG.md (B2·B4·B5·B6·B7·B9·B12·B13·B14 DONE · B11 CLOSED · B15 IN-REVIEW · active=B1·B3·B10·B8)
open_autopilot_prs: [51]
merged_since_last_iter: [50, 61a55a4]
mvp_gates: 7/8 (G2·G3·G4·G5·G6·G7·G8 [x]); G1 op-blocked

# 영구 OPERATOR 지시 (2026-04-18 chat) — 모든 future iter 준수:
#   "핵심문서 변경만 관리자 한국어 PR 확인, 나머지는 자동 머지.
#    기록은 남기고, PR도 고등학생 비개발자 관리자 입장에서 작성."
# PROMPT.md Operator-localization 규칙 5·6이 이를 강제 (main 착륙 완료).
operator_directives_sticky:
  - "비핵심(protected_paths 미접촉) PR은 빌드 그린 + 🟢 배지 조건 만족 시 로봇 자동 머지"
  - "핵심 문서(protected_paths) PR은 관리자 한국어 리뷰 필수"
  - "모든 PR 본문은 비개발자 고등학생 관리자 가독성 우선 (한국어 병기, 기술용어 최소화)"
  - "매 iter 행적은 HISTORY.md + 대시보드.md + METRICS.jsonl에 남긴다"

active_task: null  # iter50 idle-upkeep — no code sprint, BACKLOG 정리만

last_completed_task_g8:
  slug: g8-audit-log-integrity
  completed_iter: 49
  prs: [48, 49, 50]
  final_commit: 61a55a4

last_completed_task_g-bundle:
  slug: g-bundle-follow-up
  completed_iter: 46
  prs: [45, 46, 47]
  final_commit: 1761bc6
  plan_doc: .autopilot/G-BUNDLE-PLAN.md

last_completed_task_g7:
  slug: g7-consensus-convergence
  completed_iter: 42
  prs: [41, 42, 43, 44]
  final_commit: 26949eb
  plan_doc: .autopilot/G7-PLAN.md

last_completed_task_g6:
  slug: g6-rolling-summary
  completed_iter: 46
  prs: [40, 47]
  final_commit: 9cfaf10
  plan_doc: .autopilot/G6-PLAN.md

parked_task_g5:
  slug: g5-recovery-resume
  gate: G5
  status: "[~] partial — broker branch proven (xunit), end-to-end bundled with G4 [x] follow-up"
  plan_doc: .autopilot/G5-PLAN.md

parked_task_g4:
  slug: g4-round-trip-automated
  gate: G4
  status: "[~] partial — artifact-layer proven, broker-driven smoke deferred (see G4-PLAN.md §Follow-up)"

last_completed_task:
  slug: g3-checkpoint-verified
  completed_iter: 25
  prs: [33, 34]
  final_commit: 46aaa59
parked_task:
  slug: g1-peer-symmetric-packet-io
  reason: blocked on validator + cost-strip operator decisions
  branch: autopilot/g1-peer-symmetric-packet-io-20260418 (not yet created)

plan_docs:
  - DEV-PROGRESS.md
  - PROJECT-RULES.md
  - AGENTS.md
  - CLAUDE.md
  - DIALOGUE-PROTOCOL.md

spec_docs:
  - Document/DAD/PACKET-SCHEMA.md
  - Document/DAD/STATE-AND-LIFECYCLE.md
  - Document/DAD/BACKLOG-AND-ADMISSION.md
  - Document/DAD/VALIDATION-AND-PROMPTS.md

reference_docs:
  - .prompts/

# Auto-merge refuses if the PR diff touches any of these:
protected_paths:
  - .autopilot/PROMPT.md
  - .autopilot/MVP-GATES.md
  - .autopilot/project.ps1
  - .autopilot/project.sh
  - .githooks/
  - PROJECT-RULES.md
  - CLAUDE.md
  - AGENTS.md
  - DIALOGUE-PROTOCOL.md
  - Document/DAD/
  - .prompts/
  - tools/

open_questions:
  - "Which existing session packet format (YAML vs relay's JSON envelope) should the broker's primary I/O use when they collide?"
  - "Does the approval-UI surface need dual-agent view redesign, or can the existing single-pane approach serve both peers with role labels?"
  - "Is there a production DAD-v2 session artifact to replay against, or must we synthesize fixtures for the first gate?"

operator_requests:
  - "G1 validator 포팅 (tools/Validate-Dad*.ps1) — 세 옵션 중 하나 지시 필요."
  - "Codex/Claude 비대칭 비용 경로 — strip / generalize / defer 중 하나."
  - "옛 Validate-TemplateVariants 스크래핑 + CLAUDE.md 서두 정정 승인."

mvp_last_advanced_iter: 20
