# MCP Audit

## Scope

This audit captures real local evidence for Phase C1:

- whether MCP servers are configured for Codex and Claude
- whether those servers load under actual CLI execution
- whether MCP calls succeed inside real agent turns
- whether the relay code can classify the resulting action signals

Audit date: 2026-04-17  
Primary validation workspace: `D:\Unity\card game`

## Configuration Discovery

### Codex

Global Codex config at `C:\Users\mizan\.codex\config.toml` contains:

- `mcp_servers.unityMCP`
- command: `C:\Users\mizan\.local\bin\uvx.exe`
- args: `--offline --from mcpforunityserver==9.6.6 mcp-for-unity --transport stdio`

Observed commands:

```powershell
codex mcp list
codex mcp get unityMCP
```

Observed result:

- `unityMCP` is `enabled`
- transport is `stdio`
- config resolves successfully from the local Codex profile

### Claude

In the prototype workspace `D:\dad-v2-system-template`, `claude mcp list` reports:

- no MCP servers configured

In `D:\Unity\card game`, Claude local project config exposes:

- `UnityMCP`
- status `Connected`

Observed commands:

```powershell
claude mcp list
claude mcp get UnityMCP
```

Observed result:

- prototype workspace: no Claude MCP servers
- Unity project workspace: `UnityMCP` connects successfully

## Real Turn Execution

### Codex CLI

Command executed:

```powershell
codex exec --json --cd "D:\Unity\card game" "Use the Unity MCP server if available. Call the simplest read-only Unity MCP operation you can find, such as a ping or telemetry/status check. After attempting the MCP call, return exactly the word ok."
```

Observed result from `tmp-codex-mcp-audit.jsonl`:

- `item.started` with `type: "mcp_tool_call"`
- server: `unityMCP`
- tool: `manage_editor`
- arguments: `{"action":"telemetry_ping"}`
- `item.completed` returned `{"success":true,"message":"telemetry ping queued"}`
- final agent output was `ok`

Conclusion:

- Codex can load the configured MCP server
- Codex can execute a read-only MCP tool inside a non-interactive turn
- the MCP call succeeded locally

### Claude CLI

Direct MCP tool command executed:

```powershell
claude -p "Call the tool mcp__UnityMCP__manage_editor with action telemetry_ping. After the tool call completes, return exactly the word ok." --output-format stream-json --verbose
```

Observed result from `tmp-claude-mcp-direct-audit.jsonl`:

- system init includes `mcp_servers:[{"name":"UnityMCP","status":"connected"}]`
- available tools include `mcp__UnityMCP__manage_editor`
- Claude emitted `tool_use` for `mcp__UnityMCP__manage_editor`
- input: `{"action":"telemetry_ping"}`
- tool result returned `{"success":true,"message":"telemetry ping queued"}`
- final result was `ok`

Additional resource-only command executed:

```powershell
claude -p "Use the UnityMCP server if available. Call the simplest read-only Unity MCP operation you can find, such as a ping or telemetry_status check. After attempting the MCP call, return exactly the word ok." --output-format stream-json --verbose
```

Observed result:

- Claude first used `ListMcpResourcesTool`
- resource enumeration for `UnityMCP` succeeded
- final result was `ok`

Conclusion:

- Claude can load project-scoped MCP servers
- Claude can execute both MCP resource-discovery tools and direct `mcp__...` tools inside `-p --output-format stream-json`
- direct tool invocation succeeded locally

## Relay Observation Mapping

### Codex

Current relay code paths:

- `RelayApp.Desktop/Adapters/CodexCliAdapter.cs`
- `RelayApp.Desktop/Interactive/CodexInteractiveAdapter.cs`

Current behavior:

- `mcp_tool_call` item types are classified through `RelayApprovalPolicy.ClassifyCodexItemCategory(...)`
- those items become category `mcp`
- broker therefore receives `tool.invoked` / `tool.completed` plus category-specific `mcp.requested` / `mcp.completed`

Status: verified in code and supported by the real CLI transcript above

### Claude

Current relay code path:

- `RelayApp.Desktop/Interactive/ClaudeInteractiveAdapter.cs`

Current behavior after this audit update:

- direct tool names starting with `mcp__` classify to category `mcp`
- MCP meta tools such as `ListMcpResourcesTool` and `ReadMcpResourceTool` now also classify to category `mcp`
- read-only MCP resource discovery and telemetry ping/status activity now auto-clear through broker policy instead of always raising operator review

Status: direct MCP tool calls are now broker-classifiable as `mcp`, and MCP resource discovery is no longer collapsed into generic `tool`

## Current Product Assessment

### Codex

- MCP configuration: available
- MCP load in tested workspace: available
- real MCP tool call: success
- relay observation path: implemented

Overall: `working`

### Claude

- MCP configuration in prototype workspace: not configured
- MCP configuration in tested Unity workspace: available
- real MCP tool call: success
- relay observation path: implemented for direct MCP tools and MCP resource-discovery tools

Overall: `conditionally working`

Condition:

- Claude MCP depends on workspace/project MCP configuration; it is not globally available in the prototype workspace today

## Follow-up Gaps

1. End-to-end relay-session verification is still missing.
   This audit used real CLI turns and relay code inspection, but not a fully automated WPF broker session that pauses on `mcp.review_required`.

2. MCP policy is still a review bridge, not true pre-execution broker approval.
   Current product behavior is:
   - observe MCP use
   - auto-clear read-only MCP discovery / telemetry checks through broker policy
   - raise a broker review item for other MCP activity
   - allow session-scoped operator approval

3. Claude MCP availability is workspace-dependent.
   The prototype workspace itself currently has no Claude MCP servers configured.
