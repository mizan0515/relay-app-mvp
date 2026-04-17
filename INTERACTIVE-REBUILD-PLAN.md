# Interactive Runtime Hybrid Plan

## Decision

Do not rebuild the whole app from zero.

Do not build a PTY-first or TUI-first architecture.

Build a protocol-first hybrid interactive runtime:

- broker stays
- bounded non-interactive runtime stays
- Claude interactive uses a non-TUI stateful transport
- Codex interactive uses a non-TUI stateful transport
- PTY is out of scope

Keep:

- `RelayApp.Core` broker logic
- handoff schema, parser, normalization, and smoke-test repair behavior
- session persistence and event logging
- the WPF shell, diagnostics, and operator workflow
- the current non-interactive runtime as the stable fallback

Replace or supersede:

- the redirected-stdio interactive session prototype
- the assumption that "interactive" means "screen-scrape the TUI"
- the assumption that long-lived `-p` / `exec resume` loops are enough by themselves

Reason:

- Codex interactive requires a real terminal and faults with `stdin is not a terminal`.
- Even with ConPTY, TUI output may not be recoverable as a stable line stream.
- Current evidence indicates both sides already expose better non-TUI control surfaces than the visible terminal UI.
- The broker, handoff contract, and diagnostics already work and should not be discarded.

## Goal

Add a production-viable interactive runtime for both `codex` and `claude` that preserves multi-turn continuity and tool use without making the terminal UI the integration surface.

The target architecture is:

- one broker
- one Claude stateful runtime using structured streaming or SDK control
- one Codex stateful runtime using app-server / protocol control
- bounded non-interactive runtime as the only fallback
- explicit handoff boundaries and local relay state as the source of truth

## Core principle

Interactive is about:

- preserving session continuity
- allowing multi-step tool use
- avoiding full re-bootstrap every turn

Interactive is not the same thing as:

- reading the visual terminal buffer
- screen-scraping a chat box
- committing to a TUI-first implementation

Compaction is the broker's responsibility.

The TUI may present compaction nicely, but the relay must not depend on hidden TUI auto-compaction as a safety property.

The accepted handoff packet and broker-owned summary are the continuity substrate.

Resume handles, session identifiers, and transport-side thread IDs are optimization hints only.

They are not the continuity contract.

## Transport decisions

These are no longer discovery questions. They are the current plan.

### Claude

Primary path:

- `claude -p --output-format stream-json --resume <id>` from the desktop app

Secondary path:

- Claude Agent SDK only if the CLI streaming path is missing a needed capability or if `PreCompact`/`compaction_control` become load-bearing

Rejected path:

- PTY/TUI capture

Reason:

- Claude already provides structured streaming, resumable sessions, and tool-use events through non-TUI surfaces.

### Codex

Primary path:

- `codex app-server` over stdio using JSON-RPC

Client strategy:

- .NET client using `StreamJsonRpc`

Schema strategy:

- generate and pin protocol/schema artifacts from the installed Codex CLI version

Rejected path:

- PTY/TUI capture

Reason:

- Codex app-server exposes threads, turns, items, and approval requests directly.
- Automating the TUI would be strictly worse if app-server is available.

### Fallback

The bounded runtime remains the only supported fallback:

- Claude bounded fallback: `claude -p`
- Codex bounded fallback: `codex exec`

There is no PTY fallback in the current plan.

The bounded runtime remains first-class even after the hybrid runtime exists.

It is:

- the stable fallback
- the economic baseline
- the trust baseline when interactive is faulted, over-budget, or regressed

## Current state assessment

What already works:

- bounded non-interactive relay
- smoke test in the default runtime
- repair loop, fallback normalization, and diagnostics export
- session status UI and event log visibility

What is currently misleading or incomplete:

- the interactive runtime is only a process-backed prototype
- current interactive code assumes line-oriented text extraction and substring matching
- approval detection is currently heuristic string matching and should not be treated as a valid long-term design
- repeated `-p` / `exec resume` can carry token and context growth without enough budget control

## Keep / replace matrix

### Keep as-is

