# Git Workflow Audit

## Scope

This audit captures real local evidence for Phase C3:

- whether Codex and Claude can execute read-only git commands (`status`, `log`, `branch`, `diff`) against a realistic DAD workspace
- how the relay broker classifies and records those git operations when they arrive through the interactive transport
- where destructive or remote-visible git operations (`git add`, `git commit`, `git push`, `gh pr create`) sit in the current approval policy surface

Audit date: 2026-04-17
Primary validation workspace: `D:\dad-relay-mvp-temp`

## Tested Scenarios

### 1. Real relay session through the WPF app

Runtime:

- desktop app launched with interactive adapters enabled
- working directory set to `D:\dad-relay-mvp-temp`
- session id: `git-audit-20260417-111226`

Prompt used:

```text
Read PROJECT-RULES.md first. Then use read-only git commands to inspect the TaskPulse repository without modifying any files. Specifically run: git -c safe.directory=* status --short --branch, git -c safe.directory=* log --oneline -3, git -c safe.directory=* branch --show-current, git -c safe.directory=* diff --stat. Summarize the repository state and hand off to the other side asking them to verify the git inspection. Do not stage, commit, push, or run gh pr create.
```

Observed result from the live app:

- Codex Turn 1 completed successfully and the broker accepted the resulting handoff to Claude
- `Status` panel reported `Runtime: INTERACTIVE`, `Codex approval path: broker-routed`, `Claude approval path: audit-only`
- no broker approval was requested (read-only git commands are auto-cleared by policy)
- JSONL log recorded 11 `shell.requested` + 11 `shell.completed` events and one `handoff.accepted`
- the handoff summary from Codex accurately reported branch `master`, a clean status (`## master`), latest commit `4d5fb92`, and empty `git diff --stat`

Evidence files:

- `%LocalAppData%\RelayAppMvp\auto-logs\current-status.txt`
- `%LocalAppData%\RelayAppMvp\logs\git-audit-20260417-111226.jsonl`

Key per-command evidence extracted from the event log (payloads are PowerShell-wrapped):

- `"C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe" -Command 'git -c safe.directory=* rev-parse --show-toplevel'`
- `"C:\WINDOWS\...\powershell.exe" -Command 'git -c safe.directory=* status --short --branch'`
- `"C:\WINDOWS\...\powershell.exe" -Command 'git -c safe.directory=* log --oneline -3'`
- `"C:\WINDOWS\...\powershell.exe" -Command 'git -c safe.directory=* branch --show-current'`
- `"C:\WINDOWS\...\powershell.exe" -Command 'git -c safe.directory=* diff --stat'`

Conclusion:

- a real relay session can drive read-only git inspection on the TaskPulse workspace
- the broker observes every git command at the action level and records requested/completed events
- the interactive Codex approval path is wired, but no approval fires for read-only git commands because the policy auto-allows them

### 2. Direct Codex git inspection

Command executed from `D:\dad-v2-system-template\prototypes\relay-app-mvp`:

```powershell
codex exec --json --cd "D:/dad-relay-mvp-temp" "Run read-only git commands on this TaskPulse repo: (1) git -c safe.directory=* status --short --branch, (2) git -c safe.directory=* log --oneline -3, (3) git -c safe.directory=* branch --show-current, (4) git -c safe.directory=* diff --stat. Do not modify any files. After running, return exactly the word ok."
```

Observed result from `tmp-codex-git-audit.jsonl`:

- each command was issued as a `command_execution` item wrapped in `powershell.exe -Command '...'`
- every command completed with `exit_code: 0`
- final `agent_message` was exactly `ok`
- reported facts matched the workspace: branch `master`, clean status, single commit `4d5fb92`, empty diff

### 3. Direct Claude git inspection

Command executed:

```powershell
claude -p "Read PROJECT-RULES.md first. Then run these read-only git commands on the TaskPulse repo at D:\dad-relay-mvp-temp (use 'git -C D:/dad-relay-mvp-temp -c safe.directory=* <cmd>'): (1) status --short --branch, (2) log --oneline -3, (3) branch --show-current, (4) diff --stat. Do not modify files. After completing the commands, return exactly the word ok." --output-format stream-json --verbose
```

Observed result from `tmp-claude-git-audit.jsonl`:

- Claude issued a single compound Bash tool call chaining the four git commands with `&&`
- Bash call completed and final text was exactly `ok`
- Claude attempted `Read` on a PROJECT-RULES.md located in the launch directory and was permission-denied once; this did not affect the git work because the git commands themselves ran under Bash

Conclusion:

- Claude can execute read-only git inspection in the realistic TaskPulse workspace
- Claude's structured Bash tool calls are the same source shape the interactive Claude adapter classifies into broker-visible categories

