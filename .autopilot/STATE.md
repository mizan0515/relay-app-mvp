# .autopilot/STATE.md — live state, keep ≤60 lines. Loaded every iteration.

root: .
base: main
iteration: 0
status: reset-in-progress
active_task:
  slug: dad-v2-reset-rebuild
  plan:
    - "Phase 0: DAD-v2 contract files ported (DONE)"
    - "Phase 1: IMMUTABLE:mission block + hook rewrite + new MVP-GATES (DONE)"
    - "Phase 2: delete poison docs + CodexProtocol projects + RelaySide/CodexPricing (DONE)"
    - "Phase 3: Desktop/ retained for future dual-agent UI (DONE)"
    - "Phase 4: ADAPT — see .autopilot/PHASE4-PLAN.md (PENDING, build currently broken on reset branch)"
    - "Phase 5: commit --no-verify → push → PR → land → remove HALT → loop iter 1 → G1"
  started_iter: 0
  branch: reset/dad-v2-aligned
  gate: N/A (pre-gate reset — HALT active)

# active_task schema:
#   slug: <kebab-case>
#   plan: [bullet, bullet]
#   started_iter: N
#   branch: autopilot/<slug>-<YYYYMMDD>
#   gate: G<n>  (reference to .autopilot/MVP-GATES.md)

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
  # DAD-v2 contract files — peer protocol definition, drift-locked.
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

# MVP gates: canonical checklist at .autopilot/MVP-GATES.md. STATE tracks only tally.
mvp_gates: 0/8
mvp_last_advanced_iter: 0

# OPERATOR overrides — any line starting with `OPERATOR:` wins over mutable rules
# but NEVER over IMMUTABLE:mission.
#   OPERATOR: halt
#   OPERATOR: focus on <task>
#   OPERATOR: run cleanup
#   OPERATOR: mvp-rescope <rationale>
#   OPERATOR: post-mvp <direction>
#   OPERATOR: require human review
