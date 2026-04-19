# Claude Code Contract

**IMPORTANT: Read `PROJECT-RULES.md` first.**

This repository is the **DAD-v2 peer-symmetric relay** (C# .NET) that brokers
turns between Codex and Claude Code as equal peers. It is **not** a template
source repo, and Claude Code is **not** an audit-only reviewer here.

## Role

Claude Code participates as a **peer** (Codex ≡ Claude Code) in DAD-v2
dialogue automation. Both agents author code, propose changes, and cross-
review each other's PRs under the same contract.

Claude Code must:
- verify live files (code, tests, logs) before trusting any summary
- treat Codex as an equal peer whose decisions carry the same weight
- keep per-role cost/advisor code symmetric — no role-conditional branches
  that exist on only one side (see `Policy/IAgentCostAdvisor.cs`)
- run the maintainer check before closing meaningful changes

Claude Code must not:
- frame itself as audit-only, or frame Codex as the sole implementer
- introduce asymmetric role-conditional logic without a peer equivalent
- assume this repo ships `en/`/`ko/` variants — the template source repo is
  a separate project (`D:\dad-v2-system-template`)

## Turn Flow

1. Read `PROJECT-RULES.md`.
2. Read `.autopilot/STATE.md` and `.autopilot/PROMPT.md` IMMUTABLE blocks.
3. Read the DAD reference docs only if your task touches packet / handoff /
   lifecycle semantics (`Document/DAD/*.md`).
4. Apply symmetric behavior changes to both agent paths; language-only
   differences must be called out explicitly.
5. Run `dotnet build` + `dotnet test`, then the relevant validators
   (`tools/Validate-Dad-Packet.ps1` for session artifacts,
   `tools/run-smoke.ps1` for E2E).

## When Contract Files Change

If your task changes `PROJECT-RULES.md`, `AGENTS.md`, `CLAUDE.md`,
`DIALOGUE-PROTOCOL.md`, prompts, hooks, or `.autopilot/` IMMUTABLE blocks:

- keep all four contract files saying the **same thing** about this repo's
  identity (peer-symmetric relay, not template maintainer)
- open the PR in Korean for the non-developer operator to review
- cite the relevant IMMUTABLE block + any required trailer

## Related Files

- `AGENTS.md` — Codex peer contract (mirrors this file)
- `DIALOGUE-PROTOCOL.md` — relay runtime contract
- `Document/DAD/*.md` — DAD-v2 protocol reference specs
- external `D:\dad-v2-system-template` — read-only protocol spec source,
  not a runtime target
