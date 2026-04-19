using System.Text.Json;
using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Broker;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Persistence;
using CodexClaudeRelay.Core.Protocol;

// Deep integration demo: drives the RelayBroker end-to-end through a
// scripted 4-turn "mini project" to demonstrate the relay's actual broker,
// packet persistence, handoff envelope routing, and state.json lifecycle
// working together. Agents are SCRIPTED (not real CLIs) so the experiment
// runs autonomously — but the relay's own machinery is real.

var workDir = Path.GetFullPath(args.Length > 0
    ? args[0]
    : Path.Combine(Environment.CurrentDirectory, "session-workspace"));
if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
Directory.CreateDirectory(workDir);
Environment.CurrentDirectory = workDir;

var sessionId = $"deep-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
var sessionDir = Path.Combine(workDir, "Document", "dialogue", "sessions", sessionId);
var logDir = Path.Combine(workDir, "Document", "dialogue", "logs");
var stateFile = Path.Combine(workDir, "Document", "dialogue", "state.json");

var codex = new ScriptedAdapter(AgentRole.Codex);
var claude = new ScriptedAdapter(AgentRole.Claude);
// Use in-memory root-state store (avoids filesystem race on repeated Save on Windows).
// Session-level state.json under sessions/<id>/state.json is still written by the broker.
var store = new InMemoryStore();
var log = new JsonlEventLogWriter(logDir);
var broker = new RelayBroker(new IRelayAdapter[] { codex, claude }, store, log);

// Mini project: "Add a greeting feature with a test". 4 turns total.
// turn 1: codex proposes plan + files to create
// turn 2: claude reviews + adds test scaffold
// turn 3: codex implements greeting, requests final check
// turn 4: claude seals session with final_no_handoff
Console.WriteLine($"=== Starting session {sessionId} ===");
Console.WriteLine($"workspace: {workDir}");
Console.WriteLine($"project:   mini greeting feature (4 turns, scripted agents)");
Console.WriteLine();

await broker.StartSessionAsync(sessionId, AgentRole.Codex,
    "Add a greeting feature to the project: one source file `greeting.py` and one test `test_greeting.py`. Codex, propose plan and structure.",
    CancellationToken.None);

// Turn 1: Codex responds, hands to Claude.
codex.HandoffJson = BuildHandoff(
    source: AgentRole.Codex, target: AgentRole.Claude,
    sessionId: sessionId, turn: 1,
    prompt: "Claude, please create test_greeting.py covering happy path and empty-name case. Return a peer_handoff when scaffold is in place.",
    summary: new[] { "Plan: greeting.py exposes greet(name) -> string; test covers happy + empty." },
    completed: new[] { "Authored plan; filenames fixed." },
    closeout: CloseoutKind.PeerHandoff);
await broker.AdvanceAsync(CancellationToken.None);
PrintTurn(1, AgentRole.Codex, sessionDir, broker);

// Turn 2: Claude adds test scaffold, hands back to Codex.
claude.HandoffJson = BuildHandoff(
    source: AgentRole.Claude, target: AgentRole.Codex,
    sessionId: sessionId, turn: 2,
    prompt: "Codex, implement greet() in greeting.py. Empty name should raise ValueError. Run the test after.",
    summary: new[] { "Test scaffold for greet() written; 2 cases covered." },
    completed: new[] { "test_greeting.py created with 2 assert cases." },
    closeout: CloseoutKind.PeerHandoff,
    checkpoints: new[] {
        new CheckpointResult {
            CheckpointId = "cp-test-scaffold", Status = CheckpointStatus.Pass,
            EvidenceRef = "test_greeting.py created",
        },
    });
await broker.AdvanceAsync(CancellationToken.None);
PrintTurn(2, AgentRole.Claude, sessionDir, broker);

// Turn 3: Codex implements, requests final verification.
codex.HandoffJson = BuildHandoff(
    source: AgentRole.Codex, target: AgentRole.Claude,
    sessionId: sessionId, turn: 3,
    prompt: "Claude, please verify cp-impl-pass by inspecting test output. If clean, seal with final_no_handoff.",
    summary: new[] { "greet() implemented; tests pass locally." },
    completed: new[] { "greeting.py implemented; test_greeting.py passes 2/2." },
    closeout: CloseoutKind.PeerHandoff,
    checkpoints: new[] {
        new CheckpointResult {
            CheckpointId = "cp-impl-pass", Status = CheckpointStatus.Pass,
            EvidenceRef = "pytest test_greeting.py -> 2 passed",
        },
    });
await broker.AdvanceAsync(CancellationToken.None);
PrintTurn(3, AgentRole.Codex, sessionDir, broker);

