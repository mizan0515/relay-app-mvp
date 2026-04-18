using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Protocol;

/// <summary>
/// Symmetric turn-packet I/O facade for G1 (peer-symmetric packet I/O).
///
/// Write delegates to <see cref="TurnPacketYamlPersister"/> (the canonical emitter).
/// Read parses the deterministic subset produced by that emitter — round-trip
/// tests in <c>CodexClaudeRelay.Core.Tests/Protocol/PacketIOTests.cs</c> prove
/// symmetry for both <c>from: codex</c> and <c>from: claude-code</c>.
///
/// Zero NuGet deps on purpose: the emitter format is tight enough to parse by
/// line walk, so we don't need YamlDotNet. If the schema grows beyond this
/// rigid shape (anchors, flow style, multi-line scalars), add YamlDotNet here.
/// </summary>
public static class PacketIO
{
    public static Task<long> WriteAsync(TurnPacket packet, string path, CancellationToken ct = default)
        => TurnPacketYamlPersister.WriteAsync(packet, path, ct);

    public static async Task<TurnPacket> ReadAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return Parse(text);
    }

    public static TurnPacket Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        string type = "turn";
        string from = AgentRole.Codex;
        int turn = 0;
        string sessionId = string.Empty;
        var handoff = new TurnHandoff();
        var checkpoints = new List<CheckpointResult>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;

            if (TryTopScalar(line, "type", out var v)) { type = v; continue; }
            if (TryTopScalar(line, "from", out v)) { from = v; continue; }
            if (TryTopScalar(line, "turn", out v)) { turn = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture); continue; }
            if (TryTopScalar(line, "session_id", out v)) { sessionId = v; continue; }

            if (line == "handoff:")
            {
                handoff = ParseHandoff(lines, ref i);
                continue;
            }

            if (line == "peer_review:")
            {
                checkpoints = ParsePeerReview(lines, ref i);
                continue;
            }
        }

        return new TurnPacket
        {
            Type = type,
            From = from,
            Turn = turn,
            SessionId = sessionId,
            Handoff = handoff,
            PeerReview = new PeerReview { CheckpointResults = checkpoints },
        };
    }

    private static TurnHandoff ParseHandoff(string[] lines, ref int i)
    {
        string closeoutKind = string.Empty, nextTask = string.Empty, context = string.Empty,
               promptArtifact = string.Empty, doneReason = string.Empty;
        bool ready = false, suggestDone = false;
        var questions = new List<string>();

        while (i + 1 < lines.Length && lines[i + 1].StartsWith("  ") && !lines[i + 1].StartsWith("    "))
        {
            i++;
            var line = lines[i];
            var trimmed = line.Substring(2);

            if (TryScalar(trimmed, "closeout_kind", out var v)) { closeoutKind = v; continue; }
            if (TryScalar(trimmed, "next_task", out v)) { nextTask = v; continue; }
            if (TryScalar(trimmed, "context", out v)) { context = v; continue; }
            if (TryScalar(trimmed, "prompt_artifact", out v)) { promptArtifact = v; continue; }
            if (TryScalar(trimmed, "ready_for_peer_verification", out v)) { ready = v == "true"; continue; }
            if (TryScalar(trimmed, "suggest_done", out v)) { suggestDone = v == "true"; continue; }
            if (TryScalar(trimmed, "done_reason", out v)) { doneReason = v; continue; }
            if (trimmed == "questions: []") { questions = new List<string>(); continue; }
            if (trimmed == "questions:")
            {
                while (i + 1 < lines.Length && lines[i + 1].StartsWith("    - "))
                {
                    i++;
                    var itemLine = lines[i].Substring("    - ".Length);
                    questions.Add(Unquote(itemLine));
                }
                continue;
            }
        }

        return new TurnHandoff
        {
            CloseoutKind = closeoutKind,
            NextTask = nextTask,
            Context = context,
            PromptArtifact = promptArtifact,
            ReadyForPeerVerification = ready,
            SuggestDone = suggestDone,
            DoneReason = doneReason,
            Questions = questions,
        };
    }

    private static List<CheckpointResult> ParsePeerReview(string[] lines, ref int i)
    {
        var results = new List<CheckpointResult>();

        if (i + 1 < lines.Length && lines[i + 1].Trim() == "checkpoint_results: []")
        {
            i++;
            return results;
        }
        if (i + 1 >= lines.Length || lines[i + 1].Trim() != "checkpoint_results:")
            return results;
        i++; // consume `  checkpoint_results:`

        while (i + 1 < lines.Length && lines[i + 1].StartsWith("    - checkpoint_id:"))
        {
            i++;
            var idLine = lines[i].Substring("    - checkpoint_id: ".Length);
            string checkpointId = Unquote(idLine);
            string status = string.Empty, evidence = string.Empty;

            while (i + 1 < lines.Length && lines[i + 1].StartsWith("      "))
            {
                i++;
                var content = lines[i].Substring(6);
                if (TryScalar(content, "status", out var v)) status = v;
                else if (TryScalar(content, "evidence_ref", out v)) evidence = v;
            }

            results.Add(new CheckpointResult
            {
                CheckpointId = checkpointId,
                Status = status,
                EvidenceRef = evidence,
            });
        }

        return results;
    }

    private static bool TryTopScalar(string line, string key, out string value)
        => TryScalar(line, key, out value);

    private static bool TryScalar(string line, string key, out string value)
    {
        var prefix = key + ": ";
        if (line.StartsWith(prefix))
        {
            value = Unquote(line.Substring(prefix.Length));
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static string Unquote(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            var inner = raw.Substring(1, raw.Length - 2);
            return inner.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        return raw;
    }
}
