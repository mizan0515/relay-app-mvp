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
- `profiles/card-game/skill-contracts.json`
  - bucket-to-skill contract map for required skills, required evidence, and forbidden tools
- `profiles/card-game/agent-identities.json`
  - per-agent identity registry for autopilot, route, relay-peer, and Unity-MCP roles
- `profiles/card-game/tool-registry.json`
  - centrally approved tool registry for compact artifacts, relay runtimes, script runners, and Unity MCP
- `profiles/card-game/policy-registry.json`
  - centrally approved policy registry for compact-artifact discipline, forbidden-tool rules, and required evidence contracts
- `scripts/card-game/Install-CardGameProfile.ps1`
  - installs the card-game profile into `%LocalAppData%\CodexClaudeRelayMvp`
- `scripts/card-game/New-CardGameSessionPrompt.ps1`
  - builds a session prompt from the stable prefix plus a narrow task tail
- `scripts/card-game/New-CardGameSession.ps1`
  - prepares heuristics, admission, prompt, and a small session plan in one step
- `skills/card-game/*`
  - local skill catalog for repeated relay/autopilot workflows such as QA editor slices, Unity MCP proof, bounded cycles, and compact operator status
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
- `scripts/card-game/Get-CardGameManagerSignal.ps1`
  - merges relay liveness plus loop status into one compact manager signal so Desktop/autopilot can see death, wait-stop, and next action without reading multiple artifacts
- `scripts/card-game/Get-CardGameAgentIdentityStatus.ps1`
  - resolves which registered agent identities are active for the current slice and blocks governance if the identity mapping is broken
- `scripts/card-game/Get-CardGameRelayEvidence.ps1`
  - reads one compact relay session evidence summary so operators can confirm whether MCP activity was observed without opening the full JSONL log
- `scripts/card-game/Get-CardGameRequiredEvidenceStatus.ps1`
  - resolves whether the current manifest's required evidence is actually present so loop status and completion can hard-stop on missing proof
- `scripts/card-game/Watch-RelaySignalLiveness.ps1`
  - detached watcher that rewrites live signals to `Stale` if the Desktop relay process disappears unexpectedly, then refreshes the manager signal artifact
- `scripts/card-game/Wait-CardGameRelaySignal.ps1`
  - waits for a compact relay done marker with a hard timeout so operators do not wait forever
- `scripts/card-game/Run-CardGameManagedRelay.ps1`
  - the easiest operator path: prepare relay artifacts, run a bounded GUI worksession, then print only the compact signal markers
- `scripts/card-game/Get-CardGameRelayEvidence.ps1`
  - reads only the current relay event log and outputs a compact marker for whether Unity MCP tool calls were actually observed
- `docs/card-game-integration/TOKEN-SAVINGS.md`
  - token and validation budget rules for Unity work
- `docs/card-game-integration/CLAUDE-BACKEND-NOTES.md`
  - separates officially confirmed Claude backend behavior from unverified support-channel claims

## Recommended operating model

1. Keep task selection in `D:\Unity\card game\.autopilot`.
2. Admit only one narrow slice at a time into DAD.
3. Generate the relay prompt from a stable prefix plus a short task tail.
4. Carry an explicit skill contract in the admission manifest:
   - required skills
   - required evidence
   - forbidden tools
5. Carry explicit agent identities in the admission/guidance path so every manager, route, relay-peer, and Unity-MCP role is traceable and revocable.
6. Run the relay against `D:\Unity\card game` as the working directory.
7. Require compile/test/QA evidence before writing progress back to
   `.autopilot` or `Document/dialogue/`.
8. Treat missing required evidence as a hard stop for terminal-session
   completion instead of a soft warning.
9. Treat detectable forbidden-tool violations such as `web` on Unity-local slices as a hard stop for terminal-session completion.
10. Treat missing or unregistered agent identities as a hard governance stop before trusting the slice.
11. After the relay closes in a terminal state, run the completion step to
   update heuristics plus `.autopilot/STATE.md`, `HISTORY.md`, and
   `METRICS.jsonl`.
10. Treat backlog corruption warnings as a scope freeze until the live
   `BACKLOG.md` line is re-read locally or normalized.
11. Prefer the loop runner when you want relay execution to honor `HALT`,
   backlog health, and operator decision state in one place.
12. After a terminal session is integrated, the loop status resolver treats it
   as already consumed and moves back to `prepare` for the next slice.
