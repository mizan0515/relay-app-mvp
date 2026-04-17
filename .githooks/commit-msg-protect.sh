#!/usr/bin/env bash
# .githooks/commit-msg-protect.sh
#
# Trailer-dependent gates for the relay-app-mvp autopilot. Runs as commit-msg
# hook because the trailer text is only reliably available after the message
# is finalized.
#
# Enforces:
#   A. New IMMUTABLE blocks in .autopilot/PROMPT.md require
#      'IMMUTABLE-ADD: <block-name>' trailer (per block).
#   B. >5 deletions OR any sensitive-path deletion requires
#      'cleanup-operator-approved: yes' trailer.

set -euo pipefail

MSG_FILE="${1:-}"
if [ -z "$MSG_FILE" ] || [ ! -f "$MSG_FILE" ]; then
  echo "commit-msg-protect: expected commit message file as \$1"
  exit 1
fi

PROMPT=".autopilot/PROMPT.md"

has_trailer() {
  local key="$1"
  grep -qE "^${key}[[:space:]]*:" "$MSG_FILE"
}

# ---------------------------------------------------------------------------
# Gate A: new IMMUTABLE block detection (PROMPT.md must be staged).
# ---------------------------------------------------------------------------
staged=$(git diff --cached --name-only)
if printf '%s\n' "$staged" | grep -qx "$PROMPT" && git rev-parse --verify HEAD >/dev/null 2>&1; then
  tmp_base=$(mktemp); tmp_head=$(mktemp)
  trap 'rm -f "$tmp_base" "$tmp_head"' EXIT

  git show "HEAD:$PROMPT" > "$tmp_base" 2>/dev/null || : > "$tmp_base"
  git show ":$PROMPT"     > "$tmp_head"

  new_markers=$(grep -oE '\[IMMUTABLE:BEGIN [a-z-]+\]' "$tmp_head" | sort -u || true)
  old_markers=$(grep -oE '\[IMMUTABLE:BEGIN [a-z-]+\]' "$tmp_base" | sort -u || true)

  added=$(comm -23 <(printf '%s\n' "$new_markers") <(printf '%s\n' "$old_markers") \
    | sed -E 's/^\[IMMUTABLE:BEGIN ([a-z-]+)\]$/\1/')

  for name in $added; do
    [ -z "$name" ] && continue
    if ! grep -qE "^IMMUTABLE-ADD:[[:space:]]*${name}[[:space:]]*$" "$MSG_FILE"; then
      echo "commit-msg-protect: new IMMUTABLE block '$name' introduced without authorization."
      echo "  Commit message must include on its own line: 'IMMUTABLE-ADD: $name'"
      echo "  This prevents self-evolution from granting itself new charter."
      exit 1
    fi
  done
fi

# ---------------------------------------------------------------------------
# Gate B: delete thresholds + sensitive-path deletions.
# ---------------------------------------------------------------------------
deleted_paths=$(git diff --cached --name-only --diff-filter=D || true)
deleted_count=$(printf '%s\n' "$deleted_paths" | grep -cv '^$' || true)

# Sensitive paths: anything that would disable the loop's safety surface.
SENSITIVE_RE='^(RelayApp\.(Core|Desktop|CodexProtocol|CodexProtocol\.Spike)/|[A-Za-z0-9_.-]+-audit\.md$|phase-[a-z]-[a-z0-9-]+\.md$|\.githooks/|\.autopilot/)'
sensitive_hits=$(printf '%s\n' "$deleted_paths" | grep -E "$SENSITIVE_RE" || true)

needs_approval="no"
reason=""
if [ "$deleted_count" -gt 5 ]; then
  needs_approval="yes"
  reason="commit deletes $deleted_count files (>5)"
fi
if [ -n "$sensitive_hits" ]; then
  needs_approval="yes"
  reason="${reason:+$reason; }commit deletes sensitive paths"
fi

if [ "$needs_approval" = "yes" ]; then
  if ! has_trailer "cleanup-operator-approved"; then
    echo "commit-msg-protect: $reason."
    echo "  Requires 'cleanup-operator-approved: yes' trailer."
    if [ -n "$sensitive_hits" ]; then
      echo "  Sensitive deletions:"
      printf '    %s\n' $sensitive_hits
    fi
    exit 1
  fi
fi

exit 0
