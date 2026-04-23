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
- If Unity MCP is configured for this peer, prefer Unity MCP for editor verification (`read_console`, `refresh_unity`, targeted test or QA entry points) instead of broad shell logs.
- In the final handoff, say explicitly whether Unity MCP was used and name the MCP tools used, or say `Unity MCP not used`.
- Do not mark progress complete without inspectable evidence.
- If the task touches scripts in a folder, update the matching `*-research.md`.
- Treat `.autopilot`, `Document/dialogue`, and protected contract docs as governed assets.
- If the slice needs live Unity verification, prefer Unity MCP for scene refresh, console checks, QA menu execution, screenshots, and focused tests before falling back to raw editor guessing.
- Good Unity MCP candidate work: `refresh_unity`, `read_console`, `execute_menu_item` for `Tools/QA/*`, focused `run_tests`, and scene validation that produces compact `[QA-*]` evidence.
- In the final handoff, explicitly state whether Unity MCP was used. If it was not used, say `Unity MCP not used` and explain why in one short sentence.

Return a DAD handoff that keeps the next task narrow and verification-driven.