13. Terminal session completion archives the consumed manifest and clears the
    generated manifest/prompt/plan so the next prepare pass starts fresh.
14. After each relay execution, the loop runner refreshes backlog/loop status
    so a multi-session run can keep advancing instead of reusing stale state.
15. Let execution mode decide whether the desktop relay should actually run.
    `direct-codex` and `docs-lite` are valid outcomes for Unity cost control,
    and the generated route artifact makes that explicit.
16. If a prepared manifest resolves to `route`, do not keep re-running the
    desktop relay. Consume the generated route artifact and execute the
    cheaper path instead.
17. For `qa-editor` slices, treat `unity_mcp_observed` as required evidence instead of a soft preference.

## Operator Quick Path

Use these commands when you want the card-game autopilot and desktop relay to
work together without reading large logs.

1. Easiest bounded operator run:
   `powershell -ExecutionPolicy Bypass -File scripts/card-game/Run-CardGameManagedRelay.ps1 -TaskSlug companion-depth-first-slice -Turns 2 -ForceRelay`
2. Read the compact live signal only:
   `powershell -ExecutionPolicy Bypass -File scripts/card-game/Get-CardGameRelaySignal.ps1`
3. Read the compact manager signal only:
   `powershell -ExecutionPolicy Bypass -File scripts/card-game/Get-CardGameManagerSignal.ps1`
4. Wait for a bounded completion signal instead of tailing logs forever:
   `powershell -ExecutionPolicy Bypass -File scripts/card-game/Wait-CardGameRelaySignal.ps1 -TimeoutSeconds 1800`
5. Read compact MCP evidence for the latest relay session:
   `powershell -ExecutionPolicy Bypass -File scripts/card-game/Get-CardGameRelayEvidence.ps1`
6. Full autopilot-driven preparation/execution path:
   `powershell -ExecutionPolicy Bypass -File scripts/card-game/Invoke-CardGameAutopilotLoop.ps1 -ForceRelay`
7. When the relay reaches a terminal session, integrate it back into the card-game autopilot:
   `powershell -ExecutionPolicy Bypass -File scripts/card-game/Complete-CardGameRelaySession.ps1`

Relay live signal artifacts are mirrored into:
- `%LocalAppData%\CodexClaudeRelayMvp\auto-logs\relay-live-signal.json`
- `%LocalAppData%\CodexClaudeRelayMvp\auto-logs\relay-live-signal.txt`
- `D:\Unity\card game\.autopilot\generated\relay-live-signal.json`
- `D:\Unity\card game\.autopilot\generated\relay-live-signal.txt`
- `D:\Unity\card game\.autopilot\generated\relay-manager-signal.json`
- `D:\Unity\card game\.autopilot\generated\relay-manager-signal.txt`

The text signal always starts with these sentinel lines:
- `[RELAY_SIGNAL] ...`
- `[RELAY_DONE] true|false ...`

When governance blocks a slice, manager text also includes:
- `[GOVERNANCE] status=... reason=... attention=...`
- `[AGENT_IDENTITY] status=...`
- `[TOOL_REGISTRY] status=...`
- `[POLICY_REGISTRY] status=...`
- `[PROMPT_SURFACE] status=...`
- `[ANOMALY] status=...`
- `[SECURITY_POSTURE] risk=...`
- `[RETRY_BUDGET] exhausted unity_verification` or `[RETRY_BUDGET] left=... limit=...` when retry budgeting is active

These are the only lines an LLM or operator needs to read for routine status checks.
When the block needs a specific follow-up artifact, the JSON manager signal now also carries:
- `blocker_artifact_path`
- `blocker_hint`
- `blocker_detail`
- `recommended_action`
- `recommended_action_id`
- `recommended_action_label`

Unity retry budget is now only active when the current blocker is actually the missing `unity_mcp_observed` proof.
If the current blocker is something else, manager signal no longer pretends the Unity retry budget is already exhausted.

## Codex Desktop Only Path

If the administrator wants one visible operator surface, use the relay desktop
UI instead of manual PowerShell.

1. Launch `CodexClaudeRelay.Desktop`.
2. In the `Easy Operator` box, keep the default card-game root and task slug unless you were told to change them.
3. Click `Easy Start`.
4. The app repairs a dead relay automatically and runs one safe DAD session.
5. Read only `Easy Status` and the compact markers in the status mirror:
   `D:\Unity\card game\.autopilot\generated\relay-live-signal.txt`
