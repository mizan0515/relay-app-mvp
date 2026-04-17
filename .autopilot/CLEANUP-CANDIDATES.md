# CLEANUP-CANDIDATES — relay-app-mvp staleness scan output

Autopilot Cleanup Mode Phase A appends candidates here; Phase B (a later iteration, per
the two-pass rule in `[IMMUTABLE:cleanup-safety]`) decides which to actually delete.

Each candidate entry:
- `path:` tracked path relative to repo root
- `last_touch:` ISO date of last git commit touching the file (use `--follow`)
- `kind:` `temp` | `stale-code` | `stale-audit-doc` | `stale-qa-evidence` | `orphan-config`
- `ref_check:` grep-across-repo result (basename + any ID it exports). `none` = safe to
  consider; anything else = NOT stale, drop from candidates.
- `why_stale:` ≤1 line rationale.

Phase B refuses to delete any candidate whose `ref_check` came back non-empty on re-check.

---

## 2026-04-18 — bootstrap pass (initial audit-doc sweep)

Operator action needed: set `OPERATOR: approve cleanup 2026-04-18` in STATE.md to
authorize Phase B deletion of the stale-audit-doc entries, OR leave them for the loop to
re-evaluate in the next Cleanup iteration. The autopilot install commit itself only
proposes the list; it does not delete.

### Candidate list

- path: prototypes/relay-app-mvp/dad-asset-audit.md
  - last_touch: 2026-04-17
  - kind: stale-audit-doc
  - ref_check: (to re-run before Phase B — verify no `DEV-PROGRESS.md` or README reference)
  - why_stale: one-off asset audit superseded by capability-matrix.md + README.md
    "제어 가능한 UI 요소" section. Phase E/F have moved on from the asset audit scope.

- path: prototypes/relay-app-mvp/git-audit.md
  - last_touch: 2026-04-17
  - kind: stale-audit-doc
  - ref_check: (re-run — check DEV-PROGRESS iter 10 refers to git-workflow.md, NOT git-audit.md)
  - why_stale: superseded by `git-workflow.md` (iter 10). git-audit was the survey input;
    git-workflow is the distilled status doc.

- path: prototypes/relay-app-mvp/mcp-audit.md
  - last_touch: 2026-04-17
  - kind: stale-audit-doc
  - ref_check: (re-run — Codex MCP audit, likely referenced in early Phase surveys only)
  - why_stale: MCP direct audit was a one-off investigation; findings either landed in
    `IMPROVEMENT-PLAN.md` or became part of capability-matrix.md.

- path: prototypes/relay-app-mvp/shell-audit.md
  - last_touch: 2026-04-17
  - kind: stale-audit-doc
  - ref_check: (re-run)
  - why_stale: shell-tier audit; findings should have landed in capability-matrix.md.
    If not, promote a sync task rather than delete.

- path: prototypes/relay-app-mvp/codex-windows-matrix.md
  - last_touch: 2026-04-17
  - kind: stale-audit-doc
  - ref_check: (re-run — may still be referenced by capability-matrix.md)
  - why_stale: Windows-specific Codex matrix overlaps with capability-matrix.md;
    consolidation candidate.

- path: prototypes/relay-app-mvp/phase-e-survey.md
  - last_touch: 2026-04-17
  - kind: stale-audit-doc
  - ref_check: (re-run — DEV-PROGRESS recent iterations reference only phase-f-survey.md)
  - why_stale: Phase E closed (iter 11–13); survey doc no longer actionable.

- path: prototypes/relay-app-mvp/phase-e-live-1-audit.md
  - last_touch: 2026-04-17
  - kind: stale-audit-doc
  - ref_check: (re-run)
  - why_stale: Phase E closed; audit historical. Keep in ARCHIVE instead of main tree.

- path: prototypes/relay-app-mvp/phase-e-live-2-audit.md
  - last_touch: 2026-04-17
  - kind: stale-audit-doc
  - ref_check: (re-run)
  - why_stale: Phase E closed; audit historical. Candidate for archive.

### Explicitly KEEP (not candidates — still active)

- `DEV-PROGRESS.md` — live state
- `DEV-PROGRESS-ARCHIVE.md` — live history pointer
- `KNOWN-PITFALLS.md` — seeds `.autopilot/PITFALLS.md`
- `phase-f-survey.md` — current Phase F scope (referenced in DEV-PROGRESS Next priority)
- `capability-matrix.md` — canonical Claude-vs-Codex capability distinction
- `git-workflow.md` — current canonical git process
- `README.md`, `IMPROVEMENT-PLAN.md`, `INTERACTIVE-REBUILD-PLAN.md`, `PHASE-A-SPEC.md`,
  `TESTING-CHECKLIST.md`, `CLAUDE-APPROVAL-DECISION.md`, `EXTERNAL-REVIEW-PROMPT.md`
  — active reference material.

### Also untracked (NOT a cleanup concern — either user-uncommitted work or build dirs)

- `RelayApp.*/bin/`, `RelayApp.*/obj/` — should be in `.gitignore`; verify root `.gitignore`
  actually excludes them. If tracked accidentally, separate task.
- `AUTONOMOUS-DEV-PROMPT.md`, `AUTONOMOUS-DEV-PROMPT-COPY.txt` — superseded by
  `.autopilot/PROMPT.md` + `.autopilot/RUN.txt` in this install. Replaced with a stub
  pointer in the same PR (not deleted, to keep bookmarks working).
