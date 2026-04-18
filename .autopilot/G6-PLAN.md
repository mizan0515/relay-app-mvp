# G6 PLAN — Rolling summary + carry-forward injection

Gate definition (MVP-GATES.md):
> At session rotation (turn/time/token trigger), broker writes a markdown
> summary and injects Goal/Completed/Pending/Constraints into the next
> session's first turn prompt under a `## Carry-forward` section.
> `summary.generated` event carries bytes + path.
>
> Evidence: pre-/post-rotation prompt diff showing carry-forward block +
> matching `summary.generated` log line + file on disk.

## What already exists (verified 2026-04-18 iter 35)

Substantial infrastructure already landed from prior (pre-reset) work and
not yet re-evidenced after the DAD-v2 realignment:

- `CarryForwardRenderer.TryBuild` (CodexClaudeRelay.Core/Protocol/) — pure
  fn composes `## Carry-forward` markdown block from Goal + Completed +
  Pending + Constraints + LastHandoffHash.
- `RollingSummaryWriter` (CodexClaudeRelay.Core/Runtime/) — writes
  `{sessionId}-segment-{N}.md` under
  `%LOCALAPPDATA%/CodexClaudeRelayMvp/summaries/` with cumulative totals,
  last handoff, pending prompt. `BuildGeneratedEventPayload` formats the
  JSON payload for the `summary.generated` event.
- `RotationSmokeRunner` (CodexClaudeRelay.Core/Runtime/) — headless driver
  that seeds synthetic state and exercises the writer + rotation
  evaluator.
- `RotationEvaluator.Evaluate` — extracted rotation-reason logic (turn
  count, token thresholds, etc.).
- `RelayBroker`:
  - `RotateSessionAsync` (~line 1481) — invokes summary write, sets
    `CarryForwardPending=true`.
  - `AdvanceAsyncInternal` (~line 417) — calls `TryBuildCarryForwardBlock`,
    emits `summary.loaded` event with bytes + fields_populated count.
  - `WriteRollingSummaryAsync` (~line 1820) — emits `summary.generated`
    event with path + bytes payload, `summary.failed` on IO error.

## What is missing for G6 `[~]` → `[x]`

1. **xunit evidence facts** — none of the G6 infrastructure has test
   coverage in `CodexClaudeRelay.Core.Tests/`. Need:
   - `CarryForwardRendererTests` — TryBuild composes expected block when
     pending is set + state has fields; returns null when empty.
   - `RollingSummaryWriterTests` — BuildMarkdown includes all required
     sections; WriteAsync atomically lands file with expected bytes.
   - `BuildGeneratedEventPayloadTests` — JSON shape matches broker's
     expectation (path escaped, bytes/segment/turns/costs).
2. **End-to-end rotation smoke** — headless exercise that:
   - seeds a session with pending + completed + goal,
   - triggers rotation (bump `TurnsSinceLastRotation` past threshold),
   - asserts `summary.generated` event fires with correct payload,
   - asserts carry-forward block appears in the subsequent `RelayTurnContext.CarryForward`
     (or `summary.loaded` event field count).
3. **MVP-GATES.md evidence block** — once above facts green, flip
   `[ ]` → `[~]` with commit shas; flip `[~]` → `[x]` once e2e smoke
   passes.

## iter execution order — STATUS

- **iter 35 DONE**: G6-PLAN.md 작성.
- **iter 36 DONE**: `CarryForwardRendererTests` 4 + `RollingSummaryWriterTests` 4
  (PR #40, f6f2263). 45/45.
- **iter 37 DONE**: G6 `[ ]` → `[~]` (this iter). 증거 스택 MVP-GATES.md
  기록.

## Follow-up for G6 `[~]` → `[x]`

End-to-end rotation smoke remains. Harness shape:

1. Build `RelayBroker` with fake `IRelayAdapter` pair whose `RunTurnAsync`
   returns canned handoffs that carry enough Completed/Pending/Constraints
   to populate carry-forward state.
2. In-memory session store + event log writer capturing all emitted
   events into an inspectable list.
3. Advance enough turns to trip `MaxTurnsPerSession` threshold (or force
   via internal test hook if available), causing `RotateSessionAsync`.
4. Assertions:
   - summary markdown file landed at expected path,
   - `summary.generated` event present with matching bytes/path payload,
   - next `RelayTurnContext.CarryForward` begins with `## Carry-forward`.

Scope ≈120 LOC. Bundle-candidate with G4/G5 `[x]` follow-ups — same
fake-adapter harness. Defer until G7/G8 sequencing decided or until
harness is otherwise justified.

## --- historical plan (obsolete, preserved for audit) ---

## iter execution order (target 3-4 iters)

- **iter 36**: `CarryForwardRendererTests` + `RollingSummaryWriterTests`
  (pure, no broker). ~3-5 facts. ≤80 LOC. Auto-merge path.
- **iter 37**: G6 `[ ]` → `[~]` flip in MVP-GATES.md with evidence
  stack citing iter36 PR. Documents remaining gap for `[x]`.
- **iter 38**: End-to-end rotation smoke via `RotationSmokeRunner` +
  xunit asserting rotation produces both the file and the event payload.
  `[~]` → `[x]` flip. ≤120 LOC.

Alternative sequencing: if a fake-adapter harness is built for G4/G5 `[x]`
follow-up, rotation can piggyback on it (advance through enough turns to
trip rotation threshold), combining three `[x]` flips into one large PR.

## Peer-symmetry check

Rotation triggers + summary content must be role-agnostic. `ActiveAgentAtRotation`
may be codex or claude; both must produce identical summary structure. No
branch presuming rotation only happens mid-codex-turn or mid-claude-turn
(IMMUTABLE mission rule 3).

## Risk flags

- **Summary path writes outside repo**: writer uses `%LOCALAPPDATA%/CodexClaudeRelayMvp/summaries/`,
  not `Document/dialogue/sessions/`. Gate evidence format says "file on
  disk" — confirm the LOCALAPPDATA location satisfies evidence intent, or
  consider adding a repo-relative option for deterministic test
  artifact paths.
- **Pre-reset baggage**: some G6 code may carry Codex-only assumptions
  from the pre-reset broker phase (e.g., asymmetric cost fields baked
  into summary). Audit `RollingSummaryFields` usage in iter36 to catch
  any role imbalance.
- **Carry-forward regression risk**: modifying `TryBuildCarryForwardBlock`
  could ripple into the active broker path. Prefer additive tests only
  in iter36; no behavior change.
