# Relay App MVP Testing Checklist

## Fast smoke path

1. Launch the app.
2. Set `Working Directory` to `D:\dad-relay-mvp-temp`.
3. Click `Check Adapters`.
4. Confirm both sides are `Healthy`.
5. Click `Smoke Test 2`.
6. Expect:
   - `Result: PASS`
   - `Accepted relays: 2`
   - a `Claude handle`
   - a final `session.paused` with the smoke-test completion message

## First live relay path

1. Choose a real working directory.
2. Put key context in files or the initial prompt.
3. Start with one narrow task.
4. Click `Start Session`.
5. Click `Advance Once`.
6. Inspect:
   - `Status`
   - `Recent Events`
   - `Latest Accepted Handoff`
7. Only use `Auto Run 4` after the first live turn succeeds cleanly.

## MCP audit path

1. Use a workspace that actually has MCP configured for the target CLI.
2. For Codex, verify config first:
   - `codex mcp list`
   - `codex mcp get <server>`
3. For Claude, verify project-scoped MCP first in that workspace:
   - `claude mcp list`
   - `claude mcp get <server>`
4. Run a real read-only MCP call:
   - Codex example:
     `codex exec --json --cd "D:\Unity\card game" "Use the Unity MCP server if available. Call the simplest read-only Unity MCP operation you can find, such as a ping or telemetry/status check. After attempting the MCP call, return exactly the word ok."`
   - Claude example:
     `claude -p "Call the tool mcp__UnityMCP__manage_editor with action telemetry_ping. After the tool call completes, return exactly the word ok." --output-format stream-json --verbose`
5. Inspect the relay audit docs:
   - `mcp-audit.md`
   - `capability-matrix.md`
6. Expect:
   - direct `mcp__...` calls classify as `mcp`
   - `ListMcpResourcesTool` / `ReadMcpResourceTool` also classify as `mcp`
   - read-only MCP resource discovery and telemetry ping/status are auto-cleared by broker policy
   - other MCP activity still produces broker review items

## Shell / PowerShell audit path

1. Use the realistic TaskPulse workspace:
   - `D:\dad-relay-mvp-temp`
2. Launch the app and set:
   - `Working Directory` = `D:\dad-relay-mvp-temp`
   - interactive adapters enabled
3. Use a read-only shell prompt such as:
   ```text
   Read PROJECT-RULES.md first. Then use shell or PowerShell commands to inspect the TaskPulse repository without modifying files. Specifically: (1) run git status, (2) list src/api and tests, (3) print the first part of Document/api-spec.md, and (4) show the current branch and most recent commit. Summarize what you learned and hand off to the other side asking them to verify your shell-based inspection. Do not modify any project files.
   ```
4. Click `Start Session`, then `Advance Once`.
5. Inspect:
   - `Recent Events`
   - `Latest Tool Activity`
   - `Tool Category Summary`
   - `%LocalAppData%\RelayAppMvp\auto-logs\current-status.txt`
   - the latest `%LocalAppData%\RelayAppMvp\logs\*.jsonl`
6. Cross-check on disk:
   - `git -C "D:\dad-relay-mvp-temp" -c safe.directory=D:/dad-relay-mvp-temp status --short --branch`
   - `git -C "D:\dad-relay-mvp-temp" -c safe.directory=D:/dad-relay-mvp-temp log --oneline -1`
7. Expect:
   - shell commands are executed without modifying repository files
   - the broker records `shell.requested` and `shell.completed`
   - `Tool Category Summary` includes `shell`
   - Git may require a repo-local `safe.directory` override because of Windows sandbox ownership differences

## Git audit path (read-only)

1. Use the realistic TaskPulse workspace:
   - `D:\dad-relay-mvp-temp`
2. Launch the app and set:
   - `Working Directory` = `D:\dad-relay-mvp-temp`
   - interactive adapters enabled
3. Use a read-only git prompt such as:
   ```text
   Read PROJECT-RULES.md first. Then use read-only git commands to inspect the TaskPulse repository without modifying any files. Specifically run: git -c safe.directory=* status --short --branch, git -c safe.directory=* log --oneline -3, git -c safe.directory=* branch --show-current, git -c safe.directory=* diff --stat. Summarize the repository state and hand off to the other side asking them to verify the git inspection. Do not stage, commit, push, or run gh pr create.
   ```
4. Click `Start Session`, then `Advance Once`.
5. Inspect:
   - `Recent Events`
   - `Latest Tool Activity`
   - `Latest Git / PR Activity`
   - the latest `%LocalAppData%\RelayAppMvp\logs\*.jsonl`
6. Expect on Codex:
   - `shell.requested` and `shell.completed` for each git command
   - no broker approval prompt (read-only git is auto-allowed)
   - `Latest Git / PR Activity` panel may still show `No git or PR activity yet` — this is the known Windows/PowerShell-wrapping classifier gap recorded in `git-audit.md`
7. Expect on Claude (direct or relay):
   - raw `git ...` strings classify cleanly as `git` — the PowerShell-wrapping gap is Codex-specific
8. Cross-check on disk: repo should remain unchanged — `git -C "D:\dad-relay-mvp-temp" -c safe.directory=* status --short --branch` must still show a clean tree on `master`.

## What to watch for

- `Codex handle` appears but `Claude handle` does not:
  Codex did not produce a usable handoff yet.
- `repair.requested` appears once:
  The relay is still recoverable.
- repeated `repair.requested` followed by `session.paused`:
  The side is not producing a usable handoff and the repair budget is exhausted.
- `Export Diagnostics` is the right next step when the reason is not obvious from `Recent Events`.

## Practical guidance

- Do not rely on hidden context alone.
- Keep prompts bounded and explicit.
- Prefer one relay decision at a time over an open-ended autonomous run.
- Treat the handoff JSON and event log as the primary debugging surface.
- For shell and git inspection prompts in the temp workspace, verify repository-cleanliness claims on disk with a `safe.directory` override instead of trusting the relay summary alone.
