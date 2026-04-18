# 08. Template Review / Hardening

## Purpose

Check whether this template itself is generic enough for reuse in other repositories, safe even in an empty state, and whether its docs, commands, skills, and validators interlock correctly — then improve it.

## Claude Code Execution Prompt

```text
Read PROJECT-RULES.md first.
Then read CLAUDE.md and DIALOGUE-PROTOCOL.md.
Then read AGENTS.md.
If `DIALOGUE-PROTOCOL.md` points to `Document/DAD/` references, read the needed reference files there too.
Also read .prompts/01-system-audit.md, .prompts/07-existing-project-migration.md, and .prompts/10-system-doc-sync.md first.

Context:
- Repository: DAD v2 system template
- Goal: review the template itself, not a product feature
- Treat this repository as a reusable starter for other projects

Task:
Audit this template and determine whether anything else should be added, removed, generalized, or hardened before reuse in another repository.
Focus on:
1. Root contract coherence across AGENTS.md, CLAUDE.md, DIALOGUE-PROTOCOL.md, PROJECT-RULES.md
2. Template portability: remove project-specific assumptions, stack-specific wording, and hidden repo dependencies
3. Prompt / command / skill / validator / guide consistency
4. Empty-template safety: validators, session layout, bootstrap flow, and failure modes before the first live session exists
5. Migration readiness for existing repositories, not just greenfield setup
6. Missing operational prompts, missing safeguards, or weak enforcement points

Execution rules:
- Verify against live files, not memory.
- Be strict. Do not say "looks good" without file/line evidence.
- If you find a real FAIL, fix it directly and show the diff.
- If no change is needed for an audit item, say exactly "No change needed, PASS".
- If a gap cannot be closed in the same turn, make it the first explicit next task.
- If a required file is too large to read in one call, read it by section or chunk instead of stopping.

Output format:
For each audit item, report PASS / FAIL / WARN with file paths, line references, current value, expected value, and diff for any fix.
Keep findings severity-ordered.

---
If you find any gap or improvement, fix it directly and report the diff.
If nothing needs to change, state explicitly: "No change needed, PASS".
Important: do not evaluate leniently. Never say "looks good". Cite concrete evidence and examples.
```

## When To Use

- Final quality check before distributing the template for the first time
- When re-evaluating generality before applying to multiple projects
- When re-checking template drift after touching two or more of commands / skills / validators / prompts
