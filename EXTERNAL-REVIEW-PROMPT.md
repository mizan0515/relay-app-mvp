# External Review Prompt

Use the following prompt with another AI reviewer:

```text
You are a senior product and architecture reviewer auditing a Windows-first dual-agent relay prototype.

Important repository context:
- The repository root is a template source repo, not a live runtime repo.
- Restrict your review scope to:
  D:\dad-v2-system-template\prototypes\relay-app-mvp
- Do not expand into `en/` / `ko/` parity work.
- This is a review task, not a code-change task.

Primary question:
Can this relay prototype realistically evolve into a system where Claude Code CLI and Codex CLI do real machine work under broker control, including shell/PowerShell commands, file edits, git commit/push, PR creation, MCP tool use, and deep tool chains, while preserving explicit user approval and action-level auditability?

The current proposal says:
- keep `claude` CLI and `codex` CLI as the real execution engines
- let CLI-native capabilities pass through when possible
- make the broker own permissions, approvals, git workflow, action logs, continuity, and recovery
- treat Desktop-host-only magic as non-portable unless proven otherwise

You must:
1. read the local prototype docs and code first
2. then search the web for official documentation and real-world cases
3. then critique the plan, not just summarize it

Mandatory local files to inspect:
- D:\dad-v2-system-template\prototypes\relay-app-mvp\README.md
- D:\dad-v2-system-template\prototypes\relay-app-mvp\INTERACTIVE-REBUILD-PLAN.md
- D:\dad-v2-system-template\prototypes\relay-app-mvp\IMPROVEMENT-PLAN.md
- D:\dad-v2-system-template\prototypes\relay-app-mvp\RelayApp.Core\Broker\RelayBroker.cs
- D:\dad-v2-system-template\prototypes\relay-app-mvp\RelayApp.Core\Models\RelaySessionState.cs
- D:\dad-v2-system-template\prototypes\relay-app-mvp\RelayApp.Core\Protocol\RelayPromptBuilder.cs
- D:\dad-v2-system-template\prototypes\relay-app-mvp\RelayApp.Desktop\Adapters\ProcessCommandRunner.cs
- D:\dad-v2-system-template\prototypes\relay-app-mvp\RelayApp.Desktop\Adapters\ClaudeCliAdapter.cs
- D:\dad-v2-system-template\prototypes\relay-app-mvp\RelayApp.Desktop\Adapters\CodexCliAdapter.cs
- D:\dad-v2-system-template\prototypes\relay-app-mvp\RelayApp.Desktop\Interactive\ClaudeInteractiveAdapter.cs
- D:\dad-v2-system-template\prototypes\relay-app-mvp\RelayApp.Desktop\Interactive\CodexInteractiveAdapter.cs
- D:\dad-v2-system-template\prototypes\relay-app-mvp\RelayApp.CodexProtocol\CodexProtocolConnection.cs
- D:\dad-v2-system-template\prototypes\relay-app-mvp\RelayApp.CodexProtocol\CodexProtocolTurnRunner.cs

You must also research:
1. Claude Code permissions / approvals / MCP / git commit / push / PR workflow
2. OpenAI Codex CLI / codex app-server approval policy / sandbox / tool use / MCP behavior
3. Cursor agent security and terminal approval model
4. Aider auto-commit / no-auto-push workflow
5. Continue / Cline / Roo / Augment examples involving permissions, tool use, git workflow, MCP, and long-task continuity
6. Real-world failures:
   - permission bypass
   - approval drift
   - tool runaway
   - git push / PR permission errors
   - MCP-related security or trust failures
   - continuity failures caused by hidden compact/summarization

Questions you must answer:

1. Is the plan realistic?
   - If yes, under what conditions?
   - If no, what assumption breaks it?

2. Is the proposed priority order correct?
   - safety floor
   - action observability
   - capability audit
   - git workflow layer
   - deep tool-chain runtime
   - broker-owned continuity
   - skill/MCP/agent strategy refinement

3. Which capabilities are:
   - directly pass-through compatible
   - conditionally compatible
   - broker reimplementation required
   For each, include concrete reasons.

4. Is the approval problem really the first blocker?
   - Evaluate Codex approval handling
   - Evaluate Claude `-p` / streaming path
   - Explain whether the current product can safely permit machine actions before approval routing exists

5. Is action-level observability truly required before continuity work?
   - Explain whether a relay can responsibly allow shell/git/MCP actions if only handoff JSON is logged

6. Evaluate MCP specifically:
   - Can it realistically work in relay mode for Claude and Codex?
   - What configuration, environment, and audit constraints are required?
   - What blind spots remain if MCP is only passed through?

7. Evaluate git and PR handling:
   - Is a dedicated git workflow layer necessary?
   - Should commit be auto-allowed while push and PR creation require approval?
   - Are there better real-world patterns?

8. Evaluate deep tool-chain runtime:
   - Does the current bounded handoff contract prevent useful multi-step work?
   - What is the smallest safe expansion that still preserves relay discipline?

9. Evaluate continuity:
   - Is broker-owned rolling summary truly necessary?
   - Could vendor auto-compact and internal reasoning be enough?
   - Be explicit about what broker-owned continuity adds that hidden compact cannot guarantee

10. Evaluate the DAD ecosystem portability question:
   - If this MVP becomes the main system, will Claude and Codex inside it still be able to use meaningful DAD-era capabilities?
   - Ask directly whether the following are realistically usable in relay:
     - MCP
     - tool use
     - file-readable skills/instructions
     - host-registered skills
     - agent creation / sub-agent orchestration
   - Explain what survives, what partially survives, and what must be rebuilt.

11. Address this direct challenge:
   “Claude Code and Codex Desktop already use their own auto-compact and internal reasoning. If we want similar performance, why not just rely on that instead of building broker-managed continuity?”
   Answer it critically with evidence and examples.

Output format:

1. One-paragraph verdict
2. Strengths of the plan
3. Missing or weak areas
4. Overbuilt or premature areas
5. Capability table:
   - pass-through
   - conditional
   - broker-owned
6. Recommended priority order
7. Top 5 immediate next actions
8. Wrong assumptions that must not survive into implementation
9. Source list with Markdown links

Style requirements:
- Respond in Korean
- Be concise but not shallow
- Distinguish clearly between:
  - official guarantees
  - observed product behavior
  - community-reported failure cases
- Do not just restate the plan; critique it
```
