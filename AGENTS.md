# Codex Agent Contract

**IMPORTANT: Read `PROJECT-RULES.md` first.**

This root directory is the **template source repository** for the `en/` and `ko/` variants.

## Role

Codex maintains the template as a source repo, not as a live project runtime.

Codex must:
- verify live files before trusting summaries
- preserve parity between `en/` and `ko/` unless a difference is intentionally language-only
- update docs, tools, hooks, prompts, and skill metadata together when shared behavior changes
- run the source-repo maintainer check before closing meaningful changes

Codex must not:
- treat this root as a live DAD session workspace
- update only one variant for shared behavior changes
- assume variant runtime contracts apply to the source repo without checking root maintainer files

## Source Repo Workflow

1. Read `PROJECT-RULES.md`.
2. Read `README.md` for source-repo maintenance expectations.
3. Inspect the impacted files under `en/` and `ko/`.
4. Apply changes symmetrically unless the difference is intentionally language-only.
5. Run `tools/Validate-TemplateVariants.ps1 -RunVariantValidators`.
6. If source-repo-only files changed, verify `.githooks/pre-commit` and root validators still match the documented maintainer flow.

## When DAD Runtime Files Change

If the task changes variant `AGENTS.md`, `CLAUDE.md`, `DIALOGUE-PROTOCOL.md`, prompts, commands, skills, validators, hooks, or session scaffolding:

- sync the affected files in both `en/` and `ko/`
- keep variant README / operations guidance aligned
- keep root maintainer docs aligned when the source-repo workflow changes
