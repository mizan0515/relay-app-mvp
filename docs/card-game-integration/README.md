# Card Game Integration

This fork is the `codex-claude-relay` working copy for `D:\Unity\card game`.

The goal is not to replace the project's `.autopilot` loop. The goal is to
make the relay the DAD execution engine that the existing autopilot loop can
call when a task needs Codex <-> Claude peer turns.

## Why this fork exists

- The upstream relay is generic and peer-symmetric.
- `D:\Unity\card game` already has repo-specific backlog, gates, QA evidence,
  scoped `AGENTS.md`, research files, and a mature autopilot shell.
- Unity work is expensive in both context and validation time, so the relay
  needs a repo-specific operating profile instead of generic defaults.

## Contents

- `profiles/card-game/broker.cardgame.json`
  - recommended broker limits for short Unity slices
- `profiles/card-game/prompt-prefix.md`
  - stable session prefix meant to maximize cache reuse
- `scripts/card-game/Install-CardGameProfile.ps1`
  - installs the card-game profile into `%LocalAppData%\CodexClaudeRelayMvp`
- `scripts/card-game/New-CardGameSessionPrompt.ps1`
  - builds a session prompt from the stable prefix plus a narrow task tail
- `scripts/card-game/New-CardGameSession.ps1`
  - prepares heuristics, admission, prompt, and a small session plan in one step
- `scripts/card-game/Write-CardGameExecutionRoute.ps1`
  - converts the admission manifest into an execution-mode artifact so the loop can skip expensive relay runs when direct Codex or docs-lite is cheaper
- `scripts/card-game/Write-CardGameDirectPrompt.ps1`
  - turns a routed manifest into a direct-Codex prompt artifact so route mode still has a concrete next action
- `scripts/card-game/Write-CardGameRunbook.ps1`
  - bundles manifest, route, direct prompt, and next commands into one operator-facing runbook
- `scripts/card-game/Write-CardGameOpsDashboard.ps1`
  - summarizes state, backlog, loop status, context risk, and learning into one dashboard artifact
- `scripts/card-game/Append-CardGameRouteLearningRecord.ps1`
  - records route decisions so heuristics can learn which buckets usually want `direct-codex`, `docs-lite`, or `relay-dad`
- `scripts/card-game/Write-CardGameRouteHandoff.ps1`
  - writes the latest route-only handoff into `D:\Unity\card game\.autopilot\generated\relay-route-handoff.json` so the main repo can consume direct-Codex next steps without reopening the relay repo
- route-mode refresh note
  - after route learning is appended, `Start-CardGameRelay.ps1` rewrites the ops dashboard once more so `codex-relay-dashboard` and `codex-relay-next` stay on the same learning sample count
- `scripts/card-game/Invoke-CardGameAutopilotLoop.ps1`
  - checks HALT / decision state / backlog health, then prepares or runs one or more relay sessions
- `scripts/card-game/Get-CardGameLoopStatus.ps1`
  - resolves the current next action (`halt`, `prepare`, `route`, `run`, `complete`, `blocked`) from state, decisions, backlog, and session state
- `scripts/card-game/Test-CardGameBacklogHealth.ps1`
  - detects encoding-damaged backlog items before automatic admission widens scope
- `scripts/card-game/Test-CardGameContextSurface.ps1`
  - measures asmdef absence and bucket-level giant-file surface so session prep can warn about expensive areas
- `scripts/card-game/Test-ClaudeBackendReadiness.ps1`
  - inspects Claude CLI auth / gateway / thinking / long-context posture before a costly session
- `scripts/card-game/Complete-CardGameRelaySession.ps1`
  - updates heuristics and writes terminal relay results back into `.autopilot`
- `scripts/card-game/Get-CardGameRelaySignal.ps1`
  - reads the compact relay live-signal artifact without opening the full JSONL log
- `scripts/card-game/Wait-CardGameRelaySignal.ps1`
  - waits for a compact relay done marker with a hard timeout so operators do not wait forever
- `docs/card-game-integration/TOKEN-SAVINGS.md`
  - token and validation budget rules for Unity work
- `docs/card-game-integration/CLAUDE-BACKEND-NOTES.md`
  - separates officially confirmed Claude backend behavior from unverified support-channel claims

## Recommended operating model

1. Keep task selection in `D:\Unity\card game\.autopilot`.
2. Admit only one narrow slice at a time into DAD.
3. Generate the relay prompt from a stable prefix plus a short task tail.
4. Run the relay against `D:\Unity\card game` as the working directory.
5. Require compile/test/QA evidence before writing progress back to
   `.autopilot` or `Document/dialogue/`.
6. After the relay closes in a terminal state, run the completion step to
   update heuristics plus `.autopilot/STATE.md`, `HISTORY.md`, and
   `METRICS.jsonl`.
7. Treat backlog corruption warnings as a scope freeze until the live
   `BACKLOG.md` line is re-read locally or normalized.
8. Prefer the loop runner when you want relay execution to honor `HALT`,
   backlog health, and operator decision state in one place.
9. After a terminal session is integrated, the loop status resolver treats it
   as already consumed and moves back to `prepare` for the next slice.
10. Terminal session completion archives the consumed manifest and clears the
    generated manifest/prompt/plan so the next prepare pass starts fresh.
11. After each relay execution, the loop runner refreshes backlog/loop status
    so a multi-session run can keep advancing instead of reusing stale state.
12. Let execution mode decide whether the desktop relay should actually run.
    `direct-codex` and `docs-lite` are valid outcomes for Unity cost control,
    and the generated route artifact makes that explicit.
13. If a prepared manifest resolves to `route`, do not keep re-running the
    desktop relay. Consume the generated route artifact and execute the
    cheaper path instead.

## Operator Quick Path

Use these commands when you want the card-game autopilot and desktop relay to
work together without reading large logs.

1. Prepare or run the relay from this repo:
   `powershell -ExecutionPolicy Bypass -File scripts/card-game/Invoke-CardGameAutopilotLoop.ps1 -ForceRelay`
2. Read the compact live signal only:
   `powershell -ExecutionPolicy Bypass -File scripts/card-game/Get-CardGameRelaySignal.ps1`
3. Wait for a bounded completion signal instead of tailing logs forever:
   `powershell -ExecutionPolicy Bypass -File scripts/card-game/Wait-CardGameRelaySignal.ps1 -TimeoutSeconds 1800`
4. When the relay reaches a terminal session, integrate it back into the card-game autopilot:
   `powershell -ExecutionPolicy Bypass -File scripts/card-game/Complete-CardGameRelaySession.ps1`

Relay live signal artifacts are mirrored into:
- `%LocalAppData%\CodexClaudeRelayMvp\auto-logs\relay-live-signal.json`
- `%LocalAppData%\CodexClaudeRelayMvp\auto-logs\relay-live-signal.txt`
- `D:\Unity\card game\.autopilot\generated\relay-live-signal.json`
- `D:\Unity\card game\.autopilot\generated\relay-live-signal.txt`

The text signal always starts with these sentinel lines:
- `[RELAY_SIGNAL] ...`
- `[RELAY_DONE] true|false ...`

These are the only lines an LLM or operator needs to read for routine status checks.

## Immediate gaps still to implement in code

- broker-side ingestion of `.autopilot/BACKLOG.md`
- richer turn packet/state sync matching the live card-game DAD schema
- root `Document/dialogue/state.json` synchronization
- a loop runner that promotes the next backlog item automatically
- richer auto-loop behavior that re-admits the next slice after a terminal session

Until those land, this fork should be treated as a prepared integration base,
not the final autonomous loop.
