# CLEANUP-CANDIDATES — staged deletion proposals

Populated by Idle-upkeep OR by an explicit Cleanup-mode discovery pass.
Candidates must live here ≥1 full iteration before a deletion PR opens.
See `[IMMUTABLE:cleanup-safety]` in `.autopilot/PROMPT.md` for the full rules.

Two-pass rule (summary): discovery run adds the entry; a later iteration
(strict: ≥1 full iter later, not same-pass) may open the deletion PR if
the ref-check still comes back empty.

Format:

```
## <ISO-date>
### <relative-path>
- last-git-touch: <ISO>
- ref-check: <grep hit summary or "none">
- why-stale: <≤1 line rationale>
- scope: <same-pass | aged>
```

Aged entries (listed for a previous iteration) become eligible this pass.
Remove entries after the corresponding PR merges, or once a re-check finds
new references.

---

(No entries yet — discovery runs populate this file.)
