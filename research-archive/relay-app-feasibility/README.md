# Relay App Feasibility Research

Date: 2026-04-15

## Goal

Evaluate whether a Windows app can fully automate a DAD-style relay between Codex Desktop and Claude Code/Desktop by:

- reading the handoff prompt from one side
- pasting it into the other side
- detecting when the handoff is missing
- issuing a repair prompt to force handoff output
- preserving long-lived interactive sessions instead of spawning fresh CLI-to-CLI calls that lose session compaction value

## Short answer

Yes, a Windows relay app is feasible.

But the best design is not "pure UI macro automation between two desktop windows."

The better design is:

- a local relay broker app as the source of truth
- Codex controlled primarily through `codex app-server` or `codex exec --json`
- Claude controlled primarily through `claude` CLI session surfaces (`-p`, `stream-json`, `resume`, hooks) and only secondarily through desktop UI
- UI automation used only where no better control surface exists

In other words: build a Windows app, but make it a protocol broker with selective UI automation, not a brittle click bot.

## Why this changed from the first intuition

The original idea assumes both sides must be driven through visible desktop windows because direct CLI-to-CLI relay loses useful interactive-session behavior such as compaction and persistent context.

Research shows that assumption is only partly true.

Codex and Claude now expose enough automation surface that you can often keep long-lived sessions without simulating every keystroke:

- Codex has a documented App Server protocol with threads, turns, streaming items, approvals, and steering.
- Codex also has non-interactive JSONL output for machine-readable automation.
- Claude Code has print mode, JSON and stream-json output, session resume, auth status, hooks, and remote-control mode.

This means the relay problem is no longer "Can Windows click between two apps?" It is "Which parts need UI automation, and which parts should use the native protocol?"

## Key findings

### 1. Codex already exposes a broker-friendly control plane

OpenAI documents `codex app-server` as the interface used for rich clients and deep integrations, including authentication, conversation history, approvals, and streamed agent events.

Important capabilities from the official docs:

- `thread/start`, `thread/resume`, `thread/fork`
- `turn/start`, `turn/steer`, `turn/interrupt`
- event streaming for `thread/*`, `turn/*`, and `item/*`
- approval requests as server-initiated RPC calls
- JSON-RPC over `stdio` or experimental WebSocket

This is strong evidence that a custom Windows relay app does not need to scrape the Codex Desktop UI to know:

- whether a turn started
- whether the turn completed
- what the latest agent message is
- when approvals are pending

If you still want the Codex Desktop window for human visibility, your app can treat the window as a dashboard and the app-server as the real control API.

### 2. Codex non-interactive mode is script-grade

OpenAI documents `codex exec --json` as a JSONL event stream with:

- `thread.started`
- `turn.started`
- `turn.completed`
- `turn.failed`
- `item.*`
- agent messages
- command executions
- file changes
- plan updates

That is enough for a broker to drive machine-readable fallback flows and recovery flows.

### 3. Codex hooks are promising, but Windows is a blocker today