// Turn 4: Claude seals session (final_no_handoff).
claude.HandoffJson = BuildHandoff(
    source: AgentRole.Claude, target: AgentRole.Codex,
    sessionId: sessionId, turn: 4,
    prompt: string.Empty,
    summary: new[] { "Feature complete: greeting.py + test_greeting.py both land and pass." },
    completed: new[] { "cp-impl-pass verified; no further work remaining." },
    closeout: CloseoutKind.FinalNoHandoff,
    ready: false,
    suggestDone: true,
    doneReason: "Mini greeting project fully converged in 4 turns.",
    checkpoints: new[] {
        new CheckpointResult {
            CheckpointId = "cp-impl-pass", Status = CheckpointStatus.Pass,
            EvidenceRef = "Verified by inspecting test output relay-side",
        },
    });
await broker.AdvanceAsync(CancellationToken.None);
PrintTurn(4, AgentRole.Claude, sessionDir, broker);

Console.WriteLine();
Console.WriteLine("=== Session artifacts ===");
PrintDirTree(sessionDir, indent: "  ");
Console.WriteLine();
Console.WriteLine("=== Event log (last 10) ===");
var logPath = log.GetLogPath(sessionId);
if (File.Exists(logPath))
{
    var lines = await File.ReadAllLinesAsync(logPath);
    foreach (var line in lines.TakeLast(10)) Console.WriteLine("  " + line);
}

Console.WriteLine();
Console.WriteLine($"=== Verify manually with: ===");
Console.WriteLine($"  tools/Validate-Dad-Packet.ps1 -Path {Path.Combine(sessionDir, "turn-1.yaml")}");
Console.WriteLine();
return 0;

static string BuildHandoff(
    string source, string target, string sessionId, int turn, string prompt,
    string[]? summary = null, string[]? completed = null,
    string closeout = CloseoutKind.PeerHandoff, bool ready = true,
    bool suggestDone = false, string doneReason = "",
    CheckpointResult[]? checkpoints = null)
{
    var env = new HandoffEnvelope
    {
        Source = source,
        Target = target,
        SessionId = sessionId,
        Turn = turn,
        Ready = ready,
        Prompt = prompt,
        Summary = summary ?? Array.Empty<string>(),
        Completed = completed ?? Array.Empty<string>(),
        CloseoutKind = closeout,
        SuggestDone = suggestDone,
        DoneReason = doneReason,
        CheckpointResults = checkpoints ?? Array.Empty<CheckpointResult>(),
        Reason = $"{source} turn {turn}",
        CreatedAt = DateTimeOffset.UtcNow,
    };
    return JsonSerializer.Serialize(env, HandoffJson.SerializerOptions);
}

static void PrintTurn(int turn, string who, string sessionDir, RelayBroker broker)
{
    Console.WriteLine($"-- Turn {turn} ({who}) --");
    Console.WriteLine($"   active now: {broker.State.ActiveAgent}  current_turn: {broker.State.CurrentTurn}");
    var packet = Path.Combine(sessionDir, $"turn-{turn}.yaml");
    var handoff = Path.Combine(sessionDir, $"turn-{turn}-handoff.md");
    if (File.Exists(packet))   Console.WriteLine($"   wrote:  {Path.GetFileName(packet)} ({new FileInfo(packet).Length} B)");
    if (File.Exists(handoff))  Console.WriteLine($"   wrote:  {Path.GetFileName(handoff)} ({new FileInfo(handoff).Length} B)");
}

static void PrintDirTree(string root, string indent)
{
    if (!Directory.Exists(root)) { Console.WriteLine(indent + "(missing)"); return; }
    foreach (var f in Directory.EnumerateFiles(root).OrderBy(x => x))
        Console.WriteLine($"{indent}{Path.GetFileName(f)}  ({new FileInfo(f).Length} B)");
}

public sealed class InMemoryStore : IRelaySessionStore
{
    private RelaySessionState? _state;
    public Task SaveAsync(RelaySessionState state, CancellationToken ct) { _state = state; return Task.CompletedTask; }
    public Task<RelaySessionState?> LoadAsync(CancellationToken ct) => Task.FromResult(_state);
}

public sealed class ScriptedAdapter : IRelayAdapter
{
    public ScriptedAdapter(string role) { Role = role; }
    public string Role { get; }
    public string HandoffJson { get; set; } = string.Empty;

    public Task<AdapterStatus> GetStatusAsync(CancellationToken ct) =>
        Task.FromResult(new AdapterStatus(RelayHealthStatus.Healthy, true, "scripted-ok"));
    public Task<RelayAdapterResult> RunTurnAsync(RelayTurnContext ctx, CancellationToken ct) =>
        Task.FromResult(new RelayAdapterResult(HandoffJson));
    public Task<RelayAdapterResult> RunRepairAsync(RelayRepairContext ctx, CancellationToken ct) =>
        Task.FromResult(new RelayAdapterResult(HandoffJson));
}
