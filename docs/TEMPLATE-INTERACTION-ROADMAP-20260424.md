# Template Interaction Roadmap (2026-04-24)

## Goal

Apply lessons from the live product repos `D:\Unity\card game` and
`D:\cardgame-dad-relay` back into:

- `D:\codex-claude-relay`
- `D:\dad-v2-system-template`
- `D:\autopilot-template`

The target state is a three-layer system that can be copied, composed, and
operated without re-learning the same production failures.

## Lessons Learned From Live Use

1. Thin roots are mandatory.
   The live repos benefited from keeping `AGENTS.md`, `CLAUDE.md`,
   `DIALOGUE-PROTOCOL.md`, and other first-read files short while pushing detail
   into scoped docs. This reduces prompt waste and drift.

2. Operator surfaces must be compact and bounded.
   The card-game relay only became operable when status collapsed into compact
   signals, one dashboard, one obvious next action, and bounded waits instead of
   full-log reading.

3. Validators and doctor checks must encode production mistakes, not just schema.
   Real failures came from override leakage, stale signals, missed reschedules,
   drift between compact surfaces, and template/product boundary confusion.

4. Product assets and generic assets need an explicit asset boundary.
   The live relay needed repeated clarification about what stays local
   (Unity/card-game/operator policy) and what can safely move upstream.

5. Operator decisions need one path.
   Manual edits to state files are fragile. Decision PRs and merge-as-decision
   are the reusable pattern.

6. Every expensive path needs a compact proof artifact.
   The useful pattern was not "open the full log", but "emit one small JSON/TXT
   marker that a human or agent can trust before deciding the next step".

7. Template interaction needs a packaging contract.
   A real product uses all three layers together:
   autopilot loop -> DAD relay/runtime -> product repo. The boundaries,
   copy rules, and upstream/downstream sync rules must be explicit.

## Update Plan

### Track A - `D:\autopilot-template`

Focus: autonomous loop safety and compact operator control.

Planned updates:

- promote "compact proof artifact" to a first-class pattern in README and
  example wrappers
- add reusable doctor guidance for:
  - stale status/signal cleanup
  - test-only override leak detection
  - peer-tool/update drift detection
  - bounded wait / timeout checks
- document a default compact status contract:
  - one machine-readable signal file
  - one human-readable status file
  - one done marker
- keep decision-PR flow as the only durable operator decision path
- add a "managed path" example so downstream repos can expose one safe button
  instead of multiple raw loop controls

Acceptance signal:

- a downstream repo can expose one bounded operator flow without asking the
  operator to inspect raw logs or edit state files

### Track B - `D:\dad-v2-system-template`

Focus: peer-symmetric runtime contract that downstream repos can copy without
re-importing product-specific mistakes.

Planned updates:

- add a "template interaction" section to the root README:
  - DAD template = session/runtime contract
  - autopilot template = outer loop/operator control
  - product repo = local assets, local policies, local proof artifacts
- codify an upstream asset-boundary rule:
  - upstream only generic relay/session behavior
  - keep product governance, product prompts, and product skills local by
    default
- add explicit guidance for compact evidence artifacts in DAD workflows
- preserve thin-root and validator-first rules for all copied variants
- define a downstream sync checklist for adopters:
  - contracts
  - prompts
  - validators
  - hooks
  - skill metadata

Acceptance signal:

- a downstream team can tell, before editing, whether a lesson belongs in the
  DAD template or must remain product-local

### Track C - `D:\codex-claude-relay`

Focus: generic peer-symmetric relay engine and reference integration seam.

Planned updates:

- import only reusable engine improvements from `D:\cardgame-dad-relay`
- keep Unity/card-game/operator-specific behavior out of the generic relay
- add docs that explain the three-layer interaction model and upstream rules
- prefer compact health/status artifacts over narrative-only operator guidance
- keep doctor/validator coverage aligned with the actual runtime failure modes

Acceptance signal:

- reusable relay improvements can land here without dragging in card-game
  product logic or template-source assumptions

## Cross-Repo Sequencing

1. `autopilot-template`
   Lift the generic operator-control and compact-signal patterns first.

2. `dad-v2-system-template`
   Align the DAD packaging and downstream boundary rules with the autopilot
   pattern.

3. `codex-claude-relay`
   Import reusable relay-core pieces only after the two templates define the
   stable seam.

4. Product repos
   Re-sync `D:\cardgame-dad-relay` and `D:\Unity\card game` against the updated
   templates and keep only product-local deltas.

## Interaction Contract Between The Three Templates

### 1. Autopilot -> DAD Relay

- autopilot owns pacing, backlog, operator decision routing, compact status, and
  doctor checks
- DAD relay owns peer turn transport, packet/state validation, and symmetric
  handoff semantics
- autopilot must treat relay status as compact artifacts, not as raw log tails

### 2. DAD Relay -> Product Repo

- relay owns the generic peer collaboration mechanism
- product repo owns domain prompts, domain validators, domain dashboards,
  domain evidence, and domain routing heuristics
- any product rule promoted upstream must first prove it is domain-agnostic

### 3. Autopilot -> Product Repo

- autopilot should ship only generic wrappers and examples
- product repo supplies concrete `project.ps1` / `project.sh` doctor, test,
  audit, and compact-signal generation

## Immediate Work Packages

1. `autopilot-template`
   README + example wrapper updates for compact-signal/managed-path/doctor
   rules.
   Reference: `D:\autopilot-template\INTEGRATION-CHECKLIST.md`

2. `dad-v2-system-template`
   README updates clarifying interaction with autopilot-template and product
   repos.
   Reference: `D:\dad-v2-system-template\TEMPLATE-INTERACTION.md`

3. `codex-claude-relay`
   docs update pointing maintainers at this roadmap and the upstream boundary.
   Reference: [UPSTREAM-BOUNDARY-20260424.md](D:\codex-claude-relay\docs\UPSTREAM-BOUNDARY-20260424.md)
   Compact-state reference: [COMPACT-SIGNAL-CONTRACT-20260424.md](D:\codex-claude-relay\docs\COMPACT-SIGNAL-CONTRACT-20260424.md)

## Non-Goals

- copying card-game-specific governance or Unity workflows into upstream repos
- turning the template source repo into a live runtime workspace
- merging operator UX shortcuts that depend on one product's naming or folder
  layout
