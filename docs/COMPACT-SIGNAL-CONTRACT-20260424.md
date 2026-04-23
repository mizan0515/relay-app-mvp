# Compact Signal Contract (2026-04-24)

## Purpose

Lessons from live repos showed that routine operator control breaks when status
depends on raw logs or broad scans.

This document defines the generic compact-signal pattern that is safe to reuse
in relay-centric projects without importing product-specific dashboards.

## Required Artifacts

Any relay/operator integration should prefer three small artifacts:

1. live signal JSON
2. short status summary text
3. done marker text

The first read should be one of these artifacts, not a full event log.

## Live Signal Minimum Fields

Suggested minimum JSON fields:

- `status`
- `overall_status`
- `next_action`
- `attention_required`
- `session_id`
- `task_slug`
- `updated_at`
- `signal_marker`
- `done_marker`

These fields are generic enough for relay usage without product-specific
vocabulary.

## Marker Rules

The text summary should start with stable sentinel lines:

- `[LIVE_SIGNAL] ...`
- `[DONE] true|false ...`

The exact tail can vary per project, but the sentinel prefix should stay cheap
to parse.

## Doctor Rules

Generic doctor checks should warn or fail on:

- missing compact artifacts
- stale compact artifacts
- test-only override leaks
- bounded waits that would otherwise trap the operator

Doctor checks should not require reading a full relay log.

## Boundary Rule

This contract is generic only for compact relay/operator state.

Do not upstream:

- product-specific blocker names
- product dashboard layout
- product governance wording
- product evidence keys tied to one runtime

## Example Use

`autopilot-template` can expose these artifacts through `project.ps1 status` or
`project.sh status`.

Product repos may enrich the JSON, but they should keep the compact first-read
surface small and stable.