- `RelayBroker`
- `HandoffParser`
- `RelayPromptBuilder` boundary-marker concepts
- JSON state store
- JSONL event log
- smoke-test workflow
- diagnostics export
- WPF layout and operator controls

### Replace completely

- `ProcessInteractiveRelaySession`
- `ProcessInteractiveRelaySessionFactory`
- `InteractiveOutputParser`
- any logic that assumes `RedirectStandardInput/Output` is enough for interactive CLIs
- any logic that assumes a terminal session can be understood from `ReadLineAsync`
- any approval detection based on substring matching of the raw stream

### Replace, not merely adapt

- `IInteractiveRelaySession`
- `IInteractiveRelaySessionFactory`
- `TerminalSessionSnapshot`
- `InteractiveRelayAdapterBase`

These should be redesigned around transport-neutral session state, not string-tail snapshots.

## Toolchain choices

### Claude toolchain

- process invocation from the desktop app
- `claude -p --output-format stream-json`
- `--resume <session_id>` when continuing
- JSONL parsing with `System.Text.Json`
- consume structured messages and tool-use events directly

Agent SDK is a reserve option, not the first implementation target.

### Codex toolchain

- process invocation of `codex app-server`
- stdio JSON-RPC transport
- `StreamJsonRpc` for the .NET client
- generated JSON schema or typed artifacts pinned per Codex CLI version

## Protocol and versioning policy

Codex app-server is powerful, but protocol drift is a real risk.

Mandatory policy:

- pin a known-good Codex CLI version for development and test
- generate protocol/schema artifacts from that version
- keep golden transcript or fixture coverage for expected thread/turn/item flows
- fail loudly when protocol drift is detected

## Handoff contract

Even in interactive mode, hidden session state is not the source of truth.

Required boundary:

- `===DAD_HANDOFF_START===`
- one raw JSON object
- `===DAD_HANDOFF_END===`

Rules:

- prefer typed transport events and structured results over text recovery whenever the transport provides them
- markers remain useful as a fallback boundary contract, not as the primary data source
- extra analysis before the handoff is tolerated only if the final boundary is intact
- repair prompts should request only the bounded handoff block

## Token and budget policy

This is a hard product requirement, not an optimization.

Long-lived interactive sessions can silently grow cost and context. Bounded turns may remain economically superior for this workload.

Mandatory controls:

- explicit per-session budget tracking
- explicit per-turn budget tracking where supported
- explicit cache-hit / cache-miss visibility where available
- explicit session rotation
- broker-owned durable state so sessions can be restarted intentionally
- explicit broker-owned summary or carry-forward state outside the live session
- explicit rotation-latency tracking so rotation does not silently destroy wall-clock usability
- explicit output/thinking-token ceilings, not only input ceilings
- explicit process-lifecycle policy for long-lived background transports
- explicit cost-source policy so broker accounting does not trust undercounting local logs

Initial policy:

- rotate Claude session after `20` turns, `15` minutes, or when cumulative per-turn input exceeds configured budget
- rotate Codex thread or session after `20` turns or `15` minutes, with exact thresholds configurable
- record input/output/cache metrics per turn where the transport exposes them
- summarize state into handoff packets and local files before planned rotation
- prefer a fresh session plus explicit context over uncontrolled session growth
- force rotation if cache-miss behavior degrades for multiple consecutive turns
- enforce an absolute per-session cost ceiling

Suggested starting defaults:

- `maxTurnsPerSession = 20`
- `maxWallClockSeconds = 900`
- `maxCumulativeInputTokens = 40000`
- `maxPerTurnOutputTokens = 12000`
- `maxCumulativeOutputTokens = 30000`
- `cacheMissRatioThreshold = 0.35 for 3 consecutive turns`
- `maxSessionCostUsd = 1.00`
- `coldStartBudgetSeconds = 12`

These numbers are starting defaults and must be configurable.

Authoritative telemetry policy:

- treat live transport usage fields as the only authoritative cost source
- do not make broker budget decisions from local CLI history logs alone
- mirror authoritative usage into the broker event log for audit and regression detection

## Compaction parity strategy

