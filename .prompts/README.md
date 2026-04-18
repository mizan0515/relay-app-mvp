# .prompts

Prompt library for the reusable DAD v2 template.

## Included Prompts

| File | Purpose |
|------|---------|
| `01-system-audit.md` | Generic repository/system audit prompt |
| `02-session-start-contract.md` | New session kickoff and contract drafting |
| `03-turn-closeout-handoff.md` | Turn closeout, packet, and handoff cleanup |
| `04-session-recovery-resume.md` | Session recovery and safe resume |
| `05-debate-disagreement.md` | Debate and disagreement handling |
| `06-convergence-pr-closeout.md` | Convergence closeout, summary, branch, and PR checklist |
| `07-existing-project-migration.md` | Introduce DAD v2 into an existing repository safely |
| `08-template-review-hardening.md` | Review and harden the template itself before reuse |
| `09-emergency-session-recovery.md` | Force-close and manually recover a broken DAD session safely |
| `10-system-doc-sync.md` | System-doc / validator / command sync prompt |
| `11-dad-operations-audit.md` | Audit live DAD behavior, validator blind spots, and prompt-pack drift |

## Usage

- Use `01-system-audit.md` when auditing a new repository or checking whether the DAD system is coherent after changes.
- Use `02-session-start-contract.md` when creating Turn 1 and drafting the initial contract.
- Use `03-turn-closeout-handoff.md` before finalizing any turn packet and peer prompt, including a final no-handoff closeout turn.
- Use `04-session-recovery-resume.md` when resuming a paused or interrupted session.
- Use `05-debate-disagreement.md` when peer verdicts diverge on the same checkpoint.
- Use `06-convergence-pr-closeout.md` when both agents are near done and you need to close the session without skipping summaries, validation, branch hygiene, or PR steps, including the final no-handoff turn.
- Use `07-existing-project-migration.md` before enabling DAD v2 in a repository that already has its own rules, commands, or automation.
- Use `08-template-review-hardening.md` when Claude Code should audit and improve the template repository itself.
- Use `09-emergency-session-recovery.md` when `state.json`, turn packets, or validators are broken enough that normal resume flow is unsafe.
- Use `10-system-doc-sync.md` whenever a task changes protocol docs, validators, session schema, slash commands, skills, or prompt templates.
- Use `11-dad-operations-audit.md` after real sessions accumulate and you need to audit the live DAD operating model rather than only the template design.
- Add project-specific prompts here as the target repository grows.
