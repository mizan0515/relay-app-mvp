# MVP-GATES — relay-app-mvp completion scorecard

Gate count: 8

Each gate is an OBSERVABLE completion criterion with inspectable evidence. A gate flips to
`[x]` only when pointed at a runnable log/file/screenshot. `[~]` = partial/in-progress.
`[ ]` = not started or regressed.

Evidence formats accepted:
- `logs/*.jsonl` line range with event name (quote ≤3 lines)
- `auto-logs/<file>` tail reference
- `.autopilot/qa-evidence/<slug>-<ISO>.json` pointer
- `drive-destructive-qa.ps1` output snippet
- UI Automation screenshot path (saved under `.autopilot/qa-evidence/screenshots/`)
- `dotnet build` exit-code + warning count

Regression protocol: a `[x]` gate reverts to `[~]` or `[ ]` only with cited evidence
(commit sha + failing build / failing QA line / new error in logs). Silent reversion is a
rule break.

---

## G1 — Build is green end-to-end
- [ ] `powershell -File .autopilot/project.ps1 test` exits 0 on a clean checkout, zero
      `error CS*` lines in output. Release config. All four projects compile:
      `RelayApp.Core`, `RelayApp.Desktop`, `RelayApp.CodexProtocol`,
      `RelayApp.CodexProtocol.Spike`.
- Evidence: last `dotnet build` log tail (paste ≤10 lines showing "Build succeeded").

## G2 — App launches and adapter smoke passes
- [ ] Release binary launches without exception. "Check Adapters" button produces
      adapter list with at least Codex CLI reachable. "Smoke Test 2" reports success.
- Evidence: `auto-logs/<launch-id>.json` showing adapter probe success +
      `logs/<date>.jsonl` line with `smoke.pass` event.

## G3 — Session start → advance turn → clean shutdown
- [ ] Operator types a working directory + initial prompt, clicks Start Session, clicks
      Advance Once, sees tool activity populate, clicks Stop, app shuts down without
      orphan process.
- Evidence: `logs/*.jsonl` showing `session.started`, `turn.completed`, `session.stopped`
      in order, no `session.orphaned` event.

## G4 — Approval-first gate on destructive op
- [ ] Running a scenario that triggers a destructive-tier shell command (e.g.
      `drive-destructive-qa.ps1` scenario 5/6) surfaces the approval UI; Deny blocks the
      command; Approve Once lets one through without elevating session policy.
- Evidence: `drive-destructive-qa.ps1` run tail showing deny path + approve-once path,
      plus UI screenshot of approval panel.

## G5 — Handoff parse + repair loop
- [ ] A malformed handoff output triggers `BuildInteractiveRepairPrompt`; a repaired
      handoff lands as `handoff.accepted` with `ready=true` and no
      `previous_invalid_output` field (E-spec-2).
- Evidence: `logs/*.jsonl` lines `handoff.rejected` → repair prompt → `handoff.accepted`.

## G6 — Rolling summary written durably (F-impl-1)
- [ ] `RelayBroker.RotateSessionAsync` writes a markdown summary to
      `%LocalAppData%\RelayAppMvp\summaries\{sessionId}-segment-{n}.md` BEFORE per-rotation
      state reset. `summary.generated` emitted with bytes + cost fields. IO failure emits
      `summary.failed` and does not crash the broker.
- Evidence: sample summary file (size + first 20 lines) + matching `summary.generated`
      log line.

## G7 — Carry-forward injected into next turn (F-impl-2 + F-impl-3)
- [ ] `RelaySessionState` populates `Goal`/`Completed`/`Pending`/`Constraints`/
      `LastHandoffHash` (from `HandoffParser.ComputeCanonicalHash` at
      `CompleteHandoffAsync`). `RelayPromptBuilder` emits a `## Carry-forward` section
      into the prompt on the post-rotation turn and emits `summary.loaded`.
- Evidence: pre-/post-rotation prompt diff showing the carry-forward block + logged
      `summary.loaded` line.

## G8 — Rotation live exercise crossing MaxTurnsPerSession (F-live-1)
- [ ] Live run crosses the rotation threshold, carry-forward is visible in the next
      session's first turn, operator can export diagnostics, no orphan approvals, no
      `session.lost` event. Destructive-tier ops still gated across the boundary.
- Evidence: full session jsonl spanning the rotation + diagnostics export zip pointer.

---

## Flip history

(Append an ISO-timestamped line when a gate flips, with commit sha + evidence pointer.
Never delete history lines — they are the regression audit trail.)

- 2026-04-18 — scaffolded, all gates `[ ]`.