The TUI path is attractive because it appears to compact or summarize context automatically when a session grows too large.

The protocol-first runtime must recreate the useful part of that safety property deliberately instead of assuming a live session can grow forever.

Do not emulate the TUI.

Do not depend on the TUI.

Treat TUI compaction as less observable than the non-TUI surfaces, not safer.

Product rule:

- live interactive sessions are disposable execution surfaces
- the broker-owned handoff packet and local relay state remain the continuity source of truth
- compaction must happen before a session becomes dangerously expensive or forgetful
- the broker should rotate before tool-internal auto-compaction becomes the normal path

### Codex compaction strategy

Codex app-server already exposes explicit context-management RPCs:

- `thread/compact/start`
- `thread/fork`
- `thread/rollback`
- `thread/tokenUsage/updated`

Codex policy:

- track `thread/tokenUsage/updated` on every active thread
- trigger `thread/compact/start` before rotation thresholds are exceeded
- treat compaction as a first-class broker action with explicit event logging
- use `thread/fork` when preserving the pre-compaction state is safer than mutating the active thread
- if compaction completes but the next turn shows degraded continuity, rotate the thread instead of trusting the compacted state indefinitely
- keep `thread/rollback` available as an operator recovery tool, not a silent background action
- track cold-start latency after planned rotation and widen or narrow rotation thresholds based on measured wall-clock impact
- restart the `codex app-server` process on a schedule instead of treating it as immortal
- monitor process memory and orphan behavior so the broker can recycle the process before Windows-level degradation appears

### Claude compaction strategy

Claude does expose `/compact` and automatic compaction in the TUI, but the protocol-first path should not depend on emulating slash-command behavior through the terminal UI.

If Claude Agent SDK hooks are used, `PreCompact` should be treated as an observability seam, not as the main continuity mechanism.

Claude policy:

- do not rely on hidden auto-compaction inside the TUI
- prefer bounded stream-json sessions plus broker-managed summaries and planned rotation
- keep a rolling broker summary outside the live Claude session so a fresh session can resume cheaply
- use `--resume` only within bounded windows where cache behavior is still acceptable
- aggressively rotate when cache-miss behavior or per-turn token usage worsens
- if Claude auto-compaction is available in the chosen transport, configure it so broker rotation still normally happens first
- treat returned session IDs as opaque and re-read them after each resumed turn instead of trusting the requested ID
- budget output/thinking tokens explicitly because cost can spike even when input growth looks acceptable

### Shared anti-drift policy

To match the practical benefit of TUI compaction without inheriting TUI fragility:

- maintain a compact broker summary file or state block per relay session
- maintain a last-accepted-handoff packet that can always restart the next session
- keep prompts narrow so compaction summaries stay short and auditable
- prefer explicit state carry-forward over trusting the model to remember hidden long-form context
- if compaction or resume behavior becomes unreliable, fall back to the bounded runtime for that turn
- record `PreCompact`, `RotationEvent`, `Downgrade`, and `TokenBudgetBreach` explicitly in the event log
- treat transport-side session IDs as replaceable metadata that may drift across resume operations
- record `CacheRegressionSuspected` explicitly when caching behavior deteriorates across consecutive turns
- budget broker-generated summary calls separately from relay turns

### Summary model policy

Broker-generated summaries should be cheap, explicit, and separate from the live relay session.

Policy:

- do not route rolling summaries through the active Claude or Codex relay session
- use a cheaper direct model path for summaries when implementation begins
- log summary-call cost separately from relay-turn cost
- if summary generation becomes expensive enough to compete with a bounded rerun, tighten rotation instead of summarizing more often

### Explicit no-go rule

Do not ship a design that keeps a Claude or Codex session alive indefinitely with no compaction, no rotation, and no budget ceiling.

That would recreate the exact failure mode that made the TUI attractive in the first place, but without any trustworthy guardrail.

## Approval handling policy

Do not ship approval auto-detection based on substring heuristics.

Policy:

- if the transport emits an approval request, input prompt, or blocked state, pause the relay
- show the relevant structured event or recent output to the operator
- require operator confirmation to continue

