# Peer Tuning 2026-04-23

## Goal

Pick relay-safe defaults for Claude and Codex peer turns based on current model availability, recent field reports, and the card-game relay's unattended multi-turn behavior.

## Chosen defaults

- Claude:
  - model: `opus`
  - effort: `high`
- Codex:
  - model: `gpt-5.4`
  - reasoning effort: `high`

## Why Claude is not set to `xhigh` by default

- Anthropic's current guidance for Opus 4.7 says `xhigh` is the best default for many coding tasks.
- That guidance is useful for supervised Claude Code sessions, but the relay here is different:
  - it is unattended for long stretches
  - the user already asked to reduce token burn
  - the real card-game relay cycle already showed a hung turn on Claude
- Recent public reports around Opus 4.7 show a repeated pattern:
  - users like the quality increase
  - many also report noticeably higher token use, especially when `xhigh` is left as the default on long coding sessions
- Because of that, relay defaults should stay one step below the most aggressive setting.

Conclusion:
- use latest Opus for capability
- keep relay default at `high`
- reserve `xhigh` for manual review, architecture, or one-off hard debugging when a human is watching

## Why Codex is set to GPT-5.4 high

- OpenAI's current model guidance says to start with `gpt-5.4` for complex reasoning and coding.
- Relay peer turns are not tiny autocomplete requests. They often need:
  - cross-file reasoning
  - tool use
  - repair / resume turns
  - handoff-quality summaries
- `high` is the safer relay default than `medium` for those tasks.
- `xhigh` was not chosen as the relay default because unattended loops are more sensitive to cost growth and overthinking than supervised one-shot work.

## Operational notes

- Claude Code was upgraded locally from `2.1.109` to `2.1.118`.
- This matters because Opus 4.7 requires Claude Code `2.1.111+`.
- Relay operators can override the defaults with:
  - `CCR_CLAUDE_MODEL`
  - `CCR_CLAUDE_EFFORT`
  - `CCR_CODEX_MODEL`
  - `CCR_CODEX_REASONING_EFFORT`

## Source trail

- Anthropic model config:
  - <https://code.claude.com/docs/en/model-config>
- Anthropic Opus 4.7 Claude Code guidance:
  - <https://claude.com/blog/best-practices-for-using-claude-opus-4-7-with-claude-code>
- Anthropic Claude Code releases:
  - <https://github.com/anthropics/claude-code/releases>
- Public stuck-turn report on Claude Code `2.1.109` with Opus 4.7:
  - <https://github.com/anthropics/claude-code/issues/50727>
- Public token-usage regression reports around recent Claude Code releases:
  - <https://github.com/anthropics/claude-code/issues/41930>
  - <https://github.com/anthropics/claude-code/issues/42249>
- OpenAI model guidance:
  - <https://developers.openai.com/api/docs/models>
  - <https://developers.openai.com/api/docs/models/gpt-5.4>
