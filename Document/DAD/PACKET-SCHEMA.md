# DAD Packet Schema

Use this file for the full Turn Packet shape and field rules.

## Turn Packet Shape

```yaml
type: turn
from: [codex | claude-code]
turn: 1
session_id: "YYYY-MM-DD-task"

contract:
  status: "proposed | accepted | amended"
  checkpoints: []
  amendments: []

peer_review:
  project_analysis: "..."        # Turn 1 only (optional on Turn 2+)
  task_model_review:
    status: "aligned | amended | superseded"
    coverage_gaps: []
    scope_creep: []
    risk_followups: []
    amendments: []
  checkpoint_results: {}
  issues_found: []
  fixes_applied: []

my_work:
  task_model: {}
  plan: ""
  changes:
    files_modified: []
    files_created: []
    summary: ""
  self_iterations: 0
  evidence:
    commands: []
    artifacts: []
  verification: ""
  open_risks: []
  confidence: "high | medium | low"

handoff:
  closeout_kind: ""              # peer_handoff | final_no_handoff | recovery_resume
  next_task: ""
  context: ""
  questions: []
  prompt_artifact: ""
  ready_for_peer_verification: false
  suggest_done: false
  done_reason: ""
```

## Field Rules

- `my_work` is mandatory.
- `suggest_done` and `done_reason` live only under `handoff`.
- `handoff.closeout_kind` should be set on new packets. Allowed values are `peer_handoff`, `final_no_handoff`, and `recovery_resume`.
- `peer_handoff` means another peer turn remains. It requires `handoff.ready_for_peer_verification: true`, non-empty `handoff.next_task`, non-empty `handoff.context`, and a valid `handoff.prompt_artifact`.
- `peer_handoff` should hand off outcome work, not ceremony-only cleanup. Do not use it just to relay wording correction, summary/state sync, closure seal, or validator-noise cleanup unless the DAD system itself is being repaired.
- `final_no_handoff` means the session closes on this turn without a new peer prompt. It requires `handoff.ready_for_peer_verification: false`, an empty `handoff.prompt_artifact`, and an empty `handoff.next_task`. A closing session has no continuation; remaining follow-up work must be admitted to the backlog before sealing the session.
- `recovery_resume` means no peer prompt is emitted because the same agent must resume later after interruption or context overflow. It requires `handoff.ready_for_peer_verification: false`, `suggest_done: false`, an empty `handoff.prompt_artifact`, `my_work.confidence: low`, and at least one concrete `open_risks` entry explaining the resume blocker.
- If `handoff.ready_for_peer_verification: true`, `handoff.prompt_artifact` is required and must point to the saved peer-prompt artifact for that turn.
- If `handoff.ready_for_peer_verification: true`, `handoff.next_task` and `handoff.context` must both be non-empty.
- If `handoff.ready_for_peer_verification: true`, `handoff.next_task` should still name concrete remaining work. Dedicated verify-only handoffs are the risk-gated exception for remote-visible, config/runtime-sensitive, measurement-sensitive, destructive, or provenance/compliance-sensitive work.
- `handoff.next_task` continues the current session. If the work now needs a different session, backlog admission should carry it instead.
- A closeout packet that ends, blocks, or supersedes the session is incomplete if the linked backlog item remains stale as `promoted`. Resolve or re-queue the linked item in the same closeout path.
- If `suggest_done: true`, `done_reason` is required.
- `suggest_done: true` may appear with `ready_for_peer_verification: false` on a final converged no-handoff turn.
- If `suggest_done: true` and `ready_for_peer_verification: false`, leave `prompt_artifact` empty for that turn.
- Legacy packets without `handoff.closeout_kind` are accepted only when the validator can infer `peer_handoff` or `final_no_handoff` unambiguously. A non-final active turn that omits both the handoff prompt and the final closeout markers is invalid.
- Closed sessions require summary artifacts.
- Root-level aliases such as `self_work` or root-level `suggest_done` are invalid.