Approval semantics are side-specific and should stay side-specific below the adapter layer.

## Reconnect and restart policy

This is not a late hardening detail. It is a design constraint.

Assume:

- an interactive transport can die unexpectedly
- reconnect may actually mean restart plus broker-side replay from the last accepted handoff

So the product rule should be:

- the broker owns the durable relay state
- live sessions are disposable execution surfaces
- any restart should recover from the last accepted handoff, not from hidden transport memory alone
- any resumed session handle must be revalidated from transport output before reuse
- any long-lived transport process may be recycled independently of relay-session continuity

## Per-turn fallback policy

Runtime selection should not remain a binary all-or-nothing flag forever.

Target policy:

- if an interactive side is `Faulted`, `Blocked`, or `Stalled`, the broker may retry the current turn through the bounded non-interactive runtime
- every downgrade must be logged explicitly
- downgrade should be opt-in at first, not silent
- operator UI must show that a downgrade happened and why
- bounded remains the default runtime until interactive proves cost, latency, and operator-trust parity

This keeps the stable runtime useful even while the hybrid runtime is still maturing.

## Delivery phases

### Phase 0: Freeze and document

- keep default runtime as the only supported path
- keep the current interactive prototype explicitly unsupported
- document why

Exit condition:

- no one can mistake the current prototype for the intended interactive runtime

### Phase 0.5: Decision note

Write and keep one short decision note that records:

- Claude path = stream-json first
- Codex path = app-server first
- bounded runtime = only fallback
- PTY/TUI path = out of scope unless revisited later

Exit condition:

- transport choices are fixed before implementation starts

### Phase 1: Codex protocol transport

- build Codex first
- launch `codex app-server`
- connect through `StreamJsonRpc`
- implement thread and turn lifecycle
- implement compaction, fork, rollback, and token-usage handling
- implement schema generation/pinning
- add golden fixtures for expected protocol flows
- add budget and telemetry hooks as early as possible
- add cold-start latency measurement after planned rotations
- add process uptime, memory, and restart telemetry for `codex app-server`

Exit condition:

- Codex can complete a relay turn through app-server without touching the TUI
- Codex compaction and rotation can be exercised intentionally and logged
- Codex rotation latency is measured and compared against the configured cold-start budget
- Codex process recycling is exercised intentionally and does not break broker continuity

### Phase 2: Claude structured interactive transport

- build Claude on `stream-json`
- implement session resume policy
- parse structured events
- implement handoff extraction from structured output
- add budget and telemetry hooks
- if needed, add Agent SDK `PreCompact` observability without making it the continuity source
- treat resumed session IDs as opaque and update the broker's stored handle from the transport response
- parse live usage fields as the cost authority instead of relying on local CLI history logs

Exit condition:

- Claude can complete a relay turn through structured streaming without touching the TUI
- Claude rotation can happen before runaway resume cost becomes normal
- Claude resume drift or cache regressions are visible in telemetry instead of hidden
- Claude output/thinking-token ceilings can force rotation or downgrade before cost runs away

### Phase 3: Hybrid broker smoke test

- run `Codex -> Claude -> Codex` smoke path using the chosen hybrid runtime
- preserve event log and diagnostics parity with the default runtime
- verify operator-visible pause behavior
- test downgrade-to-bounded behavior if interactive fails

Exit condition:

- hybrid smoke test reaches `Accepted relays: 2`
- operator can tell why a pause or downgrade happened

### Phase 4: Operational hardening

- budget enforcement
- rotation implementation
- restart policy implementation
- health probes
- explicit unsupported-environment messages
- token/cost comparison against the bounded runtime
- soak testing
- protocol-drift detection in CI
- comparative rotation-harness testing against bounded mode

Exit condition:

- operator can understand and recover from the most common failure modes
- interactive does not obviously lose to bounded mode on cost or trust

## Success criteria

The hybrid runtime is not considered usable just because one smoke test passes.

Minimum bar:

