# Handoff Schema

Date: 2026-04-15

## Purpose

Define the exact relay contract between agents.

This schema exists to prevent the broker from guessing whether an assistant "probably emitted a handoff."

The broker accepts only this schema.

## Transport forms

Allowed transport forms:

- raw JSON object in assistant text
- fenced JSON block with no extra prose inside the block

Preferred form:

```json
{
  "type": "dad_handoff",
  "version": 1,
  "source": "codex",
  "target": "claude",
  "session_id": "2026-04-15-example",
  "turn": 4,
  "ready": true,
  "prompt": "Read PROJECT-RULES.md first. Then read ...",
  "summary": [
    "Checkpoint C1-C3 PASS.",
    "Repair needed for C4."
  ],
  "requires_human": false,
  "reason": "",
  "created_at": "2026-04-15T23:59:59+09:00"
}
```

## Required fields

### `type`

- must equal `dad_handoff`

### `version`

- integer
- current value: `1`

### `source`

- one of `codex`, `claude`

### `target`

- one of `codex`, `claude`
- must not equal `source`

### `session_id`

- non-empty string
- must match the broker's active session id

### `turn`

- integer
- must equal the source side's completed turn number

### `ready`

- boolean
- `true` means the target may receive `prompt`
- `false` means no relay should occur automatically

### `prompt`

- string
- required when `ready = true`
- must be the exact prompt to relay

### `summary`

- array of short strings
- recommended length: 1 to 10 items
- intended for broker UI and log summaries

### `requires_human`

- boolean
- if `true`, broker must stop before relaying

### `reason`

- string
- required when `ready = false` or `requires_human = true`

### `created_at`

- ISO 8601 timestamp with offset

## Validation rules

The broker should reject the handoff if any of these are true:

- JSON is invalid
- unknown schema version
- `source == target`
- `session_id` mismatch
- `turn` is stale or already consumed
- `ready = true` but `prompt` is empty
- `requires_human = true` and `reason` is empty
- handoff hash already accepted for `(session_id, target, turn)`

## Canonicalization for hashing

To prevent duplicate relay, compute a stable hash from canonical JSON:

- sort keys
- trim trailing whitespace from string values
- normalize line endings in `prompt` to `\n`
- preserve Unicode as-is

Recommended dedupe key:

- `session_id`
- `target`
- `turn`
- `sha256(canonical_json)`

## Repair protocol

If no valid handoff exists at turn completion:

1. broker marks turn as `handoff_missing`
2. broker injects a narrow repair prompt
3. broker waits for one more completion
4. broker retries parse
5. broker stops after max repair budget

Recommended repair prompt:

```text
Output exactly one valid JSON handoff object.
Do not add commentary.
Do not restate analysis.
Schema: type, version, source, target, session_id, turn, ready, prompt, summary, requires_human, reason, created_at.
```

## Optional future fields

Reserve these for future versions, not v1:

- `artifacts`
- `checkpoint_results`
- `approval_hints`
- `estimated_runtime_sec`
- `priority`
- `resume_token`

## Why not YAML

YAML is more human-friendly, but JSON is safer for machine parsing because:

- fewer ambiguity cases
- stable canonicalization
- easier hashing
- easier streaming and validation

Use JSON in the broker protocol even if the human-facing DAD documents remain YAML.
