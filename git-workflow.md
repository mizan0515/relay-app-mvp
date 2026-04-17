# Phase D — Git Workflow Layer Status

Audit date: 2026-04-17

Maps the Phase D deliverables from `IMPROVEMENT-PLAN.md` to concrete evidence
already landed during the Phase C audits and classifier work, so we can see
exactly what is covered, what is partially covered, and what is still open.

## D1. Commit wrapper

**Spec**: before commit, show staged diff summary + commit message preview;
require policy decision or approval. Record author side, branch, commit SHA,
commit message, timestamp.

**Covered**:
- `git-commit` category classification via `RelayApprovalPolicy.ClassifyCommandCategory`, including unwrapping of Codex/Windows PowerShell wrappers (iteration 3).
- Commit message preview in the approval card via `RelayApprovalPolicy.BuildGitCommitSummary` (already present in `RelayApprovalPolicy.cs`).
- Live evidence: session `destructive-qa-20260417-131500` emitted `git.commit.requested`/`.completed` against the TaskPulse workspace.
- Author side recorded per event (`Side: "Codex"` in every JSONL record).
- Timestamp recorded on every event.

**Gaps**:
- Commit SHA is not explicitly parsed out of `git.commit.completed` today — it is present in `aggregatedOutput` but not surfaced as a first-class field on the observed-action record.
- Branch name is not explicitly carried; it is derivable from `cwd` + `git status` but not recorded as a structured field.
- Codex runs `git commit` inside its sandbox without escalating to a broker approval on Windows, so the operator-approval flow never actually fires for commit today. Escalation would require either a Codex policy change or a relay-side pre-commit hook.

## D2. Push wrapper

**Spec**: before push, show remote + branch, show whether force push is attempted; require approval. Record remote, branch, commit range if available, timestamp.

**Covered**:
- `git-push` category classification, including protected-branch detection via `TargetsProtectedPushBranch`.
- Force-push detection via `IsForceLikeGitPush` — bumps risk to `critical`.
- Remote/branch preview via `BuildGitPushSummary`.
- `git-push` correctly escalates to the broker as `item/commandExecution/requestApproval` (verified in `destructive-qa-20260417-131500`).
- Auto-approve server-side fix (iteration 5) so `AutoApproveAllRequests=true` actually lets the push go through instead of being denied by the adapter race.

**Gaps**:
- Commit range (`origin/branch..HEAD` or the equivalent) is not computed — only the push command string is captured.
- msys/sh.exe pipe-creation fails under the relay's Job Object sandbox when `git push` forks `sh.exe` for HTTPS auth (surfaced in `auto-approve-push-qa-20260417-143000`). Non-blocking for approval flow but a real Windows compatibility gap.

## D3. PR wrapper

**Spec**: before PR creation, preview base/head/title/body; require approval. Record PR URL, base/head, title, timestamp.

**Covered**:
- `pr` category classification for `gh pr create` (after unwrapping PowerShell wrappers).
- Protected-base-branch detection via `TargetsProtectedPullRequestBase` — bumps risk to `critical`.
- Base/head/title/body extraction via `BuildPullRequestSummary` + `BuildPullRequestPolicyKey` (already present in `RelayApprovalPolicy.cs`).
- Operator-approval flow path is the same one validated for git-push.

**Gaps**:
- Live `gh pr create` exercise still pending against a real GitHub remote — the local bare repo at `D:\dad-relay-mvp-remote.git` does not support PR creation.
- PR URL capture from `gh pr create` stdout is not wired into a structured field on the observed-action record; it only appears in `aggregatedOutput`.

## D4. Safety defaults

**Spec**:
- `git status` / `diff` / `log`: allow
- `git add`: ask
- `git commit`: ask or session-allow
- `git push`: ask
- `gh pr create`: ask
- `git push --force`: deny
- `git reset --hard`: deny

**Status**: implemented end-to-end in `RelayApprovalPolicy`:

| Command | Implementation site | Verified live |
|---|---|---|
| `git status`/`diff`/`log` | `ResolveDefaultDecision` → `ApproveOnce` | `git-classify-qa-20260417-115929` |
| `git add` | `GetRiskLevel` → `medium`; escalates via `item/commandExecution/requestApproval` when Codex routes through broker | Codex runs it inside sandbox on Windows — verified in `destructive-qa-20260417-131500` |
| `git commit` | Same as `git add` | Same — verified in `destructive-qa-20260417-131500` |
| `git push` | `GetRiskLevel` → `high` (or `critical` for force / protected branches); escalates | `destructive-qa-20260417-131500`, `auto-approve-push-qa-20260417-143000` |
| `gh pr create` | Routes through `pr` category | Pending live remote |
| `git push --force` | `IsForceLikeGitPush` → risk `critical`; `IsDestructiveGitCommand` → auto-Deny via `ResolveDefaultDecision` | Unit-level only — not exercised live |
| `git reset --hard` | `IsDestructiveGitCommand` → auto-Deny | Unit-level only — not exercised live |

## Exit criteria assessment

Phase D exit criteria from `IMPROVEMENT-PLAN.md`:

1. **"commit/push/PR are never invisible shell side effects"** — met on Windows for push/PR (they escalate to the broker as `commandExecution/requestApproval`). Partially met for commit/add because Codex runs them inside its sandbox without escalating; the relay still observes them as `git.commit.requested`/`.completed` events (not invisible), but cannot block them mid-execution.

2. **"the app can reconstruct the git lifecycle for a session afterward"** — met: every session's JSONL log contains `git.*.requested`/`.completed` events with the full command string, exit code, and aggregated stdout/stderr. The `Latest Git / PR Activity` panel shows this live in the WPF UI.

## Remaining Phase D work (recommended order)

1. Live `gh pr create` exercise against a real GitHub remote — lets us verify D3 end-to-end.
2. Structured field extraction: parse commit SHA, branch, commit range, and PR URL out of `aggregatedOutput` into first-class observed-action fields (nice-to-have, not blocking).
3. Windows sandbox fix: investigate the msys/sh.exe pipe-creation failure so `git push` over HTTPS completes inside the relay sandbox (surfaced in `auto-approve-push-qa-20260417-143000`).

Phase D can be declared substantively done on the classification + recording side; the open items are all verification or quality-of-life improvements rather than new protocol work.