6. If the relay dies, read:
   `D:\Unity\card game\.autopilot\generated\relay-manager-signal.txt`

Advanced buttons still exist for debugging:
- `Managed CardGame Run`
- `Managed Autopilot Run`
- `Managed Next Step`
- `Managed Run Until Attention`
- `Managed Autopilot Loop`

What this button does:
- prepares the next card-game relay slice through `Start-CardGameRelay.ps1 -PrepareOnly`
- loads the generated session id + prompt into the desktop app automatically
- starts the relay in the current desktop instance
- runs only the bounded turn batch requested in `Managed Turns`
- pauses intentionally after success so the operator never waits forever

What `Managed Autopilot Run` adds:
- refreshes backlog health plus loop status first
- respects route/blocked/halt outcomes instead of blindly starting DAD every time
- starts relay only when the loop resolver says the next action is `run`
- writes one manager-level signal artifact so the operator can see `relay_dead`, `route_only`, `prepare_next`, `blocked`, or `halted`

What `Managed Next Step` adds:
- reads `suggested_desktop_action` from the compact manager signal and executes only that one action
- lets Desktop handle `prepare`, `run`, or `complete` without the operator deciding which script should run
- stops immediately on `route_only`, `blocked`, `halted`, or `wait_for_signal`
- shows the governance stop reason directly in `Managed Autopilot Status` so the operator does not have to decode the signal file
- shows the exact blocker artifact path when governance says a proof or policy file must be opened next
- refreshes governance while reading the manager signal so stale blocker state does not hide behind an older relay mismatch
- shows the exact missing key, such as `unity_mcp_observed`, instead of forcing the operator to open JSON just to discover the field name
- shows one recommended next action sentence derived from the missing key or policy violation
- changes the blocker button label to match the current blocker type, such as `Open Required Evidence` or `Open Tool Policy`
- can execute lightweight runtime actions for some blocker types, such as refreshing compact signals instead of only opening a file
- can execute a bounded remediation run for `unity_mcp_observed`, where Desktop prepares a fresh relay slice and retries Unity verification directly
- records the last remediation result directly in `Managed Autopilot Status` and `Easy Status`, so the operator can see whether the retry helped
- mirrors the last remediation result into compact artifacts:
  - `D:\Unity\card game\.autopilot\generated\relay-remediation-status.json`
  - `D:\Unity\card game\.autopilot\generated\relay-remediation-status.txt`
- remediation artifacts now include the same retry-budget summary used by manager signal:
  - `[RETRY_BUDGET] exhausted unity_verification`
  - `[RETRY_BUDGET] left=... limit=...`
- ops dashboard now mirrors both `[REMEDIATION] ...` and `[RETRY_BUDGET] ...` so all compact operator surfaces stay aligned
- when no remediation has run yet, dashboard now shows `[REMEDIATION] no remediation yet` instead of leaving the section blank
- when Desktop writes a new remediation artifact, it now refreshes both the manager signal and the ops dashboard immediately so the compact surfaces stay on the same tick
- after Desktop writes a remediation artifact and refreshes those compact files, it also re-reads the manager signal immediately so `Managed Autopilot Status` and `Easy Status` update on the same tick instead of waiting for the next timer refresh
- governance now reads that remediation artifact back in, so a recent Unity verification retry can downgrade the next button from another blind retry to `Open Required Evidence`
- after repeated Unity verification retries, governance can escalate the button all the way to a human-attention path instead of retrying forever
- retry budget is now explicit in the Desktop status text and the button label, so the operator can see how many automatic Unity verification retries remain
- when the Unity retry budget reaches zero, the Desktop managed/easy status surfaces switch to a stronger warning palette

What `Managed Run Until Attention` adds:
- repeats manager-directed steps until the compact manager signal says attention is required or waiting should end
- gives the administrator one default Codex Desktop path instead of choosing between prepare/run/complete scripts
- keeps the stop rule compact and token-cheap because Desktop still reads only the manager signal, not the full relay log

