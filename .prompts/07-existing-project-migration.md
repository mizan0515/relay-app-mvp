# 07. Existing Project Introduction / Migration

## Purpose

When introducing DAD v2 into a repository that is already running, organize the introduction order so it does not conflict with existing rules, docs, or automation.

## Baseline Inspection Items

1. Whether existing root rule docs conflict with `PROJECT-RULES.md`, `AGENTS.md`, or `CLAUDE.md`
2. Whether slash commands, agent skills, prompts, or automation systems already exist
3. Whether session / review / operations docs already exist under `Document/` or similar folders
4. Whether git policy, branch policy, or PR policy differs from template defaults
5. Where repo-specific research / inventory / architecture docs live
6. How to run existing validators, linters, and test runners together with the DAD validator

## Introduction Procedure

1. Decide the authoritative doc first. If existing docs exist, specify what is inherited and what is replaced.
2. Fill in `PROJECT-RULES.md` with the repo's actual rules.
3. Adjust `AGENTS.md`, `CLAUDE.md`, and `DIALOGUE-PROTOCOL.md` for the repo environment.
4. Align `.claude/commands/`, `.agents/skills/`, `.prompts/`, and the `Document/` operations guide with the root contract.
5. Apply a project-specific Codex skill namespace, validate `.agents/skills/` metadata, and then register the Codex Desktop skills for the repository (`Set-CodexSkillNamespace`, then `Validate-CodexSkillMetadata`, then `Register-CodexSkills`).
6. Make `tools/Validate-Documents.ps1 -Root . -IncludeRootGuides -IncludeAgentDocs -Fix` pass first.
7. If needed, run a smoke test with one sample session: `New-DadSession`, `New-DadTurn`, `Validate-DadPacket`.
8. Before any live work, re-run the system audit prompt (`01`) to re-check overall coherence.

## Merge Strategy

When the existing repo already has `CLAUDE.md`, `AGENTS.md`, `.claude/commands/`, etc.:

1. Back up the existing files (`{filename}.pre-dad.bak`).
2. **Append** the DAD v2 sections to the existing files without deleting existing rules.
3. Explicitly list the conflict points (e.g. git policy, branch rules, verification procedures) and decide which side to follow.
4. Record the decision in `PROJECT-RULES.md` (which rules were inherited, which replaced).
5. After the merge, make `tools/Validate-Documents.ps1 -Root . -IncludeRootGuides -IncludeAgentDocs -Fix` pass.

## Rollback Path

If validators or operational smoke tests break after the merge:

1. Immediately restore the original files backed up as `.pre-dad.bak`.
2. Remove or revert only the DAD-related directories newly added in this introduction (`.claude/commands/`, `.agents/skills/`, `.prompts/`, `Document/dialogue/`, `tools/`).
3. Record which files were restored and which DAD items were removed.
4. Log the conflict cause in `PROJECT-RULES.md` or a separate operations memo, then refine the merge strategy for the next introduction.

## Forbidden

- Overwriting existing project rules with template docs without reading them
- Passing only the DAD validator while ignoring repo-specific verification procedures
- Treating the introduction as complete after moving only part of the commands / skills / prompts / validators
