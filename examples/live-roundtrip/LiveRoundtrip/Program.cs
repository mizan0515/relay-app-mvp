using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Protocol;

// Live round-trip demo: builds two TurnPackets, writes them via PacketIO,
// reads them back, and asserts byte-level equality of the second pass.
// Proves the relay's canonical YAML emitter is stable across write → read → write.

var outDir = Path.GetFullPath(args.Length > 0
    ? args[0]
    : Path.Combine(Environment.CurrentDirectory, "session-out"));
Directory.CreateDirectory(outDir);

var sessionId = $"live-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
var turn1 = new TurnPacket
{
    From = AgentRole.Codex,
    Turn = 1,
    SessionId = sessionId,
    Handoff = new TurnHandoff
    {
        CloseoutKind = CloseoutKind.PeerHandoff,
        NextTask = "Claude Code, verify cp-1 (packet round-trip) on turn-2.",
        Context = $"Live demo session {sessionId}. Codex authored turn-1 via PacketIO directly.",
        PromptArtifact = $"Document/dialogue/sessions/{sessionId}/turn-1-handoff.md",
        ReadyForPeerVerification = true,
    },
};

var turn2 = new TurnPacket
{
    From = AgentRole.Claude,
    Turn = 2,
    SessionId = sessionId,
    PeerReview = new PeerReview
    {
        CheckpointResults = new[]
        {
            new CheckpointResult
            {
                CheckpointId = "cp-1",
                Status = CheckpointStatus.Pass,
                EvidenceRef = "PacketIO round-trip byte-equal on second emit.",
            },
        },
    },
    Handoff = new TurnHandoff
    {
        CloseoutKind = CloseoutKind.FinalNoHandoff,
        SuggestDone = true,
        DoneReason = "Live demo converged on cp-1 PASS; no follow-up work.",
    },
};

var path1 = Path.Combine(outDir, "turn-1.yaml");
var path2 = Path.Combine(outDir, "turn-2.yaml");

await PacketIO.WriteAsync(turn1, path1);
await PacketIO.WriteAsync(turn2, path2);

var read1 = await PacketIO.ReadAsync(path1);
var read2 = await PacketIO.ReadAsync(path2);

var tmp1 = path1 + ".verify";
var tmp2 = path2 + ".verify";
await PacketIO.WriteAsync(read1, tmp1);
await PacketIO.WriteAsync(read2, tmp2);

var bytes1a = await File.ReadAllBytesAsync(path1);
var bytes1b = await File.ReadAllBytesAsync(tmp1);
var bytes2a = await File.ReadAllBytesAsync(path2);
var bytes2b = await File.ReadAllBytesAsync(tmp2);

File.Delete(tmp1);
File.Delete(tmp2);

var match1 = bytes1a.SequenceEqual(bytes1b);
var match2 = bytes2a.SequenceEqual(bytes2b);

Console.WriteLine($"session: {sessionId}");
Console.WriteLine($"out:     {outDir}");
Console.WriteLine($"turn-1:  {(match1 ? "round-trip byte-equal" : "DRIFT")} ({bytes1a.Length} B)");
Console.WriteLine($"turn-2:  {(match2 ? "round-trip byte-equal" : "DRIFT")} ({bytes2a.Length} B)");

return (match1 && match2) ? 0 : 1;
