# 11. DAD Operations Audit

## Purpose

Audit whether the DAD v2 system still matches the repository's **live operating reality** after real sessions have accumulated.

This prompt is not limited to checking whether the rules are coherent on paper. It also checks whether prompts, commands, skills, validators, and session artifacts still match the way the repository is actually being used.

## When To Use It

Include this prompt by default when any of the following is true:

1. two or more DAD system files were recently changed
2. live sessions exist and you suspect weak handoffs, premature PASS calls, validator blind spots, or prompt drift
3. the prompt pack looks out of date relative to the current repository structure or operating habits
4. a new maintainer or model is taking over the DAD system
5. you need to ask "is the DAD system still working operationally?" separate from product-feature work

## Audit Scope

Review at least:

- `AGENTS.md`
- `CLAUDE.md`
- `DIALOGUE-PROTOCOL.md`
- `Document/DAD/README.md` and the referenced DAD detail docs when the root protocol is thin
- `.prompts/README.md` and relevant `.prompts/*.md`
- `.claude/commands/`
- `.agents/skills/`
- `Document/DAD-Operations-Guide.md`
- `Document/dialogue/state.json`
- `Document/dialogue/sessions/`
- `tools/Validate-DadPacket.ps1`
- `tools/Validate-Documents.ps1`

## Default Audit Areas

1. Whether the root contracts still describe the same operating rules
   - session paths, Turn Packet schema, `suggest_done` gate, auto-converge, no direct push to main, mandatory tail
2. Whether prompts, commands, and skills still match the current protocol and actual call flow
   - dead references, missing files, stale wording, wrong storage paths
3. Whether the prompt pack is sufficiently split for the repository's current operating needs
   - missing operational prompts
   - prompts that are now obsolete, too weak, or too heavy
4. Whether the operations guide matches live artifacts
   - `state.json`, latest `turn-{N}.yaml`, summaries, session directory structure
5. Whether the validators catch the real problems
   - include validator blind spots in the audit
   - do not assume passing validators means the system is healthy
6. Whether the current repository needs additional operating constraints
   - task size norms
   - user-bridged cost control
   - repeated failure patterns
   - first-clone or first-session bootstrap hazards
   - explicit outcome-scoped session gates
   - peer-verification allowlists
   - anti-churn rules for wording-only / sync-only / seal-only turns
7. Whether recent session history shows outcome work or meta-only churn
   - sessions opened only for wording correction, summary/state sync, closure seal, or validator-noise cleanup
   - peer-verify-only turns without a clear risk trigger
   - session chains that spend more turns on ceremony than on product artifacts, measurements, fixes, or decisions

## Execution Rules

1. Prefer live files and live session artifacts over narrative summaries.
2. Treat validator behavior as audit input, not as authority beyond question.
3. If you find a real FAIL, fix it in the same turn when practical and report the diff.
4. If a gap cannot be closed in the same turn, make it the first explicit next task.
5. Do not write impressionistic verdicts like "looks good". Every judgment needs file or artifact evidence.
6. If a required file is too large to read in one call, read the needed sections in chunks and continue the audit.
7. Call out meta-only session chains explicitly instead of hiding them inside a generic process-health verdict.

## Output Format

Use these labels per finding:

- `PASS`: current rules and live artifacts agree; include file/line or artifact evidence
- `FAIL`: include current value, expected value, operational impact, and diff
- `WARN`: not an immediate violation yet, but drift risk is building; include why it matters now and the recommended next step

## Recommended Companion References

- When fixing system drift directly, also use `10-system-doc-sync.md`
- When checking whether validators and session structure are still trustworthy, read live `Document/dialogue/` artifacts together with recent summaries
- When reassessing the operating model itself, sample recent work-session notes or chat-log artifacts if the target repository keeps them, instead of trusting only the protocol docs
