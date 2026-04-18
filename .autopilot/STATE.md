# .autopilot/STATE.md — live state, keep ≤60 lines. Loaded every iteration.

root: .
base: main
iteration: 107
status: post-mvp-idle  # iter107: MVP 완주 후 idle-upkeep, 운영자 지시 대기
idle_upkeep_streak: 12
next_iter_unblock_plan: iter108 — post-MVP 백로그(B16/B17/B18) 운영자 승인 대기 지속
backlog: .autopilot/BACKLOG.md (B1·B2·B3·B4·B5·B6·B7·B9·B12·B13·B13.1·B14·B15 DONE · B11 CLOSED · B10 SPEC-LANDED · active=B8 blocked · post-MVP B16/B17/B18)
open_autopilot_prs: []  # iter95: PR #58 머지 완료
merged_since_last_iter: [58]  # iter95: 🎉 G1 flip PR 관리자 머지 → MVP 8/8
mvp_gates: 8/8 🎉🎉🎉 COMPLETE (PR #58 merged 2026-04-18T20:12Z · commit 0da8b0d)

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
active_task_g1:
  slug: g1-peer-symmetric-packet-io
  status: "UNBLOCKED iter61 — 관리자 3 결정 접수 (1:b · 2:e · 3:승인)"
  branch: autopilot/g1-peer-symmetric-packet-io (iter62 생성 예정)
  decisions:
    validator: "b — en/ko Validate-Dad*.ps1 스크래핑 → tools/Validate-Dad-Packet.ps1"
    cost_path: "e — AgentCostAdvisor 전략으로 Codex/Claude 대칭화"
    claude_md_intro: "승인 — 서두 ~20줄 v2 언어로 정정"

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

operator_requests: []  # iter61: 3건 모두 답변 접수 — RUNBOOK 하단 참조
operator_answered_iter61:
  - "validator: b (en/ko 스크래핑)"
  - "cost_path: e (generalize via AgentCostAdvisor)"
  - "claude_md_intro: 승인"

mvp_last_advanced_iter: 20
