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

## Source Repo Workflow

1. Read `PROJECT-RULES.md`.
2. Read `README.md`.
3. Inspect the impacted files under `en/` and `ko/`.
4. Apply shared behavior changes symmetrically unless the difference is intentionally language-only.
5. Run `tools/Validate-TemplateVariants.ps1 -RunVariantValidators`.
6. If source-repo-only files changed, verify `.githooks/pre-commit` and root validators still match the documented maintainer flow.

## Related Files

- `AGENTS.md` - Codex source-repo contract
- `DIALOGUE-PROTOCOL.md` - source-repo maintenance and parity protocol
- `en/` / `ko/` - actual runtime template variants
