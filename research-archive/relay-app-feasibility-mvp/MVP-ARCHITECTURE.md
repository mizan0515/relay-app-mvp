# MVP Architecture

Date: 2026-04-15

## Target shape

Keep the MVP architecture small:

```text
+-----------------------+
| WPF Desktop App       |
| - status UI           |
| - session controls    |
| - error surface       |
+-----------+-----------+
            |
            v
+-----------------------+
| Broker Core           |
| - session state       |
| - handoff parser      |
| - relay logic         |
| - repair loop         |
+-----+-------------+---+
      |             |
      v             v
+-----------+   +-----------+
| Codex     |   | Claude    |
| Adapter   |   | Adapter   |
+-----------+   +-----------+
```

## Broker responsibilities

- own active session state
- own active side and turn counter
- validate handoff JSON
- dedupe relay submissions
- issue repair prompts
- stop on repeated failure

## Adapter responsibilities

### Codex adapter

Preferred:

- `codex app-server`

Fallback:

- machine-readable `codex` execution surface

The adapter should expose a normalized interface like:

- `StartSession`
- `ResumeSession`
- `SendPrompt`
- `WaitForCompletion`
- `GetFinalOutput`

### Claude adapter

Preferred:

- `claude` CLI with `--output-format json` or `stream-json`
- resume/continue support

The interface should match the same normalized shape as Codex.

## Persistence choice

Keep it small for MVP:

- one `sessions.json` or `sessions.db`
- one per-session `.jsonl` event log

If you want the fastest start, use:

- JSON file for session metadata
- JSONL for events

If you want slightly better structure from day 1, use SQLite.

Recommendation:

- SQLite if you are already comfortable with it
- otherwise JSON + JSONL for the first cut

## Repair loop

On completion:

1. parse final output
2. if handoff valid, relay
3. if invalid, send repair prompt
4. retry parse
5. stop after 2 failed repairs

## UI requirements

Single window is enough.

Must show:

- active session id
- active side
- current turn
- adapter health
- latest handoff preview
- repair count
- last failure

Must provide buttons:

- Start
- Pause
- Resume
- Stop
- Retry Repair
- Open Log Folder

## Deliberate simplifications

- no multi-session scheduler
- no distributed broker
- no background Windows Service
- no OCR fallback
- no plugin system

## Why WPF for MVP

Use WPF unless there is a strong reason not to.

Reasons:

- faster to ship than a more polished packaged app route
- good tooling
- easy local desktop utility fit
- no packaging overhead required for the first real build