What `Easy Start` adds:
- gives a non-developer operator one obvious button instead of multiple relay-control buttons
- points the operator to `Easy Status` instead of the raw relay or event log views
- forces relay mode for the easy path so the operator gets one bounded DAD session instead of a route-only result
- repairs a stale relay automatically before starting the next bounded session
- if the relay dies during that safe session, retries one more fresh session automatically before asking the human to intervene
- refreshes `Easy Status` and `Managed Autopilot Status` from the compact manager signal every few seconds so the screen does not look frozen while a retry is happening
- shows a plain-language governance reason such as missing Unity MCP proof instead of only showing `fix_blocker`
- shows `What file to read next` when a blocker artifact exists, so the operator is pointed at one compact file instead of the full log surface
- shows `What exact key is missing` when governance already knows the missing proof or policy key
- shows `What to do now` so the operator does not have to infer the remediation from the raw key name
- stops again after one safe session so the operator never gets trapped in an invisible long loop
- works with `Open Blocker File`, which opens the compact blocker artifact directly from Desktop

## Admin Infinite Prompt

For a stable admin-operated autopilot loop, use:
- [AUTOPILOT-ADMIN-PROMPT.md](D:/cardgame-dad-relay/docs/card-game-integration/AUTOPILOT-ADMIN-PROMPT.md)

That prompt is designed for repeated Codex Desktop copy-paste use and keeps the operator on the compact manager / relay / evidence surfaces only.

Bounded retry proof:
- `scripts/gui-smoke/run-gui-easy-operator.ps1 -InjectRelayDeathOnce` simulates one relay death after `Easy Start` reaches `relay_active`
- the expected compact-signal path is `relay_dead -> relay_active -> injected relay_dead -> relay_active(new session)`
- this lets operators verify the auto-retry path without reading the full event log

What `Managed Autopilot Loop` adds:
- repeats `prepare -> bounded relay -> completion write-back` for the requested number of sessions
- stops early if manager signal says `route_only`, `blocked`, `halted`, or `relay_dead`
- keeps the operator inside Codex Desktop instead of bouncing back to manual completion commands

Stale signal handling:
- every live signal now carries `source_pid`, `source_process_started_at`, and `heartbeat_at`
- if the desktop process is gone, the detached watcher rewrites the artifact to `status=Stale` immediately; startup/read paths still keep a second normalization pass as fallback
- if the relay process stays alive but the turn watchdog has already expired beyond a short grace window, compact readers now treat that as a hung relay instead of waiting forever
- loop status treats `Stale` as `prepare a fresh session`, not `keep waiting`
- manager signal turns that into one compact operator state such as `relay_dead` with `suggested action: prepare_fresh_session`
- manager signal also detects `relay_hung` and `relay_session_mismatch`, so a new prepared session cannot be hidden behind an unrelated older active relay
- Desktop itself now shows only an event-log tail in the main surface so routine operation does not require reading the full JSONL log

Peer model defaults:
- relay now launches Claude with `--model opus --effort high` by default
- relay now launches Codex with `-m gpt-5.4 -c model_reasoning_effort="high"` by default
- these defaults aim at unattended relay quality without jumping to the most expensive settings by default
- override environment variables:
  - `CCR_CLAUDE_MODEL`
  - `CCR_CLAUDE_EFFORT`
  - `CCR_CODEX_MODEL`
  - `CCR_CODEX_REASONING_EFFORT`

If the administrator wants to stay inside Codex Desktop only, the working rule
should be one repeated manager prompt:

`Use D:\cardgame-dad-relay as the relay operator for D:\Unity\card game. Never tail full logs. Read only relay-live-signal.txt/json markers, fix stale signals before waiting, and use the Managed CardGame Run path for bounded sessions.`

## How This Differs From The Old Autopilot Loop

The old pattern was effectively:
- keep one large prompt in Codex
- repeat the same prompt forever
- inspect long logs to infer whether work moved

The relay-integrated pattern should be:
- autopilot picks the slice and writes the prompt/runbook
- relay runs a bounded peer session (`Turns 2` or `Turns 4`)
- operators and LLMs read only `[RELAY_SIGNAL]` and `[RELAY_DONE]`
- if the session is stale or timed out, the watcher exits instead of waiting forever
- if the session reaches a terminal state, write-back goes through `Complete-CardGameRelaySession.ps1`

## Immediate gaps still to implement in code

- broker-side ingestion of `.autopilot/BACKLOG.md`
- richer turn packet/state sync matching the live card-game DAD schema
- root `Document/dialogue/state.json` synchronization
- a loop runner that promotes the next backlog item automatically
- richer auto-loop behavior that re-admits the next slice after a terminal session

Until those land, this fork should be treated as a prepared integration base,
not the final autonomous loop.
