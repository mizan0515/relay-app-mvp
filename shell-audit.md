# Shell / PowerShell Audit

## Scope

This audit captures real local evidence for Phase C2:

- whether Codex and Claude can execute read-only shell or PowerShell inspection commands
- whether the relay broker records shell activity during a real interactive session
- what Windows-specific constraints appeared during the audit

Audit date: 2026-04-17  
Primary validation workspace: `D:\dad-relay-mvp-temp`

## Tested Scenarios

### 1. Real relay session through the WPF app

Runtime:

- desktop app launched with interactive adapters enabled
- working directory set to `D:\dad-relay-mvp-temp`
- session id: `shell-audit-20260417-102903`

Prompt used:

```text
Read PROJECT-RULES.md first. Then read CLAUDE.md and DIALOGUE-PROTOCOL.md. Inspect the current state of src/api/tasks.py and src/services/task_service.py. Summarize the API capabilities and any gaps versus Document/api-spec.md. Hand off to the other side asking them to verify your analysis.
```

Read-only shell inspection prompt used for the successful audit run:

```text
Read PROJECT-RULES.md first. Then use shell or PowerShell commands to inspect the TaskPulse repository without modifying files. Specifically: (1) run git status, (2) list src/api and tests, (3) print the first part of Document/api-spec.md, and (4) if available, run a simple multi-step command that shows the current branch and the most recent commit. Summarize what you learned about the repository and hand off to the other side asking them to verify your shell-based inspection. Do not modify any project files.
```

Observed result from the live app:

- the session advanced successfully on Codex
- the broker accepted the resulting handoff to Claude
- the app showed no approval requests
- the current status snapshot reported:
  - `Status: Active`
  - `Active side: Claude`
  - `Current turn: 2`
  - `Tool Category Summary: shell: completed=8, requested=8`
- the JSONL event log recorded shell events and final handoff acceptance

Observed files:

- `%LocalAppData%\RelayAppMvp\auto-logs\current-status.txt`
- `%LocalAppData%\RelayAppMvp\logs\shell-audit-20260417-102903.jsonl`

Key evidence from the event log:

- `shell.requested`
- `shell.completed`
- `turn.completed`
- `handoff.accepted`

Conclusion:

- a real relay session can drive read-only shell inspection on the TaskPulse workspace
- the broker records shell activity at the action level
- the desktop UI and auto-log output surface the result clearly enough for operator verification

### 2. Direct Claude shell execution

Command executed from `D:\dad-relay-mvp-temp`:

```powershell
claude -p "Read PROJECT-RULES.md first. Then use shell or PowerShell commands to inspect the TaskPulse repository without modifying files. Specifically: (1) run git status using a safe.directory override if needed, (2) list src/api and tests, (3) print the first part of Document/api-spec.md, and (4) show the current branch and most recent commit. After the shell work completes, return exactly the word ok." --output-format stream-json --verbose
```

Observed result from `tmp-claude-shell-audit.jsonl`:

- Claude read `PROJECT-RULES.md` first
- Claude used Bash tool calls for repository inspection
- Claude executed:
  - `git -c safe.directory='*' status`
  - `git -c safe.directory='*' branch --show-current`
  - `git -c safe.directory='*' log -1 --oneline`
  - directory listing for `src/api` and `tests`
  - a read of `Document/api-spec.md`
- final result was exactly `ok`

Concrete evidence returned by the tool calls:

- branch: `master`
- worktree: clean
- latest commit: `4d5fb92 Initial TaskPulse project with DAD v2 template`
- `src/api`: `__init__.py`, `schemas.py`, `tasks.py`, `users.py`
- `tests`: `__init__.py`, `test_task_service.py`

Conclusion:

- Claude can execute read-only shell inspection successfully in the realistic TaskPulse workspace
- Claude follows the `Read PROJECT-RULES.md first` contract in this scenario

## Windows-Specific Findings

### Git safe.directory friction exists under relay sandboxing

In the real relay session, Codex first attempted:

```powershell
git status --short --branch
```

Observed result:

- command failed with Git dubious ownership protection
- the sandbox user differed from the repository owner
- retrying with a repo-local safe-directory override succeeded

Observed successful pattern:

```powershell
git -c safe.directory='D:/dad-relay-mvp-temp' status --short --branch
```

Implication:

- shell and git audit results must distinguish command capability from repository ownership friction
- the relay currently handles this as a workspace/environment fact, not as a broker policy denial

### PowerShell command execution is visible as shell activity

The Codex relay transcript included PowerShell commands such as:

```powershell
Get-Content -Path 'Document/api-spec.md' -TotalCount 120
```

and the broker still classified them under `shell`.

Implication:

- the current broker shell category is broad enough for both command shell and PowerShell inspection work
- this is acceptable for the current audit and product phase

## Relay Observation Mapping

### Codex

Observed in the live relay session:

- `tool.invoked` and `tool.completed` were emitted for command execution items
- category-specific `shell.requested` and `shell.completed` events were emitted
- the session finished with `handoff.accepted`

Status: verified with a real WPF-driven relay session

### Claude

Observed in the direct CLI transcript:

- Claude uses structured Bash tool calls in `-p --output-format stream-json`
- those Bash calls are the same source shape that the interactive Claude adapter classifies into broker-visible shell/git/pr categories

Status: shell execution is verified directly; relay-side end-to-end shell evidence is still stronger on Codex than on Claude

## Current Product Assessment

### Codex

- read-only shell inspection: working
- PowerShell inspection commands: working
- relay event logging for shell activity: working

Overall: `working`

### Claude

- read-only shell inspection: working
- structured shell tool transcript: working
- broker-routed approval for shell activity: not applicable in current Claude audit-only design

Overall: `working`

### Relay / Broker

- shell activity observation in a real relay turn: working
- shell category summary in UI and auto-logs: working
- Claude remains audit-only, so shell execution on Claude is not broker-gated the way Codex approvals are

Overall: `partial`

## Follow-up Gaps

1. Git ownership friction is real in sandboxed relay execution.
   The product should continue to treat `safe.directory` failures as environment issues, not as proof that shell execution is unavailable.

2. Claude shell execution is verified directly, but not yet through a full end-to-end WPF relay turn in this audit slice.

3. The desktop UI appeared to refresh live state late during the Codex audit run.
   Final state was correct, but near-real-time operator refresh should be re-checked in a follow-up QA slice before treating it as fully settled.
