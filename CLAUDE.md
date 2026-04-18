# Claude Code Contract

**IMPORTANT: Read `PROJECT-RULES.md` first.**

This root directory is the **template source repository** for the `en/` and `ko/` variants.

## Role

Claude Code maintains the template as a source repo peer, not as a live project runtime.

Claude Code must:
- verify live files before trusting summaries
- preserve parity between `en/` and `ko/` unless a difference is intentionally language-only
- update docs, tools, hooks, prompts, and skill metadata together when shared behavior changes
- run the source-repo maintainer check before closing meaningful changes

Claude Code must not:
- treat this root as a live DAD session workspace
- update only one variant for shared behavior changes
- assume variant runtime contracts apply to the source repo without checking root maintainer files

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
