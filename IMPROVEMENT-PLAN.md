# Relay App MVP Improvement Plan

## Purpose

This plan moves `relay-app-mvp` from a bounded handoff relay into a brokered dual-agent runtime that can safely let Claude Code CLI and Codex CLI do real work on a machine.

Target outcome:

- Claude and Codex remain the actual execution engines
- file edits, shell/PowerShell commands, git commit/push, PR creation, MCP tool use, and deep tool chains can happen inside the relay
- risky actions are mediated by the relay app, not trusted to hidden model behavior
- action-level audit logs, approval records, and continuity state are durable and reviewable

## Product Goal

The desired operator experience is:

1. The user starts a task in the relay app.
2. Codex performs part of the work and hands off to Claude.
3. Claude reviews, extends, or corrects the work and hands off back to Codex.
4. The relay repeats until the task is complete.
5. If either side tries to run a risky command, commit, push, or create a PR, the app pauses and asks the user.
6. Every meaningful action is logged.
7. Long-running work survives session rotation through broker-owned continuity data.
8. Finished work can register the next item in backlog instead of ending as an isolated chat.

## Non-Goals

- Recreating hidden Desktop-host-only behavior exactly
- Trusting hidden chain-of-thought as continuity state
- Trusting vendor auto-compact as the relay's source of truth
- Treating the current bounded handoff-only prompt contract as sufficient for deep tool chains

## Current State

Implemented already:

- bounded handoff relay
- broker state, persistence, JSONL event log
- usage and cost tracking
- budget breakers and rotation
- experimental interactive adapters
- protocol-backed Codex app-server transport
- readable auto-log snapshots under `%LocalAppData%\RelayAppMvp\auto-logs`

Missing for the product target:

- robust approval handling
- action-level observability
- git workflow policy and wrappers
- PR workflow
- MCP capability discovery and audit
- deep tool-chain runtime contract
- broker-owned rolling summary and carry-forward memory
- explicit multi-agent orchestration strategy

## Design Principles

### 1. CLI-first

The product must keep using:

- `claude` CLI
- `codex` CLI / `codex app-server`

The relay app is an orchestrator and governor, not a replacement execution engine.

### 2. Broker-owned authority

The broker owns:

- permission policy
- approval workflow
- audit trail
- git and PR mediation
- continuity state
- recovery semantics

### 3. Pass-through where possible

If Claude or Codex already support a capability natively, prefer passing it through and observing it rather than re-implementing the capability in the broker.

### 4. Safe-by-default

Potentially destructive or externally visible actions must never rely only on model self-restraint.

## Capability Model

### Pass-through candidates

- file reads and writes inside the working directory
- CLI-native shell/tool use
- MCP tools already configured for each CLI
- repo-visible instructions such as `AGENTS.md`, `CLAUDE.md`, and project docs

### Broker-owned control points

- approval prompts
- command classification
- git commit/push/PR wrappers
- action-level logs
- continuity summary and carry-forward state
- cross-agent handoff acceptance and validation

### Likely non-portable host features

- Desktop-host-only skill registry behavior
- host-level sub-agent orchestration APIs
- hidden auto-compact as a reliable continuity contract

## Phase Plan

## Phase A: Safety Floor

### Goal

Create the minimum product layer required to let the agents use the machine without losing operator control.

### Deliverables

#### A1. Permission model

Define a broker-owned policy file, for example:

- `%LocalAppData%\RelayAppMvp\policy.json`

Initial policy categories:

- file read
- file write within working directory
- shell/PowerShell command
- network access
- git read commands
- git write commands
- git push
- PR creation
- MCP tool call
- destructive command

Initial actions:

- `allow`
- `ask`
- `deny`

#### A2. Approval queue

Add a runtime approval queue with records containing:

- `Id`
- `SessionId`
- `Turn`
- `Side`
- `Category`
- `RiskLevel`
- `ActionText`
- `WorkingDirectory`
- `TargetFiles`
- `Preview`
- `CreatedAt`

#### A3. Approval UI

Add WPF UI for:

- current pending approval
- allow once
- allow for session
- persist allow rule
- deny
- stop session

#### A4. Codex approval routing

Replace the current "not implemented" handling for server-initiated requests from `codex app-server`.

Required behavior:

- surface approval request into the broker approval queue
- pause the relay while awaiting user input
- resume or deny based on user action

#### A5. Claude approval baseline

Short term:

- keep the CLI path
- explicitly document and constrain allowed tools as much as the current surface permits

Medium term:

- evaluate whether Claude Agent SDK is required for parity-grade approval hooks

### Exit Criteria

- risky Codex actions no longer run silently
- the app can stop, allow, or deny a high-risk operation
- approval results are durable in logs

## Phase B: Action Observability

### Goal

Make the broker aware of what the agents actually did, not only what they claimed in handoff JSON.

### Deliverables

#### B1. Action event schema

New event types:

- `tool.invoked`
- `tool.completed`
- `tool.failed`
- `approval.requested`
- `approval.granted`
- `approval.denied`
- `git.commit.requested`
- `git.commit.completed`
- `git.push.requested`
- `git.push.completed`
- `pr.create.requested`
- `pr.create.completed`

#### B2. Claude stream-json extraction

Extend parsing to capture:

- tool-use events
- tool results
- blocked or approval-related states

#### B3. Codex app-server extraction

Capture:

- tool-related items
- approval-related requests
- command execution signals when available

#### B4. UI and auto-log output

Expose:

- recent tool actions
- last approval
- last git action
- last PR action

### Exit Criteria