- interactive smoke test reaches `Accepted relays: 2`
- approval or blocked-input pause can be surfaced and resumed
- mid-session transport death can be recovered by broker-driven restart from the last accepted handoff
- one 60-minute soak test completes without transport-host crash
- a token/cost comparison exists against the bounded runtime
- a latency comparison exists against the bounded runtime, including planned rotation overhead
- diagnostics remain at least as good as the current fallback runtime
- at least one forced interactive fault can be downgraded cleanly to the bounded runtime if that policy is enabled
- protocol drift breaks loudly through fixture/schema checks
- at least one planned rotation cycle preserves useful continuity through the broker-managed carry packet
- cache-regression detection fires on a synthetic regression case
- long-lived transport process recycling is proven under soak instead of assumed

## Kill switch

Do not let the interactive rebuild drag on indefinitely.

Abandon or pause the hybrid interactive track if either of these becomes true:

- the chosen non-TUI transport for a side is too unstable to trust operationally
- after a bounded implementation window, the hybrid path still cannot justify its extra complexity over the bounded runtime
- the token/cost profile is materially worse without a continuity benefit that operators actually value

Suggested quantitative triggers:

- protocol breakage across Codex minor-version upgrades happens more than twice in the target support window
- interactive cost exceeds bounded mode by more than `40%` on the soak harness without clear operator value
- planned rotation consistently breaches the configured cold-start latency budget without offsetting operator value
- long-lived transport processes exceed memory or stability budgets often enough that recycling makes the runtime untrustworthy

If the kill switch is triggered:

- keep the bounded non-interactive runtime as the production path
- stop trying to force deeper interactive support until a tool vendor exposes a better long-lived surface

## Risks

### Risk: Codex app-server is still unstable on Windows

Mitigation:

- pin versions
- keep schema fixtures
- use bounded runtime as fallback
- document unsupported Windows combinations explicitly

### Risk: Claude resume or cache behavior regresses and increases cost

Mitigation:

- track cache and cost metrics from day one
- rotate aggressively
- compare against bounded mode continuously
- do not trust hidden auto-compaction as the primary protection
- treat returned session IDs as hints, not continuity guarantees
- treat live usage events, not local history logs, as the budget authority

### Risk: output or thinking tokens dominate cost even when input growth looks safe

Mitigation:

- track output-token usage per turn and per session
- enforce explicit output ceilings
- force rotation or downgrade when output-heavy turns make the interactive path economically irrational

### Risk: the interactive path is more expensive than the fallback

Mitigation:

- measure cost and latency explicitly
- enforce budgets
- keep the bounded runtime available

### Risk: aggressive rotation damages wall-clock usability

Mitigation:

- measure cold-start latency explicitly
- tune rotation thresholds with both cost and latency in view
- warm or prepare replacement sessions only if the added complexity is justified by measurements

### Risk: long-lived background transports leak memory or processes on Windows

Mitigation:

- restart transport processes on a schedule
- monitor uptime and memory explicitly
- clean up orphans during planned recycle points
- keep the bounded runtime available when transport recycling itself becomes noisy

## Relevant precedents

The architecture pattern is not novel. It is close to existing orchestrator and handoff-driven systems:

- Anthropic Agent Teams
- `ccbridge`
- `metaswarm`
- `awslabs/cli-agent-orchestrator`
- `ComposioHQ/agent-orchestrator`

The common pattern is:

- disposable live sessions
- durable external coordination state
- explicit handoff or assignment packets
- bounded recovery and fallback instead of trusting hidden terminal memory

### Risk: Claude and Codex require fundamentally different control planes

Mitigation:

- accept asymmetric transports
- keep one broker and one handoff contract, not one forced transport implementation

## What should be deleted later

Once the hybrid interactive runtime works:

- retire `ProcessInteractiveRelaySession`
- retire `ProcessInteractiveRelaySessionFactory`
- retire the current line-oriented interactive parsing assumptions
- remove the current process-backed experimental wording from the UI

Do not delete the current non-interactive adapters. Keep them as fallback and diagnostics mode.

## Recommended immediate next action

Do not spend more time patching the redirected-stdio prototype.

Implement the fixed transport choices:

- Codex first via app-server
- Claude second via stream-json
- bounded runtime remains fallback
