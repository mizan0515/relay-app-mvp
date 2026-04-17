## 2026-04-17 Session 1 — archived iterations 1–13 and Phase A/B/C/D/E audit entries

Moved out of DEV-PROGRESS.md on 2026-04-17 as part of the token-optimization pass
(see AUTONOMOUS-DEV-PROMPT-COPY.txt "Token & progress optimization rules" section C).

### Completed (iterations 1–14)

- Boot: read prototype state docs and verified baseline desktop build before new work.
- A7: relaxed the bounded prompt contract to marker-based handoff extraction and removed bounded CLI schema forcing.
- B4: added a dedicated `Latest Git / PR Activity` panel plus diagnostics and auto-log output.
- A2: added a broker-owned durable approval queue/history, wired interactive Codex approvals through it, and exposed an `Approval Queue` operator panel plus diagnostics output.
- G2 bridge: promoted unmanaged `mcp` / `web` activity from advisory-only into broker review items that can pause the session and be approved once or for the remainder of the session.
- A6 polish: surfaced matching saved session-approval rules directly in the pending approval and session-rule UI summaries.
- C1/G2: completed a real MCP capability audit for Codex and Claude, added `mcp-audit.md` / `capability-matrix.md`, and tightened MCP review policy so read-only resource discovery and telemetry ping/status auto-clear through broker policy while other MCP activity still pauses for review.
- C2: completed a real shell/PowerShell capability audit using the TaskPulse DAD workspace, captured live relay shell evidence plus direct Claude shell evidence, and recorded the results in `shell-audit.md` / `capability-matrix.md` / `TESTING-CHECKLIST.md`.
- C3: completed the read-only tier of the git workflow capability audit. Captured live WPF-driven relay evidence (session `git-audit-20260417-111226`) plus direct Codex and direct Claude git evidence against the TaskPulse workspace, and recorded everything in `git-audit.md` / `capability-matrix.md` / `TESTING-CHECKLIST.md`. Surfaced a real product gap: Codex wraps every Windows command in `powershell.exe -Command '...'`, so `RelayApprovalPolicy.ClassifyCommandCategory` never matches the `git*`/`pr` categories for Codex on Windows, and the destructive-tier git approval flow is therefore currently unreachable for Codex on Windows in practice.
- C3 follow-up (classifier fix): closed the Codex/Windows PowerShell-wrapping classifier gap. `RelayApprovalPolicy.ClassifyCommandCategory` now unwraps `"...\powershell.exe" -Command '<inner>'`, `pwsh -Command '<inner>'`, and `cmd /c <inner>` before matching on `git`/`git commit`/`git add`/`git push`/`gh pr create`, and strips `git -c key=value` / `git -C <path>` option pairs before subcommand matching. The Codex adapter now also refines `commandExecution` item categorization from the generic `shell` class to the more specific git/git-add/git-commit/git-push/pr class when the wrapped command warrants it.
- C3 destructive-tier live exercise: ran real `git add` / `git commit` / `git push` against a disposable `audit/destructive-20260417` branch of the TaskPulse workspace through the interactive relay (session `destructive-qa-20260417-131500`). Captured in `git-audit.md` and `capability-matrix.md`. `gh pr create` live exercise still pending (blocked on a real remote).
- Auto-approve server-side fix: closed the `AutoApproveAllRequests` gap surfaced by the destructive-tier exercise (adapter synchronous auto-approve path + Codex protocol decision mapping `ApproveForSession` → `accept`). Verified live in QA session `auto-approve-push-qa-20260417-143000`.
- DAD asset classification audit: ran live WPF-driven QA session `dad-asset-qa-20260417-145500`. Captured in `dad-asset-audit.md`; gaps recorded (DAD-specific band missing, DAD reads collapse into `shell` on Codex/Windows).
- Codex Windows compatibility matrix: consolidated shell/git/MCP/DAD live-session evidence into `codex-windows-matrix.md`. Matrix flipped from Pending to Working.
- Read classifier (iteration 8): added a `read` category to `RelayApprovalPolicy.ClassifyCommandCategory`. Verified live in `read-classify-qa-20260417-180000`.
- DAD-asset band (iteration 9): added `dad-asset` category + Codex adapter `fileChange` refinement. Verified live in `dad-asset-band-qa-20260417-190000`.
- Phase D status doc (iteration 10): authored `git-workflow.md` mapping D1–D4 to evidence. Phase D substantively complete for classification/recording side.
- Phase E opening survey (iteration 11): authored `phase-e-survey.md`.
- E-live-1 edit→test→handoff QA (iteration 12): planted `phase_e_demo` stdlib-only calc project; drove Codex through full edit-test chain. Authored `phase-e-live-1-audit.md`.
- E-live-2 repair-flow QA (iteration 13): drove deliberate handoff-format violation; confirmed repair flow end-to-end. Authored `phase-e-live-2-audit.md`.
- Phase F opening survey (iteration 14): authored `phase-f-survey.md`. Recommended slices F-impl-1..3 + F-live-1.

### Build status (iterations 1–14)

All builds passed. Most recent baseline: pass 2026-04-17T21:40:00+09:00 (Phase F opening survey is synthesis-only).

### Runtime verification (rollup)

- Claude/Codex CLI smoke: both return `ok` across all iterations.
- MCP: Codex `manage_editor` + `telemetry_ping` real-turn success; Claude direct `mcp__UnityMCP__manage_editor` success.
- WPF interactive sessions (all succeeded):
  `shell-audit-20260417-102903`, `git-audit-20260417-111226`, `git-classify-qa-20260417-115929`,
  `destructive-qa-20260417-131500`, `auto-approve-push-qa-20260417-143000`,
  `dad-asset-qa-20260417-145500`, `read-classify-qa-20260417-180000`,
  `dad-asset-band-qa-20260417-190000`, `phase-e-edit-test-qa-20260417-200500`,
  `phase-e-repair-qa-20260417-210800`.

### Loop status (iterations 1–10 detail moved here)

- iteration 1: paused after C3 read-only merge (PR #5).
- iteration 2: classifier fix shipped; paused.
- iteration 3: 컨텍스트 포화로 일시 정지.
- iterations 4–10: see full detail above; each iteration ended with one commit/PR and a context-boundary pause.