- the operator can answer "what did Codex or Claude actually do last turn?" from logs alone
- commit/push/PR attempts are visible even if the handoff text is misleading or incomplete

## Phase C: Capability Audit

### Goal

Turn assumptions into an explicit capability matrix.

### Audit Areas

#### C1. MCP

Verify separately for Claude and Codex:

- whether configured MCP servers load under relay execution
- whether tool calls still work inside relay turns
- whether the broker can observe that MCP was used

#### C2. Shell and PowerShell

Verify:

- simple commands
- multi-step commands
- script invocation
- command chaining
- timeouts and cleanup

#### C3. Git

Verify:

- `git status`
- `git add`
- `git commit`
- `git push`
- `gh pr create`

#### C4. DAD assets

Classify current DAD features into:

- directly usable in relay
- conditionally usable
- broker reimplementation required

### Deliverables

- `capability-matrix.md`
- `mcp-audit.md`
- `git-audit.md`

### Exit Criteria

- no major product dependency remains based on guesswork

## Phase D: Git Workflow Layer

### Goal

Treat commit, push, and PR creation as first-class product operations.

### Deliverables

#### D1. Commit wrapper

Before commit:

- show staged diff summary
- show commit message preview
- require policy decision or approval

Record:

- author side
- branch
- commit SHA
- commit message
- timestamp

#### D2. Push wrapper

Before push:

- show remote and branch
- show whether force push is attempted
- require approval

Record:

- remote
- branch
- commit range if available
- timestamp

#### D3. PR wrapper

Before PR creation:

- preview base/head/title/body
- require approval

Record:

- PR URL
- base/head
- title
- timestamp

#### D4. Safety defaults

Recommended defaults:

- `git status`, `git diff`, `git log`: allow
- `git add`: ask
- `git commit`: ask or session-allow
- `git push`: ask
- `gh pr create`: ask
- `git push --force`: deny
- `git reset --hard`: deny

### Exit Criteria

- commit/push/PR are never "invisible shell side effects"
- the app can reconstruct the git lifecycle for a session afterward

## Phase E: Deep Tool-Chain Runtime

### Goal

Let each side perform substantive multi-step work inside one turn without losing relay discipline.

### Current Limitation

The bounded prompt contract heavily biases the model toward returning one JSON handoff immediately.

### Deliverables

#### E1. Tool-rich interactive contract

Allow:

- exploration
- edits
- tests
- shell commands
- MCP tool use

Require:

- final handoff boundary output at the end

#### E2. Failure-to-handoff recovery

If the side does real work but fails to emit a valid handoff:

- preserve action history
- run repair prompt
- avoid losing the fact that work already occurred

### Exit Criteria

- a turn can realistically perform edit → test → inspect → summarize → handoff

## Phase F: Broker-owned Continuity

### Goal

Maintain continuity across turns and rotations without trusting hidden vendor behavior.

### Deliverables

#### F1. Rolling summary

Before planned rotation:

- summarize relevant state
- write durable summary file

Possible path:

- `%LocalAppData%\RelayAppMvp\summaries\{sessionId}-segment-{n}.md`

#### F2. Carry-forward state

Structured state fields:

- `Goal`
- `Completed`
- `Pending`
- `Constraints`
- `LastHandoffHash`
- `UpdatedAt`

#### F3. Prompt assembly

Inject into next turn:

- latest accepted handoff
- rolling summary
- carry-forward state
- selected recent events

#### F4. Summary events

- `summary.generated`
- `summary.loaded`
- `summary.failed`
- `summary.bytes`
- `summary.cost`

### Exit Criteria

- session rotation no longer feels like memory loss

## Phase G: Skill, MCP, and Agent Strategy

### Goal

Make the DAD ecosystem intelligible under relay constraints.

### Deliverables

#### G1. Skill classification

Classify current DAD assets:

- repo-readable instructions
- CLI-native tools
- host-registered skills
- host-only orchestration

#### G2. MCP policy integration

Extend policy and logs so MCP is not a blind spot.

#### G3. Agent strategy

Stage 1:

- rely on model internal task decomposition

Stage 2:

- broker-managed task fan-out/fan-in

Stage 3:

- real child session orchestration with separate approval and budget policies

### Exit Criteria

- the product clearly knows which DAD capabilities are portable, conditional, or non-portable

## 6. Data and Log Design

### Approval record

- `id`
- `session_id`
- `turn`
- `side`
- `category`
- `risk_level`
- `action_text`
- `working_directory`
- `target_files`
- `preview`
- `created_at`
- `resolved_at`
- `resolution`

### Action log payload

- `session_id`
- `turn`
- `side`
- `action_type`
- `tool_name`
- `command`
- `target_files`
- `result`
- `diagnostics`

### Git log payload

- `repo`
- `branch`
- `commit_sha`
- `message`
- `remote`
- `pr_url`
- `approved_by`

## 7. Risks

- approval handling delayed too long leaves the product in an unsafe state
- action logging delayed too long leaves shell/git/MCP behavior un-auditable
- blind MCP pass-through creates a privilege blind spot
- continuity work without observability can hide state drift
- assuming host-only agent features are portable will derail the roadmap

## 8. Recommended Execution Order

1. Safety floor
2. Action observability
3. Capability audit
4. Git workflow layer
5. Deep tool-chain runtime
6. Broker-owned continuity
7. Skill/MCP/agent strategy refinement

## 9. Success Criteria

The plan succeeds when:

- operators can let the agents perform real machine actions without losing control
- high-risk actions are mediated and logged
- commit/push/PR become product features rather than opaque side effects
- MCP and tool use are real, visible, and governable
- long tasks remain coherent after rotation
