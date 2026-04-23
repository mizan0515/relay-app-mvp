# Agent Governance Roadmap 2026-04-23

## Sources mapped

- `claude-slim`
  - startup token waste usually hides in always-loaded skills, memory, plugin instructions, and stale project state
  - apply as compact inventory plus reversible cleanup, never destructive deletion
- GeekNews `28697`
  - reduce direct context injection and unnecessary tools before touching model budgets
- GeekNews `28777` + `agent-skills`
  - convert recurring failures into registered, testable workflow assets instead of ad-hoc prompt text
- Google Cloud agent-governance stack
  - build in order:
    1. agent identity
    2. centralized tool registry/governance
    3. centralized policy enforcement
    4. behavioral anomaly detection
    5. unified posture dashboard

## Current mapping to card-game relay

- Layer 1 already partially exists as role names in Desktop, relay peers, and compact manager states, but not as a distinct identity registry.
- Layer 2 partially exists as skill contracts and forbidden tools, but not as a registry of approved agent/tool identities.
- Layer 3 already exists in compact form through governance, required evidence, and tool policy.
- Layer 4 exists only indirectly through stale/hung/retry-budget signals; there is no explicit anomaly artifact yet.
- Layer 5 partially exists through the ops dashboard, but it does not yet unify identity, registry, policy, and anomaly into one security posture section.

## Planned application order

### Step 1: Agent identity

- create a registry of distinct autopilot, relay peer, route, and Unity-MCP identities
- resolve active identities per manifest and execution mode
- expose identity marker in governance, manager signal, and ops dashboard
- block governance when a required identity is missing or unregistered

### Step 2: Centralized tool governance

- create a compact registry for approved tools, MCP servers, and usage classes
- connect each active identity to the tools it may use
- surface missing/unapproved tools in governance before the relay loop starts
- status: initial compact implementation added via `tool-registry.json` and `generated-tool-registry-status.*`

### Step 3: Policy enforcement

- keep prompt contracts, but move more checks into deterministic compact gates
- use one policy artifact instead of repeating local rules in many prompts
- keep non-developer operator language short and action-oriented
- status: initial compact implementation added via `policy-registry.json` and `generated-policy-registry-status.*`

### Step 4: Behavioral anomaly detection

- add compact anomaly markers for unusual retry patterns, off-route tool usage, output-budget spikes, and identity/tool mismatches
- avoid full-log analysis by default; derive anomalies from compact counters and known relay events
- status: initial compact implementation added via `anomaly-rules.json` and `generated-anomaly-status.*`

### Step 5: Unified posture

- extend ops dashboard with a single security posture section
- unify identity, registry, policy, anomaly, and remediation status into one operator view
- status: initial compact implementation added via `generated-security-posture.*`
- route follow-through: use the previous compact posture and prompt-surface result to downshift the next execution mode before another expensive relay cycle

## Token-efficiency posture

- prefer removing avoidable context and tool exposure before raising model effort or output budgets
- keep compact artifacts as the default evidence path
- make every new governance layer emit small JSON/TXT outputs so the operator prompt can stay stable and cheap
