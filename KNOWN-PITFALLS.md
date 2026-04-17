# KNOWN-PITFALLS — append-only landmine registry

Read on every boot (AUTONOMOUS-DEV-PROMPT G3). Each entry = one landmine you've paid for once;
the next iteration should pre-plan around it, not rediscover it. **Append only. Never reformulate existing entries.** When you hit a new one, add ≤5 lines at the bottom with a date and a concrete "next time, do X" line.

---

### 2026-04-17 — Codex wraps every Windows command in `powershell.exe -Command '...'`
- Symptom: `RelayApprovalPolicy.ClassifyCommandCategory` never matches `git*`/`pr` categories for Codex on Windows → destructive-tier git approval flow unreachable.
- Next time: any classifier-side matching on commands from Codex MUST unwrap `"...\powershell.exe" -Command '<inner>'`, `pwsh -Command '<inner>'`, and `cmd /c <inner>` before subcommand matching. Also strip `git -c k=v` / `git -C <path>` option pairs before matching.
- Resolved in: `dev/git-classifier-unwrap` (PR #6).

### 2026-04-17 — msys/sh.exe pipe creation fails under Codex Job Object sandbox
- Symptom: `git push` (and any git op that forks an `sh.exe` child) fails with pipe-creation error even when the approval correctly let the command through. Not an approval-flow defect.
- Next time: do NOT mis-classify as an approval bug. When a git op succeeds through the policy layer but exits non-zero with pipe/sh errors on Windows, this is the sandbox-fork issue; record and move on.
- Status: open, non-blocking.

### 2026-04-17 — `AutoApproveAllRequests` had two overlapping defects
- (a) `BuildServerRequestResponse` emitted `acceptForSession` which Codex treats as unknown → implicit decline. Fix: map `ApproveForSession` → `accept` for `commandExecution`/`fileChange` approval methods.
- (b) `HandleServerRequestAsync` resolved auto-approve ~200ms later on the UI thread → Codex already declined by then. Fix: resolve synchronously in the adapter, persist queue work on background task.
- Next time: any adapter change touching the approval reply path needs a live-QA run under `AutoApproveAllRequests=true` PLUS a check that the Codex side actually executed the command (not just that the broker emitted `approval.granted`).
- Resolved in: `dev/auto-approve-server-side` (PR #8).

### 2026-04-17 — Codex may decline to handoff when TaskPulse `state.json` has a prior active session
- Symptom: turn ends with `session.paused` instead of `handoff.accepted` even though the work completed. Codex correctly refuses to silently mutate unrelated DAD state.
- Next time: between unrelated test scenarios, RESET the workspace (`git checkout -- . && git clean -fd` or full bootstrap) so `Document/dialogue/state.json` is clean. This is a test-workspace hygiene issue, not a product gap.

### 2026-04-17 — `BuildInteractiveRepairPrompt` ergonomics gap
- Symptom: after a valid repair, the repaired handoff routes to `session.paused` ("Relay requires manual review.") instead of `handoff.accepted` because the repaired JSON lacks `ready:true` and includes `previous_invalid_output`.
- Next time: repair-prompt changes should explicitly instruct the side to set `ready=true` and omit `previous_invalid_output`. Filed as E-spec-2.

### 2026-04-17 — `gh pr merge --delete-branch` works; stale tracking refs are a LOCAL illusion
- Symptom: `git branch -r` shows 14× `origin/dev/*` branches that look un-cleaned after merge — but `gh api DELETE .../refs/heads/<b>` returns 422 "Reference does not exist" for all of them. Remote is fine; local `refs/remotes/origin/dev/*` just weren't pruned.
- Next time: ALWAYS run `git fetch --prune origin` as part of post-merge cleanup (5f). Also hard-delete local branches whose upstream is `: gone]`. The cleanup block in prompt step 5f is mandatory every iteration.

### 2026-04-17 — Codex patch fidelity deviates on README-style appends
- Symptom: during `dad-asset-qa-20260417-145500`, Codex appended literal `A` to `README.md` instead of the prompt's append string.
- Next time: append/edit test scenarios should assert exact content on disk, not just "file changed." Don't trust patch summaries; diff the actual bytes.

### 2026-04-18 — Autopilot summary text is not proof of `ScheduleWakeup` tool call
- Symptom: iter 0 end message read "NEXT_DELAY=1800; rescheduled" but /loop dynamic-mode wakeup never fired. Loop silently halted with no HALT/LOCK/env-broken marker; `.autopilot/NEXT_DELAY` written correctly; `status: idle-upkeep-bootstrap` (not a halt condition).
- Root cause: agent wrote summary narration but did not actually invoke the `ScheduleWakeup` tool. Text ≠ side-effect.
- Next time: trust only `.autopilot/LAST_RESCHEDULE` (written after tool-call return) and `.autopilot/LAST_HALT_NOTE` (intentional-halt sentinel). Boot watchdog in `[IMMUTABLE:wake-reschedule]` compares sentinel mtime vs METRICS tail; `.autopilot/project.ps1 check-reschedule` runs the same check manually.
- Resolved in: `dev/autopilot-relay-evolution-20260418-2121` + `.autopilot/INCIDENT-2026-04-18-loop-halt.md`.