## Approval Surface Assessment

Policy source: `RelayApp.Core/Policy/RelayApprovalPolicy.cs`

Observed from the current policy and the live relay session:

| Git operation | Raw command category | Default policy outcome |
|---|---|---|
| `git status` / `git diff` / `git log` | `git` | auto-allowed once (read-only inspection) |
| `git add ...` | `git-add` | operator approval required (risk: medium) |
| `git commit ...` | `git-commit` | operator approval required (risk: medium) |
| `git push ...` (normal) | `git-push` | operator approval required (risk: high) |
| `git push ...` to `main`/`master`/`release/*` | `git-push` | blocked by default (risk: critical) |
| `git push --force` / `git push -f` | `git-push` | blocked by default (risk: critical) |
| `git reset --hard` | `git` (destructive) | blocked by default |
| `gh pr create ...` | `pr` | operator approval required |

In this audit slice only the read-only tier was exercised end-to-end through the relay, so the destructive-tier behavior above is documented from policy truth rather than a live denial.

## Windows-Specific Findings

### Codex wraps every command in powershell.exe

In the interactive Codex app-server transport, every command executed on Windows arrives at the broker wrapped in:

```text
"C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe" -Command '<actual command>'
```

The current `RelayApprovalPolicy.ClassifyCommandCategory` only inspects the outer command string. It checks `StartsWith("git ")` / `StartsWith("gh pr create")` / `StartsWith("git push")`, so PowerShell-wrapped git invocations do not match those prefixes and fall through to the `shell` branch.

Observed consequence in this audit:

- the `LatestGitActivityTextBox` UI panel showed `No git or PR activity yet` even though the session executed five concrete `git ...` commands
- the `Tool Category Summary` credited the work to `shell`, not `git`
- this is harmless for read-only inspection (policy still auto-allows) but means the broker's git-specific approval classes (`git-commit`, `git-push`, `git-add`, `pr`) are currently unreachable for any git work Codex drives on Windows

This is a real product gap, captured here as a follow-up for the approval-surface work rather than fixed in this audit slice.

**Resolved in follow-up commit.** `RelayApprovalPolicy.ClassifyCommandCategory` now unwraps `"...\powershell.exe" -Command '<inner>'`, `pwsh -Command '<inner>'`, and `cmd /c <inner>` before matching, and it also strips `git -c key=value` / `git -C <path>` option pairs before inspecting the subcommand. The Codex adapter additionally refines `commandExecution` item categorization from the generic `shell` class to the specific git class when the wrapped inner command warrants it. Verified live in QA session `git-classify-qa-20260417-115929`: five wrapped `git ...` commands produced five `git.requested` / five `git.completed` events and the `Latest Git / PR Activity` panel surfaced the activity for the first time on Codex/Windows.

### Git safe.directory friction still applies

Consistent with the C2 shell audit, all git commands in this audit used an explicit `-c safe.directory=*` override to work around the sandbox-vs-repo-owner mismatch on Windows. Without that override, `git status` fails with "dubious ownership" and the relay surfaces the failure through `shell.completed` with non-zero exit — not as a broker policy denial.

## Relay Observation Mapping

### Codex

Observed in the live relay session:

- `tool.invoked` and `tool.completed` for every command execution item
- `shell.requested` and `shell.completed` for each command (including the five git calls)
- no `git.requested` / `git.commit.requested` / `git.push.requested` events because the classifier never saw a raw `git` prefix
- `handoff.accepted` at the end of the turn

Status: read-only git inspection is verified end-to-end. Git-specific categorization on Windows is a gap.

### Claude

Observed in the direct CLI transcript:

- Claude's Bash calls include the raw `git -C ... -c safe.directory=* <cmd>` string directly
- in the interactive relay, those raw strings would classify as `git` and the specific subcategories would match (`git-add`, `git-commit`, `git-push`, `pr`) without the PowerShell wrapping issue
- per the current product decision Claude is audit-only, so these events would be logged but not broker-gated

Status: git execution is verified directly; relay-side end-to-end git evidence on Claude was not exercised in this audit slice.

## Current Product Assessment

### Codex

- read-only git inspection: working
- broker-routed approval for destructive git / push / PR creation: implemented by policy but currently unreachable on Windows because of the PowerShell-wrapping classifier gap
- classification of git operations as `shell`: observed; read-only tier still safe because `shell` auto-allows for these inspection commands

Overall: `partial`

### Claude

- read-only git inspection: working
- broker-routed approval for git: not applicable by design (audit-only)

Overall: `working` (within audit-only scope)

### Relay / Broker

