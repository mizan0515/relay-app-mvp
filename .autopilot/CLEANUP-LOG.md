# CLEANUP-LOG — relay-app-mvp deletion audit trail

Every Cleanup Phase B commit MUST append an entry here. Format:

```
## <ISO timestamp> — <branch>

- PR: <url>
- Commit sha: <sha>
- Rollback: `git revert <sha>`
- Files deleted:
  - <path>  (kind: <temp|stale-code|stale-audit-doc|stale-qa-evidence|orphan-config>)
  - ...
- Evidence pointer: <CLEANUP-CANDIDATES.md section date>
- Orphan-config / orphan-meta flags: <list or "none">
- Pre-cleanup baseline:
  - dotnet build: <pass|fail> (<warning count>)
  - smoke event counts: <snapshot>
- Post-cleanup regression check: <pending|clean|reverted>
```

No audit line → the commit itself is evidence of cleanup-safety rule #6 violation.
Treat such a commit as a revert candidate.

---

## Entries

(none yet)