OpenAI documents Codex hooks with `SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, and `Stop`.

The important relay idea would be:

- inspect the last assistant message at `Stop`
- if no valid handoff block exists, return `decision: "block"` with a reason like "Emit the DAD handoff block before stopping"

However, the current official docs explicitly say:

- hooks are experimental
- Windows support is temporarily disabled
- hooks are currently disabled on Windows

So if the target machine is Windows-first, do not make Codex hook-based self-repair your primary design assumption.

### 4. Claude Code is also broker-friendly

Anthropic documents these surfaces in the Claude Code CLI:

- `claude -p "query"` for print mode
- `--output-format json`
- `--output-format stream-json`
- `--include-hook-events`
- `--resume` / `--continue`
- `claude auth status`
- `claude remote-control`

This means the relay app can:

- keep a stable Claude session identity
- detect auth state
- request machine-readable output
- receive hook signals
- resume a named session instead of recreating context from scratch

### 5. Claude hooks are more automation-ready than Codex hooks on Windows

Anthropic documents rich hook behavior, including:

- `Stop`
- `SubagentStop`
- `PermissionRequest`
- `Notification`
- prompt-based hooks
- agent-based hooks

For relay automation, the most important part is that a `Stop` hook can block stopping and continue the conversation with a reason. The docs also expose `last_assistant_message` and `stop_hook_active`, which are exactly the fields you would want for a "handoff was missing, force one more pass" repair loop.

So Claude can self-repair missing handoff output more directly than Codex on Windows.

### 6. Windows UI automation is feasible, but fragile by nature

Microsoft documents UI Automation as the standard accessibility and automation framework for Windows desktop UI. Microsoft also documents that:

- UIA selectors are preferred for modern apps
- multiple selectors should be kept as fallback
- text-based selectors are often more resilient
- selector failure is common enough that repair and alternative approaches are first-class topics
- when selectors fail, fallback may require image automation, mouse/keyboard actions, or OCR

This is useful for a relay app because it confirms:

- building a desktop bot is technically normal on Windows
- selector drift is a predictable maintenance burden
- you should design for fallback, not assume a single stable selector forever

### 7. pywinauto is viable, but it should not be the core architecture

The official pywinauto docs confirm:

- `Application(backend="uia")` style control
- connect-by-process, handle, path, or title
- access to control hierarchies
- UIA visibility for elements without traditional Win32 handles

This makes pywinauto a valid implementation tool for the Windows app if you decide to automate visible windows.

But it should remain an adapter layer, not the system core.

## What the relay app should actually be

### Recommended architecture

Use a three-layer design:

1. Session broker
2. Native adapters
3. UI fallback adapters

#### 1. Session broker

The broker owns:

- session state
- turn numbers
- which side is active
- timeout policy
- repair policy
- audit log
- duplicate suppression
- crash recovery

The broker should be the only place that decides whether:

- a handoff is valid
- a turn is complete
- a repair prompt is needed
- a relay should continue or stop

#### 2. Native adapters

Preferred first:

- Codex adapter via `codex app-server`
- optional Codex fallback via `codex exec --json`
- Claude adapter via `claude` CLI JSON or stream-json session surfaces

These adapters should produce normalized events such as:

- `TurnStarted`
- `AssistantDelta`
- `AssistantCompleted`
- `ApprovalRequested`
- `NeedsRepair`
- `SessionError`

#### 3. UI fallback adapters

Only for tasks that the native adapters cannot do.

Examples:

- bring a visible desktop window forward
- paste a prompt into a chat box that lacks a stable protocol surface
- read the visible rendered handoff block when no machine-readable stream exists

Implementation candidates:

- C# with `System.Windows.Automation`
- pywinauto as a prototype tool
- OCR fallback only when selector access fails

## The protocol you should enforce

The broker will be unreliable unless the agents emit a strict handoff format.

The relay app should require a machine-detectable block such as:

```text
===DAD_HANDOFF_START===
target: claude
session_id: 2026-04-15-foo
turn: 8
ready: true
prompt:
Read PROJECT-RULES.md first. Then read ...
===DAD_HANDOFF_END===
```

A better version is JSON:

```json
{
  "type": "dad_handoff",
  "target": "claude",
  "session_id": "2026-04-15-foo",
  "turn": 8,
  "ready": true,
  "prompt": "Read PROJECT-RULES.md first..."
}
```

The broker should never infer handoff from "looks like a prompt." It should only accept one explicit schema.

## How missing handoff should work

The repair loop should be deterministic:

1. Wait for turn completion.
2. Parse final assistant output.
3. If a valid handoff block exists, relay it.
4. If no valid handoff block exists, send a repair prompt.
5. If repair succeeds, relay.
6. If repair fails twice, stop and surface the session to the human.

Good repair prompts are narrow:

- "Output only the DAD handoff block for the next agent."
- "Do not restate analysis. Emit exactly one valid handoff block."

This is much safer than asking the model to think again.

## Why pure desktop-to-desktop automation is the wrong v1

If you build only a visible-window shuttle, you inherit all of these problems at once:

- selector drift after app updates
- focus theft
- hidden side panels
- partial rendering
- scroll position ambiguity
- duplicated paste/submit
- race conditions between token streaming and final completion
- OCR ambiguities
- no durable replayable audit stream

That does not mean desktop automation is impossible. It means desktop automation should be the thinnest possible layer.

## Best implementation direction for a Windows app

### Suggested stack

- UI shell: WPF or WinUI 3
- Broker core: C# async state machine
- Persistence: SQLite or JSONL audit log
- Codex adapter: child process + JSON-RPC transport to `codex app-server`
- Claude adapter: child process + `claude` CLI JSON or stream-json
- UI fallback: `System.Windows.Automation`, optionally pywinauto during prototyping
- OCR fallback: optional, not in v1

### Why C# is the best fit

- native access to Windows UI Automation
- straightforward process supervision
- easy named-pipe, stdio, and JSON streaming support
- good packaging for a Windows desktop app

## Feasibility by option

### Option A: pure UI relay

Feasible: yes

Recommended: no

Use only if:

- you insist on driving two visible desktop apps
- no supported automation surface is available

Main risk:

- high maintenance cost

### Option B: hybrid relay broker

Feasible: yes

Recommended: yes

Use:

- Codex app-server or exec JSON
- Claude CLI JSON or stream-json
- UI automation only for visual assist or missing surfaces

Main benefit:

- best balance of reliability and session continuity

### Option C: fully headless relay

Feasible: mostly yes

Recommended: maybe later

Use only if:

- you accept giving up the visible desktop-centric workflow
- both sides can be controlled through protocol surfaces alone

Main limitation:

- it changes the user experience more than your original idea intended

## Specific risks you should expect

### 1. Session semantics mismatch

Codex thread semantics and Claude session semantics are not identical.

Your broker must normalize:

- new session
- resume
- fork
- interrupt
- repair continuation

### 2. Endless repair loops

If "missing handoff" triggers another turn and that turn also misses handoff, you can get infinite loops.

Mitigation:

- repair budget per turn
- `stop_hook_active` style guard where available
- broker-level max-repair counter

### 3. Approval deadlocks

Both systems can pause for permissions or approvals.

Mitigation:

- explicit approval state in broker
- never assume silence means completion
- surface approvals in the app UI

### 4. Duplicate relay

If the same handoff is seen twice during retry or reconnect, the next agent may receive the same prompt twice.

Mitigation:

- hash the accepted handoff block
- store `(session_id, turn, target, hash)` and reject duplicates

### 5. Windows-only asymmetry

Codex hook repair is not a safe Windows assumption today because official docs say hooks are disabled on Windows.

Mitigation:

- make broker-driven repair the primary path
- treat Codex hooks as future optimization only

## MVP recommendation

Build this first:

- one local Windows app
- one active DAD session at a time
- Codex controlled by app-server
- Claude controlled by CLI JSON mode plus resume
- strict handoff JSON block
- repair loop with max 2 retries
- audit log for every turn, repair, and relay
- optional button to open the matching visible app/session

Do not build these in v1:

- OCR-based scraping
- automatic multi-session scheduling
- self-healing selector AI
- desktop-only full control of both sides

## Final recommendation

Build the Windows app.

But build it as a relay broker that speaks the native automation surfaces first, then falls back to desktop automation only where necessary.

If you instead build a window-clicker first, you will spend most of your time fighting selectors, focus bugs, and ambiguous completion detection. If you build the broker first, the remaining UI automation becomes small and tractable.

## Sources

OpenAI:

- Codex app-server docs: https://developers.openai.com/codex/app-server
- Codex non-interactive mode docs: https://developers.openai.com/codex/noninteractive
- Codex hooks docs: https://developers.openai.com/codex/hooks
- Codex app automations docs: https://developers.openai.com/codex/app/automations

Anthropic:

- Claude Code CLI reference: https://code.claude.com/docs/en/cli-reference
- Claude Code hooks reference: https://code.claude.com/docs/en/hooks
- Claude Code hooks guide: https://code.claude.com/docs/en/hooks-guide

Microsoft / Windows automation:

- UI Automation overview: https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview
- Power Automate UI elements: https://learn.microsoft.com/en-us/power-automate/desktop-flows/ui-elements
- Power Automate custom selectors: https://learn.microsoft.com/en-us/power-automate/desktop-flows/build-custom-selectors
- Selector failure troubleshooting: https://learn.microsoft.com/en-us/troubleshoot/power-platform/power-automate/desktop-flows/ui-automation/failed-get-ui-element

Windows automation library:

- pywinauto docs: https://pywinauto.readthedocs.io/en/latest/HowTo.html
