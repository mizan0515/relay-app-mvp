# Relay App MVP

Date: 2026-04-15

## Purpose

This folder is the reduced-scope counterpart to the service-grade relay design.

The full production-oriented documents remain in:

- `research/relay-app-feasibility/`

This folder exists to answer a narrower question:

- what is the smallest Windows app that is still genuinely usable for one operator running one DAD-style relay session at a time?

## MVP objective

Build a local Windows desktop app that can:

- start or attach one Codex-side session
- start or attach one Claude-side session
- wait for one side to finish a turn
- parse one strict handoff JSON block
- relay that prompt to the other side
- detect missing handoff
- run up to 2 repair attempts
- pause and surface the problem to the user if repair fails

## Non-goal

This MVP is not trying to be a polished service product.

It does not need:

- multi-user support
- team deployment
- packaging and auto-update
- advanced approval policies
- OCR-heavy desktop automation
- multiple concurrent sessions

## Recommended MVP shape

- Windows desktop app
- one broker process
- one active session at a time
- Codex via native automation surface first
- Claude via CLI JSON/stream-json first
- visible UI for status and manual intervention

## Files in this folder

- `MVP-SCOPE.md`
- `MVP-ARCHITECTURE.md`
- `MVP-ROADMAP.md`

## Relationship to the full design

Use this folder for the first build.

Use the full service-grade folder later when:

- the first broker works reliably
- one-session relay is stable
- crash recovery and supportability become the next bottleneck
