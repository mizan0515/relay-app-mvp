# .autopilot/STATE.md — live state, keep ≤60 lines. Loaded every iteration.

root: .
base: main
iteration: 0
status: initialized
active_task: null
# active_task schema:
#   slug: <kebab-case>
#   plan: [bullet, bullet]
#   started_iter: N
#   branch: dev/autopilot-<slug>-<YYYYMMDD>
#   gate: G<n>  (reference to .autopilot/MVP-GATES.md)

plan_docs:
  - DEV-PROGRESS.md
  - IMPROVEMENT-PLAN.md
  - README.md

spec_docs:
  - PHASE-A-SPEC.md
  - INTERACTIVE-REBUILD-PLAN.md
  - TESTING-CHECKLIST.md
  - CLAUDE-APPROVAL-DECISION.md
  - capability-matrix.md

reference_docs:
  - KNOWN-PITFALLS.md
  - phase-f-survey.md
  - git-workflow.md

# Auto-merge refuses if the PR diff touches any of these:
protected_paths:
  - RelayApp.sln
  - .autopilot/PROMPT.md
  - .autopilot/MVP-GATES.md
  - .autopilot/CLEANUP-LOG.md
  - .autopilot/CLEANUP-CANDIDATES.md
  - .autopilot/project.ps1
  - .autopilot/project.sh
  - .githooks/
  # Dormant defensive guards (origin-template carryover — files do not exist here
  # today, but if they ever reappear the guard activates automatically. Do NOT
  # prune these just because the paths are missing from this repo).
  - en/
  - ko/
  - tools/
  - PROJECT-RULES.md
  - CLAUDE.md
  - AGENTS.md
  - DIALOGUE-PROTOCOL.md

open_questions:
  - "Does the rotation-with-carry-forward exercise (F-live-1) meaningfully preserve Goal/Completed/Pending across the split, or do the fields end up empty in practice?"
  - "Can the approval UI communicate destructive-tier risk without operator reading the full command, or is the risk summary still too abstract?"
  - "Is Claude's audit-only stance still the right call given the handoff-parser maturity curve, or should we revisit per CLAUDE-APPROVAL-DECISION.md?"

# MVP gates: canonical checklist at .autopilot/MVP-GATES.md. STATE tracks only tally.
mvp_gates: 0/8
mvp_last_advanced_iter: 0

# OPERATOR overrides — any line starting with `OPERATOR:` wins over PROMPT.md.
#   OPERATOR: halt
#   OPERATOR: halt evolution
#   OPERATOR: focus on <task>
#   OPERATOR: allow evolution <rationale>
#   OPERATOR: allow push to main for <task>    (single use, delete after use)
#   OPERATOR: require human review             (disables auto-merge globally)
#   OPERATOR: run cleanup                      (promotes Cleanup mode this iter; one-shot)
#   OPERATOR: mvp-rescope <rationale>          (allow gate count to decrease; one-shot)
#   OPERATOR: post-mvp <direction>             (unblocks after mvp-complete halt; sticky)
#   OPERATOR: approve cleanup <candidate-date> (authorizes Phase B bulk-delete; one-shot)
#
# One-shot overrides are CONSUMED by the loop at the end of the iteration that
# acts on them — the exit step deletes the line from this file. Sticky
# overrides persist until the operator removes them manually.
