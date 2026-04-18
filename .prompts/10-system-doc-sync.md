# 10. System Doc Consistency / Sync

## Purpose

When DAD infrastructure or system rules change during work, prevent drift between code, validators, commands, and documentation.

## System Doc Set

- `AGENTS.md`
- `CLAUDE.md`
- `DIALOGUE-PROTOCOL.md`
- `Document/DAD/` detailed protocol references when the root protocol is thin
- `.claude/commands/`
- `.agents/skills/`
- `.prompts/`
- Operations guides under `Document/`
- `tools/Register-CodexSkills.ps1`
- `tools/Set-CodexSkillNamespace.ps1`
- `tools/Validate-CodexSkillMetadata.ps1`
- `tools/Validate-DadPacket.ps1`
- `tools/Validate-Documents.ps1`

## When To Use

Include this prompt as a default reference when any of the following apply:

1. Session path, packet/state schema, or summary naming rules change
2. A slash command, skill, prompt template, or validator changes
3. A conflict is found between root contract docs
4. A mismatch is found between the actual storage structure and documented rules
5. System operations docs must change together for the work result to make sense

## Execution Rules

1. Confirm the authoritative rule against live files.
2. List every system file that must change.
3. Change them all within the same task where possible.
4. After the changes, re-run at minimum:
   - `tools/Validate-Documents.ps1 -Root . -IncludeRootGuides -IncludeAgentDocs -Fix`
   - `tools/Set-CodexSkillNamespace.ps1 -Namespace "<repo-prefix>"` when skill naming rules changed
   - `tools/Validate-CodexSkillMetadata.ps1 -Root .`
   - `tools/Validate-DadPacket.ps1 -Root . -AllSessions`
5. If they cannot all be closed in the same turn, record system-doc sync as the first item in `handoff.next_task`.

## Forbidden

- Aligning only the code while implicitly deferring system docs
- Bypassing a broken validator by patching only the session files to make them pass
- Marking `PASS` while conflicts remain between root contract docs and subordinate commands / skills / prompts
