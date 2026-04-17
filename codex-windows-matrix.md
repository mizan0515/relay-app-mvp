# Codex Windows Compatibility Matrix

Audit date: 2026-04-17
Platform: Windows 11 Pro 10.0.26200
Shell: PowerShell 5.1 (`C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe`)
Relay sandbox: Job Object with CPU/memory/process limits

This doc consolidates Windows-specific Codex behavior observed across the
shell / git / MCP / DAD-asset audits, so follow-up classifier and sandbox
work can target concrete, evidence-backed gaps rather than guesses.

## Method

Every row references a concrete live-session id (either WPF UI Automation
against the relay app, or a direct `codex exec --json` run). No row is based
on documentation or theory; each row is either **verified live** or
explicitly marked pending.

## Matrix

| Domain | Codex mechanism on Windows | Broker visibility | Evidence |
|---|---|---|---|
| Shell command | Wrapped as `powershell.exe -Command "<inner>"`. Inner string carries the actual intent. | `commandExecution` item with full wrapper + `commandActions[].type = "unknown"`. | `shell-audit-20260417-102903` |
| Read file | Wrapped as `powershell.exe -Command "Get-Content -Raw '<path>'"` — no dedicated read tool. | Appears as `commandExecution` → `shell` category, not `read`. | `dad-asset-qa-20260417-145500` |
| Write file | Native `fileChange` item (add/update/delete + unified diff). Not shell-wrapped. | `file.change.requested`/`.completed` with path + diff; no approval round-trip under auto-approve after iteration-5 fix. | `dad-asset-qa-20260417-145500` |
| Directory create | Silent — folds into the first `fileChange` write under the new directory. | Not independently observable. | `dad-asset-qa-20260417-145500` (`notes/` auto-created with `audit-marker.md` add) |
| Git read-only (`status`/`log`/`diff`/`branch`/`show`) | Each command wrapped as `powershell.exe -Command "git …"`. | After iteration-3 classifier fix: `git.requested`/`.completed` with category `git`. | `git-classify-qa-20260417-115929` |
| `git add` | Wrapped `git add`. Runs inside Codex sandbox without routing to broker approval. | `git.add.requested`/`.completed` exit 0. No approval event. | `destructive-qa-20260417-131500` |
| `git commit` | Wrapped `git commit`. Runs inside Codex sandbox without routing to broker approval. | `git.commit.requested`/`.completed`. No approval event. | `destructive-qa-20260417-131500` |
| `git push` | Wrapped `git push`. **Escalated as `item/commandExecution/requestApproval`** — the only git subcommand Codex currently escalates on Windows. | `git.push.requested` + `approval.requested` + `approval.queue.enqueued` + `git.push.completed`. Auto-resolved after iteration-5 server-side fix. | `auto-approve-push-qa-20260417-143000` |
| `git push` sandbox networking | Forks `sh.exe` for msys pipe setup; pipe creation **fails** under the relay's Job Object sandbox, producing exit 1 on push. | Broker records completion with non-zero exit; no approval failure — purely a sandbox/networking gap. | `auto-approve-push-qa-20260417-143000` |
| `gh pr create` | Wrapped `gh pr create`. Classifier routes to `pr` category. | `pr.requested`/`.completed` expected; live confirmation pending a real remote. | Pending |
| MCP config discovery (Codex) | `codex mcp list` / `codex mcp get <name>` returns enabled servers from Codex config. | Works. | `mcp-audit.md` |
| MCP tool call | Native `tool.invoked` / `tool.completed` — not shell-wrapped. | Broker classifies as `mcp`; read-only resource discovery + telemetry ping auto-clear, other MCP activity still pauses for review (post-iteration-2 policy). | `mcp-audit.md` |
| Approval round-trip (auto-mode) | Server-originated `item/…/requestApproval` must be replied to with `accept`/`cancel` (not `acceptForSession`/`decline`). | Fixed in iteration 5: adapter resolves auto-approve synchronously and emits the correct `accept` decision. | `auto-approve-push-qa-20260417-143000` |
| Working directory | Passed via `--cd <path>`. Forward slashes in payloads are fine; mixed separators appear in `fileChange.path` (e.g. `D:\\dad-relay-mvp-temp\\Document/dialogue/...`). | Cosmetic only — broker treats them as the same file. | `dad-asset-qa-20260417-145500` |
| CLI output format | `codex exec --json` produces line-delimited JSON. stderr carries deprecation / compat warnings but does not fail the turn. | Broker logs adapter stderr at `adapter.diagnostics`. | All sessions |
| Telemetry / pricing | `codex.pricing.fallback` event observed — price table not yet resolved for current model. | Cosmetic. Does not affect turn success. | `dad-asset-qa-20260417-145500` |

## Windows-specific gaps (with file:line pointers where applicable)

1. **Reads collapse into `shell`.** Codex has no "read_file" primitive on
   Windows; it uses `powershell.exe -Command "Get-Content …"`. Broker
   classifier has no case for this pattern.
   - Follow-up: extend `ClassifyCommandCategory` to match `Get-Content` /
     `cat` / `type <path>` inside already-unwrapped PowerShell payloads.
     File: [RelayApprovalPolicy.cs](prototypes/relay-app-mvp/RelayApp.Desktop/Approvals/RelayApprovalPolicy.cs) (source not tracked in git).

2. **`git add` / `git commit` don't escalate.** Codex runs them inside its
   sandbox without a broker approval round-trip on Windows. Only `git push`
   is escalated. This is a Codex-side policy, not a relay gap, but worth
   documenting: destructive add/commit cannot be blocked by the broker on
   Windows once the turn starts.

3. **msys/sh.exe pipe-creation fails under Job Object sandbox.** Any git
   operation that forks `sh.exe` (notably `git push` via HTTPS) hits a pipe
   setup failure inside the relay sandbox. `git add`/`git commit` are not
   affected because they don't fork `sh.exe`.

4. **No DAD-asset band.** Writes under `Document/dialogue/**` are classified
   identically to any other `fileChange`. DAD-runtime-critical artifacts
   (backlog / state / sessions) deserve their own band.

5. **Silent directory creation.** `fileChange` writes auto-create parent
   directories with no separate broker-observable event, so directory
   scaffolding under DAD roots cannot be governed independently.

## What's known-good on Windows

- Shell, git (read-only + add/commit/push), MCP telemetry/resource
  discovery, file writes (add/update/delete), auto-approve round-trip,
  handoff to Claude — all verified live in 2026-04-17 sessions.
- Classifier correctly unwraps `powershell.exe -Command`, `pwsh -Command`,
  and `cmd /c` wrappers; strips `git -c key=value` / `git -C <path>` option
  pairs before subcommand matching.

## Next-audit candidates

- `gh pr create` end-to-end against a real GitHub remote.
- Direct Claude parity pass for the destructive git tier (currently
  "pending live" in `capability-matrix.md`).
- UI refresh timing during long-running turns (flagged in shell audit).
