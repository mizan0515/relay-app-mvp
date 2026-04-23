# CLEANUP-LOG — audit trail for autonomous deletion commits

Every `cleanup:`-prefixed commit MUST append an entry here per
`[IMMUTABLE:cleanup-safety]` rule 6. No audit line → the commit itself is
evidence of rule break. Append, never rewrite history in this file.

Format:

```
## <ISO-timestamp> — <short-sha>
- pr: <url>
- iteration: <N>
- files-deleted:
  - <relative/path/one>
  - <relative/path/two>
- rollback: git revert <sha>
- evidence: <pointer — CLEANUP-CANDIDATES entry date OR ref-check log path>
- notes: <≤1 line, optional>
```

---

(No entries yet — the first cleanup commit populates this file.)
