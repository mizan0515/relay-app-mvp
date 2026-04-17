# relay-app-mvp

Windows desktop (WPF / .NET 8) **relay** that brokers Codex CLI sessions under an
approval-first, DAD-aware policy. Not a chat UI or multi-agent orchestrator — a relay
with strong guarantees: approvals, handoffs, rolling-summary carry-forward, audit trail,
risk gating.

## Status

Phase F (summary durability + carry-forward injection + rotation live exercise) is the
active delivery target. Claude parity is explicitly **audit-only** by design — see
[CLAUDE-APPROVAL-DECISION.md](CLAUDE-APPROVAL-DECISION.md).

## Projects

| Project | Purpose |
|---|---|
| `RelayApp.Core` | Broker, adapters (Codex / Claude audit), policy, pricing, persistence, protocol handoff |
| `RelayApp.Desktop` | WPF shell, approval UI, session view, UIA-exposed controls |
| `RelayApp.CodexProtocol` | Canonical Codex CLI protocol types + handoff parser |
| `RelayApp.CodexProtocol.Spike` | Isolated protocol experiments; does not ship |

## Build + run

```powershell
# Env sanity
.\.autopilot\project.ps1 doctor

# Build
.\.autopilot\project.ps1 test            # → dotnet build RelayApp.sln -c Release

# Launch
.\RelayApp.Desktop\bin\Release\net8.0-windows\RelayApp.Desktop.exe
```

## Autopilot

This repo is driven by a self-looping autopilot (`.autopilot/`) with IMMUTABLE-block
enforcement via git hooks. To advance one iteration:

```powershell
.\.autopilot\project.ps1 install-hooks   # one-time, sets core.hooksPath=.githooks
.\.autopilot\project.ps1 start           # prints path to RUN.txt
```

Then in Claude Code, **preferred** launch (self-scheduling via `/loop` dynamic mode):

```
/loop <paste .autopilot/RUN.txt body here>
```

The loop calls `ScheduleWakeup` at the end of each iteration using the
`NEXT_DELAY` seconds it just wrote — 270 / 900 / 1800 / 3600 depending on mode
— and re-fires the same prompt automatically. It stops self-rescheduling when
`.autopilot/HALT` appears or STATE `status:` becomes `halted` /
`mvp-complete` / `stagnation on <gate>` / `env-broken`.

**Fallback** (no `/loop`): paste RUN.txt body directly. Loop runs one
iteration, exits, and you re-paste after `NEXT_DELAY` seconds.

Operator controls:

- `.\.autopilot\project.ps1 stop` — create `.autopilot/HALT` (polite stop at next boot)
- `.\.autopilot\project.ps1 resume` — remove HALT
- Overrides: edit `.autopilot/STATE.md`, add a line beginning with `OPERATOR:` — see
  the header comment in STATE.md for the full override vocabulary.

Key files:

| Path | Purpose |
|---|---|
| `.autopilot/PROMPT.md` | Canonical prompt (IMMUTABLE blocks enforced by hook) |
| `.autopilot/STATE.md` | Live state — iteration, status, active_task, protected_paths |
| `.autopilot/MVP-GATES.md` | 8-gate completion scorecard |
| `.autopilot/CLEANUP-CANDIDATES.md` | Phase A staleness worklist |
| `.autopilot/CLEANUP-LOG.md` | Phase B deletion audit trail |
| `.githooks/protect.sh` | Pre-commit: IMMUTABLE + protected_paths + MVP-GATES integrity |
| `.githooks/commit-msg-protect.sh` | Trailer gates: `IMMUTABLE-ADD:`, `cleanup-operator-approved:` |

## Docs

- [DEV-PROGRESS.md](DEV-PROGRESS.md) — current priorities + loop status
- [IMPROVEMENT-PLAN.md](IMPROVEMENT-PLAN.md) — roadmap
- [PHASE-A-SPEC.md](PHASE-A-SPEC.md), `phase-*-survey.md`, `phase-*-audit.md` — phase work
- [KNOWN-PITFALLS.md](KNOWN-PITFALLS.md) — persistent gotchas
- [TESTING-CHECKLIST.md](TESTING-CHECKLIST.md)
- [capability-matrix.md](capability-matrix.md) — adapter capability surface
- [CLAUDE-APPROVAL-DECISION.md](CLAUDE-APPROVAL-DECISION.md) — audit-only stance rationale
- [git-workflow.md](git-workflow.md) — branching + PR conventions
- [INTERACTIVE-REBUILD-PLAN.md](INTERACTIVE-REBUILD-PLAN.md)

Reference archives (not tracked on main branch of merged PRs; available to the loop):

- `research-archive/` — Codex / Claude feasibility research
- `codex-schema-archive/` — JSON schema dumps of Codex CLI protocol surfaces

## License

TBD.
