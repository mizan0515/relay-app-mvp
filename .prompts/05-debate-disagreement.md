# 05. Debate / Disagreement Handling

## Purpose

When Codex and Claude Code diverge on PASS/FAIL for the same checkpoint, resolve based on evidence (not opinion) and either converge or escalate.

## When To Use

- One side says PASS, the other FAIL
- Interpretations of code / docs / validator conflict
- The same checkpoint keeps getting stuck

## Procedure

1. State exactly which checkpoint the verdicts diverged on.
2. Each side writes evidence separately for code, tests, logs, and docs.
3. Split the peer's evidence into what you accept vs. what you rebut.
4. Where possible, confirm PASS on the agreed portions and narrow the remaining dispute.
5. If no agreement within 3 rounds, escalate to the user.

## Quality Standards

- Forbid phrases like "looks good" or "mostly right"
- Forbid verdicts without a line reference or executed evidence
- If partial agreement is possible, do not smear the whole thing as FAIL
