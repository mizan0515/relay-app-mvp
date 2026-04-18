# Source Repo Rules

This root directory is the **template source repository**, not a project runtime root.

## Source Of Truth

1. Root maintainer files in this directory govern source-repo maintenance:
   - `README.md`
   - `PROJECT-RULES.md`
   - `AGENTS.md`
   - `CLAUDE.md`
   - `DIALOGUE-PROTOCOL.md`
   - `tools/Validate-TemplateVariants.ps1`
   - `.githooks/pre-commit`
2. Variant runtime contracts live under `en/` and `ko/`.
3. When a rule for the source repo conflicts with a rule inside a variant, preserve source-repo parity first, then update both variants coherently.

## Maintainer Reality

- The source repo ships two functionally equivalent variants: `en/` and `ko/`.
- Changes to shared DAD behavior must be reflected in both variants unless the difference is intentionally language-only.
- Root maintenance is complete only after variant parity and validator parity are both re-verified.

## Required Maintainer Checks

- Run `powershell -ExecutionPolicy Bypass -File tools/Validate-TemplateVariants.ps1 -RunVariantValidators` before closing source-repo changes.
- Treat a root-only change as incomplete if it affects variant docs, tools, hooks, prompts, or skill metadata and the corresponding variant updates are missing.

## Guardrails

- Do not treat this root directory as a live DAD session workspace.
- Do not add runtime-only files such as root `Document/dialogue/state.json` here.
- Keep frequently read root docs thin; move detailed operational rules into the variants.
