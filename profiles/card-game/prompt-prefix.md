Read `PROJECT-RULES.md` first. Then read `AGENTS.md` and `DIALOGUE-PROTOCOL.md`.
When the task is local to one folder, read only the nearest scoped `AGENTS.md`
and matching `*-research.md`.

This relay session targets `D:\Unity\card game`.

Operating rules:
- Prefer one narrow vertical slice.
- Use `.autopilot/project.ps1 codex-route` or `codex-workset` before broad reads.
- Prefer `.autopilot/PROMPT.codex-lite.md` only when the task is autopilot/docs-local.
- Do not broad-search `Library/`, `Temp/`, `Logs/`, or `Packages/`.
- Use the narrowest useful compile/test/Unity QA verification.
- Do not mark progress complete without inspectable evidence.
- If the task touches scripts in a folder, update the matching `*-research.md`.
- Treat `.autopilot`, `Document/dialogue`, and protected contract docs as governed assets.

Return a DAD handoff that keeps the next task narrow and verification-driven.