- git activity observation as a generic shell event: working
- git-specific category classification on Codex/Windows: gap (PowerShell wrapping is not unwrapped before classification)
- policy-level handling of destructive git operations: implemented in `RelayApprovalPolicy` but not exercised live in this audit

Overall: `partial`

## Follow-up Gaps

1. `RelayApprovalPolicy.ClassifyCommandCategory` should detect `git`/`gh pr create` invocations even when wrapped in `powershell.exe -Command '...'` or `cmd /c ...`. Otherwise the destructive-tier git approval flow stays unreachable for Codex on Windows in practice.

2. Live exercise of `git add` / `git commit` / `git push` / `gh pr create` through the relay (and observation of the corresponding `git.add.requested` / `git.commit.requested` / `git.push.requested` / `pr.requested` events and operator approval UI) is still pending. This audit slice deliberately stayed read-only to match the prompt contract.

**Resolved (partial) in destructive-tier live exercise.** WPF-driven QA session `destructive-qa-20260417-131500` against a disposable branch `audit/destructive-20260417` of the TaskPulse workspace, with `AutoApproveAllRequests=true`, produced the following broker-observed events for the wrapped PowerShell git invocations:

- `git.add.requested` + `git.add.completed` (exit 0) for `git -c safe.directory=* add AUDIT-DESTRUCTIVE.md` — Codex's sandbox executed the add directly; no `approval.requested` event reached the broker.
- `git.commit.requested` + `git.commit.completed` for `git -c safe.directory=* commit -m 'audit: destructive tier marker'` — Codex's sandbox executed the commit directly; no `approval.requested` event reached the broker.
- `git.push.requested` + `approval.requested` + `approval.queue.enqueued` + `git.push.completed` (status `declined`, `exec command rejected by user`) for `git -c safe.directory=* push origin audit/destructive-20260417` — Codex's sandbox correctly escalated the push as a server `item/commandExecution/requestApproval`, which the broker classified and enqueued. The approval was not resolved within the turn window, so Codex received a decline and the push did not reach the remote.

Key product findings from this exercise:

- The classifier fix is effective end-to-end: destructive git commands surface as the specific `git-add` / `git-commit` / `git-push` events on Codex/Windows despite the PowerShell wrapper.
- Codex-side sandbox policy decides whether the broker sees an `approval.requested`. For `git add` and `git commit`, Codex runs them directly without asking. Only `git push` (and presumably `gh pr create`) escalates to the broker.
- `AutoApproveAllRequests=true` did not auto-resolve the approval on this branch. The push approval enqueued and then received the default-deny path after the Codex server-side turn timed out. This is a real product gap: the auto-approve flag is not currently honoured for server-originated `item/commandExecution/requestApproval` events in the Codex interactive transport. Fix candidate: have the broker's approval handler respond with `accept` immediately when `AutoApproveAllRequests` is set, before the operator UI has had a chance to act.

**Resolved in follow-up commit.** Two related defects were fixed in `RelayApp.Desktop/Interactive/CodexInteractiveAdapter.cs`:

1. Protocol decision string. `BuildServerRequestResponse` was mapping `RelayApprovalDecision.ApproveForSession` to `"acceptForSession"`, but Codex's `item/commandExecution/requestApproval` only advertises `{accept, acceptWithExecpolicyAmendment, cancel}` in its `availableDecisions`. The unknown decision value was treated as a decline, producing `exec command rejected by user` even when the relay had already auto-approved. `ApproveForSession` now maps to `accept` for command and file-change approvals (session-scope approval is still expressed via `permissions/requestApproval`'s `scope = "session"`).
2. UI-thread race. When `AutoApproveAllRequests` was on, the adapter offloaded the whole approval decision to `HandlePendingApprovalAsync` on the UI thread, which awaited `EnqueueApprovalAsync` before returning. That gave Codex enough time (~200ms) to emit a decline before the reply reached the socket. The adapter now resolves `autoApproveAll` synchronously in `HandleServerRequestAsync`, emits `approval.requested` + `approval.auto_mode.applied` + `approval.granted` into the observed actions, and fires the queue-persist work on a background task. QA session `auto-approve-push-qa-20260417-143000` confirmed the `git push` command now actually executes under auto-approve (exit 1 returned from the push itself was an unrelated msys/sh.exe pipe-creation issue on this Windows sandbox, not an approval decline).
- `gh pr create` live exercise was not run in this slice; the push path already demonstrates the approval-escalation behavior, and PR creation would need a live GitHub remote, which this disposable local bare-repo setup does not provide.

3. The `LatestGitActivityTextBox` UI panel's "No git or PR activity yet" message during a session that actually ran five git commands is consistent with the classifier gap above but should be revisited after the classifier is fixed, to make sure the panel reflects reality for Codex on Windows.
