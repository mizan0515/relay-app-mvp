# 01. System Audit

## Purpose

Check whether the DAD v2 system and its related docs, tooling, and session artifacts actually interlock and operate in the current repository.

## Baseline Audit Items

1. Rule conflicts between `AGENTS.md`, `CLAUDE.md`, `DIALOGUE-PROTOCOL.md`, and any detailed `Document/DAD/` references that the root protocol points to
2. Whether paths and procedures in `.claude/commands/`, `.agents/skills/`, and `.prompts/` match the root contract docs
3. Whether the `Document/dialogue/` session structure matches validator expectations
4. Whether `tools/Validate-DadPacket.ps1` and `tools/Validate-Documents.ps1` behave correctly against real artifacts
5. Whether remaining document drift was fixed in the same turn or recorded as an explicit follow-up task

## Output Format

- `PASS`: no issue, cite file/line evidence
- `FAIL`: issue found, show current value / expected value / fix diff
- `WARN`: not an immediate violation, but follow-up hardening is recommended

## Principles

- Live files first
- Do not trust validator output blindly; the validator itself is also an audit target
- Do not re-report issues already fixed; only report remaining gaps
