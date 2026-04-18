# DAD Validation And Prompt References

Use this file for validation timing, peer prompt rules, and prompt references.

## Peer Prompt Rules

When another peer turn remains, every peer prompt must include:

1. `Read PROJECT-RULES.md first. Then read {agent-contract}.md and DIALOGUE-PROTOCOL.md. If that file points to Document/DAD references, read the needed files there too.`
2. `Session: Document/dialogue/state.json`
3. `Previous turn: Document/dialogue/sessions/{session-id}/turn-{N}.yaml`
4. concrete `handoff.next_task + handoff.context`
5. a relay-friendly summary
6. the mandatory tail block
7. the exact prompt text saved to `handoff.prompt_artifact`, typically `Document/dialogue/sessions/{session-id}/turn-{N}-handoff.md`

The same closeout reply must paste that exact prompt text. In `user-bridged` mode, "saved the artifact" is not enough; the user should not need a second message just to ask for the relay prompt.

Mandatory tail:

```
---
If you find any gap or improvement, fix it directly and report the diff.
If nothing needs to change, state explicitly: "No change needed, PASS".
Important: do not evaluate leniently. Never say "looks good". Cite concrete evidence and examples.
```

## Validation

Use:

- `tools/Validate-Documents.ps1 -Root . -IncludeRootGuides -IncludeAgentDocs -Fix`
- `tools/Validate-CodexSkillMetadata.ps1 -Root .`
- `tools/Validate-DadPacket.ps1 -Root . -AllSessions`
- `tools/Validate-DadBacklog.ps1 -Root .`

Run validation at minimum:

1. after saving a turn packet
2. after saving the handoff prompt artifact referenced by `handoff.prompt_artifact`, when that turn actually emits a peer handoff
3. before recording `suggest_done: true`
4. before resuming a recovered session
5. after backlog linkage changes or when a linked session is closing
6. after editing `.agents/skills/*/SKILL.md` or `agents/openai.yaml`

On a final converged no-handoff turn, `handoff.prompt_artifact` may stay empty and `handoff.ready_for_peer_verification` may stay false. The validator still requires `handoff.done_reason` when `suggest_done: true`.

On a non-final turn, leaving both `handoff.prompt_artifact` and `handoff.ready_for_peer_verification` empty is valid only for an explicit `handoff.closeout_kind: recovery_resume` packet.

Validation passing is not a license to open a meta-only follow-up turn. Wording correction, summary/state sync, closure seal, and validator-noise cleanup should stay inside the active execution turn unless the DAD system itself needs repair.

When another peer turn remains, `handoff.next_task` should still describe outcome work. A dedicated verify-only relay is justified only for remote-visible, config/runtime-sensitive, measurement-sensitive, destructive, or provenance/compliance-sensitive work.

`Validate-DadPacket.ps1` may emit warnings for `peer_handoff` packets that read like meta-only cleanup without a matching risk-gated reason. Treat those warnings as a review signal to collapse the relay back into the current execution turn or state the real risk more concretely.

`Validate-DadPacket.ps1` also enforces that `final_no_handoff` packets keep `handoff.next_task` empty. A closing session has no continuation; remaining follow-up work must be admitted to the backlog in the same closeout path. See `PACKET-SCHEMA.md` for the exact rule and `VALIDATOR-FIRST-DISCOVERY-DEFERRED.md` for the underlying design rationale.

If an upgraded downstream repository still has older `final_no_handoff` packets with `handoff.next_task` populated, migrate them by either clearing stale text or admitting the real follow-up work to the backlog before clearing `handoff.next_task`.

`Validate-DadBacklog.ps1` checks the admission layer, not the execution log. It should enforce one active `promoted` item per active session and keep `now` empty while an active session exists.

`Validate-CodexSkillMetadata.ps1` should also keep runtime `SKILL.md` and `agents/openai.yaml` ASCII-safe because those files must remain UTF-8 without BOM.

## Prompt References

Base references in this template:

- `.prompts/01-system-audit.md`
- `.prompts/02-session-start-contract.md`
- `.prompts/03-turn-closeout-handoff.md`
- `.prompts/04-session-recovery-resume.md`
- `.prompts/05-debate-disagreement.md`
- `.prompts/06-convergence-pr-closeout.md`
- `.prompts/07-existing-project-migration.md`
- `.prompts/08-template-review-hardening.md`
- `.prompts/09-emergency-session-recovery.md`
- `.prompts/10-system-doc-sync.md`
- `.prompts/11-dad-operations-audit.md`

## Large-File Reading Rule

Use `.prompts/06-convergence-pr-closeout.md` on the final converged turn, especially when no peer prompt will follow. That prompt is the checklist for summary/state artifacts plus the required git closeout.

- If a required reference file is too large to read in one call, read the section index first, then read only the needed sections in chunks.
- Do not stop the task only because a monolithic read failed once.
- Prefer splitting large reference docs before adding more fallback wording to prompts.
