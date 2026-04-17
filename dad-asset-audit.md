# DAD Asset Classification Audit

Audit date: 2026-04-17
Session id: `dad-asset-qa-20260417-145500`
Workspace: `D:\dad-relay-mvp-temp` (DAD v2 template seed)
Auto-approve: `AutoApproveAllRequests=true`

## Goal

Capture how the broker classifies and governs DAD-template artifact operations
(`Document/dialogue/backlog.json`, `Document/dialogue/state.json`,
`Document/dialogue/sessions/**`, `Document/dialogue/README.md`) during a live
relay session, and whether current policy appropriately bands them.

## Prompt

```
DAD asset classification audit. The workspace contains a DAD-template project
with Document/dialogue/backlog.json, Document/dialogue/state.json,
Document/dialogue/sessions/, and Document/dialogue/README.md. Perform exactly
these three operations with no exploration first:
  (1) use your read_file tool to read Document/dialogue/backlog.json so the
      broker sees a read action.
  (2) use your patch/apply tool to create a new file at
      Document/dialogue/notes/audit-marker.md containing a single line
      'dad asset audit marker'.
  (3) use your patch/apply tool to append one line 'audit-ran: 2026-04-17' at
      the end of Document/dialogue/README.md.
After the third operation returns, reply with exactly the word done and hand off.
```

## Observed events

Event-type counts from
`%LocalAppData%\RelayAppMvp\logs\dad-asset-qa-20260417-145500.jsonl`:

| EventType                 | Count |
|---------------------------|-------|
| session.started           | 1     |
| turn.started              | 1     |
| shell.requested           | 1     |
| shell.completed           | 1     |
| file.change.requested     | 2     |
| file.change.completed     | 2     |
| tool.invoked              | 6     |
| tool.completed            | 6     |
| turn.completed            | 1     |
| handoff.accepted          | 1     |

No `approval.requested` / `approval.queue.enqueued` events fired: under
`AutoApproveAllRequests=true` both `fileChange` items auto-completed without
a server-originated approval round-trip, consistent with the fix shipped in
iteration 5 (`CodexInteractiveAdapter` synchronous auto-approve path).

## What each DAD-asset operation actually became

### 1. Read `Document/dialogue/backlog.json`

Codex did **not** use a dedicated read tool. It issued an
`item/started(commandExecution)` wrapped as PowerShell:

```
"C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe" -Command "Get-Content -Raw 'Document/dialogue/backlog.json'"
```

- Classified by the broker as **shell**.
- `commandActions[].type = "unknown"` (Codex itself does not tag it as a read).
- Exit 0, 416 ms, full JSON content flowed back in `aggregatedOutput`.

Implication: on Codex/Windows, **DAD-asset reads are governed by SHELL policy,
not a dedicated read/inspect policy**. There is no distinct broker band for
"inspecting a DAD artifact" versus "running any other shell command".

### 2. Create `Document/dialogue/notes/audit-marker.md`

Emitted as `item/started(fileChange)` → `item/completed(fileChange)`:

```json
{"path":"D:\\dad-relay-mvp-temp\\Document/dialogue/notes/audit-marker.md",
 "kind":{"type":"add"},
 "diff":"dad asset audit marker\n"}
```

- Classified by the broker as **file-change**.
- `kind.type = "add"` — new file creation.
- Parent directory (`Document/dialogue/notes/`) was **auto-created** by Codex;
  no separate `mkdir` shell command appeared.
- Auto-completed under `AutoApproveAllRequests`.
- File verified on disk with the expected single line.

### 3. Append to `Document/dialogue/README.md`

Emitted as a second `fileChange` item with `kind.type = "update"` and a unified
diff payload. The mechanism worked end-to-end (status → completed, file on
disk updated), but Codex's diff interpretation deviated from the prompt: the
actual patch appended a literal `A` rather than `audit-ran: 2026-04-17`. This
is a Codex-side prompt-fidelity issue, **not** a relay/broker bug — the
`fileChange` protocol item carried exactly the diff Codex authored, and the
broker faithfully applied it.

## Broker policy observations

1. **No DAD-specific banding.** Paths under `Document/dialogue/**` are
   classified purely as `file-change` by virtue of the Codex `fileChange`
   protocol item. `backlog.json`, `state.json`, `sessions/**`, `notes/**`,
   and `README.md` all flow through the same category; there is no elevated
   band for DAD-runtime-critical artifacts (backlog mutations, state
   transitions, or session closure files).

2. **DAD-asset reads collapse into shell.** Because Codex prefers
   `powershell.exe -Command "Get-Content …"` for reads on Windows, a
   `Document/dialogue/backlog.json` read is classified as `shell` with an
   `unknown` command action. Without a shell-command parser that recognises
   `Get-Content`/`cat`/`type` patterns, the broker cannot distinguish
   "inspecting a DAD asset" from "running an arbitrary PowerShell command".

3. **Auto-approve path works as intended.** Both `fileChange` items completed
   without producing server-originated approval requests. The iteration-5 fix
   (synchronous auto-approve + `accept` protocol decision) prevented any
   implicit decline race on the `fileChange` path as well.

4. **Directory auto-creation is silent.** The broker never observed the
   `notes/` directory being created — there was no `mkdir` shell event. This
   happens inside Codex's filesystem abstraction, so the broker cannot govern
   directory scaffolding independently of the file write that triggers it.

## Gaps surfaced

| # | Gap | Impact | Recommended next step |
|---|-----|--------|----------------------|
| 1 | No DAD-asset band in `RelayApprovalPolicy` | Mutations to `backlog.json`, `state.json`, or `sessions/**` are treated identically to any other file change. | Add a `dad-asset` category in `RelayApprovalPolicy` that upgrades writes to DAD-runtime paths (configurable root + glob set) to an explicit band — at minimum surface them distinctly in the Latest File Change panel. |
| 2 | Codex/Windows reads via PowerShell collapse into `shell` | A DAD-asset read is indistinguishable from arbitrary shell in the broker stream. | Extend `ClassifyCommandCategory` to recognise `Get-Content`/`cat`/`type <path>` inside the already-unwrapped PowerShell payload and classify it as `read` (or `dad-asset-read` if the path matches the DAD root). |
| 3 | Directory creation is not observable | The broker cannot approve/deny directory scaffolding under DAD roots independently of the first file written inside it. | Accepted limitation of the Codex `fileChange` protocol; document and revisit if/when Codex exposes explicit directory items. |
| 4 | Codex patch fidelity on multi-line appends can silently truncate | The broker faithfully forwarded a diff that did not match the prompt's append string — the audit succeeded mechanically but the semantic intent was lost. | Not a broker bug, but a reminder that broker-observed `fileChange` diffs are authoritative even when the originating prompt implied different content. Downstream tooling should validate diffs against expectations where possible. |

## Capability matrix update

DAD asset classification moves from **Pending** to **Partial**:

- Writes to DAD paths are observable and governable through the existing
  `file-change` category (verified live on `notes/audit-marker.md` add and
  `README.md` update).
- Reads of DAD paths collapse into the generic `shell` category on
  Codex/Windows and are not distinguishable from arbitrary PowerShell.
- No dedicated DAD-asset band exists in `RelayApprovalPolicy`.
