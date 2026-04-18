# Template Source Repo Protocol

This root directory is the **template source repository**, not a live DAD runtime.

## Purpose

- maintain parity between `en/` and `ko/`
- keep root maintainer files aligned with variant behavior
- verify that shared changes update docs, tools, hooks, prompts, skills, and validators together

## Source Repo Turn Flow

1. Read `PROJECT-RULES.md`.
2. Read `AGENTS.md` or `CLAUDE.md`.
3. Inspect the affected files in both `en/` and `ko/`.
4. Apply shared behavior changes symmetrically unless a difference is intentionally language-only.
5. Re-run `tools/Validate-TemplateVariants.ps1 -RunVariantValidators`.
6. Update root maintainer docs if the source-repo workflow changed.

## What Stays Out Of Root

- no root `Document/dialogue/state.json`
- no root live-session packets
- no source-repo-only drift from one variant to the other

Detailed runtime DAD rules belong inside the variants, not in this root protocol:

- `en/DIALOGUE-PROTOCOL.md`
- `ko/DIALOGUE-PROTOCOL.md`
- variant `Document/DAD/` reference docs
