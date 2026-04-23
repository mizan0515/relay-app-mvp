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
        var contract = new TurnContract();
        var handoff = new TurnHandoff();
        var peerReview = new PeerReview();
        var myWork = new MyWork();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;

            if (TryTopScalar(line, "type", out var v)) { type = v; continue; }
            if (TryTopScalar(line, "from", out v)) { from = v; continue; }
            if (TryTopScalar(line, "turn", out v)) { turn = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture); continue; }
            if (TryTopScalar(line, "session_id", out v)) { sessionId = v; continue; }

            if (line == "contract:")
            {
                contract = ParseContract(lines, ref i);
                continue;
            }

            if (line == "handoff:")
            {
                handoff = ParseHandoff(lines, ref i);
                continue;
            }

            if (line == "peer_review:")
            {
                peerReview = ParsePeerReview(lines, ref i);
                continue;
            }

            if (line == "my_work:")
            {
                myWork = ParseMyWork(lines, ref i);
                continue;
            }
        }

        return new TurnPacket
        {
            Type = type,
            From = from,
            Turn = turn,
            SessionId = sessionId,
            Contract = contract,
            Handoff = handoff,
            PeerReview = peerReview,
            MyWork = myWork,
        };
    }

    private static TurnContract ParseContract(string[] lines, ref int i)
    {
        string status = "accepted";
        var checkpoints = new List<string>();
        var amendments = new List<string>();

        while (i + 1 < lines.Length && lines[i + 1].StartsWith("  ") && !lines[i + 1].StartsWith("    "))
        {
            i++;
            var trimmed = lines[i].Substring(2);
            if (TryScalar(trimmed, "status", out var v)) { status = v; continue; }
            if (trimmed == "checkpoints: []") { checkpoints.Clear(); continue; }
            if (trimmed == "checkpoints:")
            {
                checkpoints = ParseList(lines, ref i, "    - ");
                continue;
            }
            if (trimmed == "amendments: []") { amendments.Clear(); continue; }
            if (trimmed == "amendments:")
            {
                amendments = ParseList(lines, ref i, "    - ");
                continue;
            }
        }

        return new TurnContract
        {
            Status = status,
            Checkpoints = checkpoints,
            Amendments = amendments,
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

    private static PeerReview ParsePeerReview(string[] lines, ref int i)
    {
        string projectAnalysis = string.Empty;
        var taskModelReview = new TaskModelReview();
        var results = new List<CheckpointResult>();
        var issuesFound = new List<string>();
        var fixesApplied = new List<string>();

        while (i + 1 < lines.Length && lines[i + 1].StartsWith("  ") && !IsTopLevel(lines[i + 1]))
        {
            i++;
            var trimmed = lines[i].Substring(2);

            if (TryScalar(trimmed, "project_analysis", out var scalar))
            {
                projectAnalysis = scalar;
                continue;
            }

            if (trimmed == "task_model_review:")
            {
                taskModelReview = ParseTaskModelReview(lines, ref i);
                continue;
            }

            if (trimmed == "checkpoint_results: []")
            {
                results.Clear();
                continue;
            }

            if (trimmed == "checkpoint_results:")
            {
                results = ParseCheckpointResults(lines, ref i);
                continue;
            }

            if (trimmed == "issues_found: []")
            {
                issuesFound.Clear();
                continue;
            }

            if (trimmed == "issues_found:")
            {
                issuesFound = ParseList(lines, ref i, "    - ");
                continue;
            }

            if (trimmed == "fixes_applied: []")
            {
                fixesApplied.Clear();
                continue;
            }

            if (trimmed == "fixes_applied:")
            {
                fixesApplied = ParseList(lines, ref i, "    - ");
            }
        }

        return new PeerReview
        {
            ProjectAnalysis = projectAnalysis,
            TaskModelReview = taskModelReview,
            CheckpointResults = results,
            IssuesFound = issuesFound,
            FixesApplied = fixesApplied,
        };
    }

    private static List<CheckpointResult> ParseCheckpointResults(string[] lines, ref int i)
    {
        var results = new List<CheckpointResult>();
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

    private static TaskModelReview ParseTaskModelReview(string[] lines, ref int i)
    {
        string status = "aligned";
        var coverageGaps = new List<string>();
        var scopeCreep = new List<string>();
        var riskFollowups = new List<string>();
        var amendments = new List<string>();

        while (i + 1 < lines.Length && lines[i + 1].StartsWith("    "))
        {
            i++;
            var trimmed = lines[i].Substring(4);
            if (TryScalar(trimmed, "status", out var v)) { status = v; continue; }
            if (trimmed == "coverage_gaps: []") { coverageGaps.Clear(); continue; }
            if (trimmed == "coverage_gaps:") { coverageGaps = ParseList(lines, ref i, "      - "); continue; }
            if (trimmed == "scope_creep: []") { scopeCreep.Clear(); continue; }
            if (trimmed == "scope_creep:") { scopeCreep = ParseList(lines, ref i, "      - "); continue; }
            if (trimmed == "risk_followups: []") { riskFollowups.Clear(); continue; }
            if (trimmed == "risk_followups:") { riskFollowups = ParseList(lines, ref i, "      - "); continue; }
            if (trimmed == "amendments: []") { amendments.Clear(); continue; }
            if (trimmed == "amendments:") { amendments = ParseList(lines, ref i, "      - "); continue; }
        }

        return new TaskModelReview
        {
            Status = status,
            CoverageGaps = coverageGaps,
            ScopeCreep = scopeCreep,
            RiskFollowups = riskFollowups,
            Amendments = amendments,
        };
    }

    private static MyWork ParseMyWork(string[] lines, ref int i)
    {
        string plan = string.Empty;
        string verification = string.Empty;
        string confidence = "medium";
        int selfIterations = 0;
        var changes = new WorkChanges();
        var evidence = new WorkEvidence();
        var openRisks = new List<string>();

        while (i + 1 < lines.Length && lines[i + 1].StartsWith("  ") && !IsTopLevel(lines[i + 1]))
        {
            i++;
            var trimmed = lines[i].Substring(2);
            if (TryScalar(trimmed, "plan", out var v)) { plan = v; continue; }
            if (TryScalar(trimmed, "self_iterations", out v))
            {
                selfIterations = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }
            if (TryScalar(trimmed, "verification", out v)) { verification = v; continue; }
            if (TryScalar(trimmed, "confidence", out v)) { confidence = v; continue; }
            if (trimmed == "changes:")
            {
                changes = ParseWorkChanges(lines, ref i);
                continue;
            }
            if (trimmed == "evidence:")
            {
                evidence = ParseWorkEvidence(lines, ref i);
                continue;
            }
            if (trimmed == "open_risks: []")
            {
                openRisks.Clear();
                continue;
            }
            if (trimmed == "open_risks:")
            {
                openRisks = ParseList(lines, ref i, "    - ");
            }
        }

        return new MyWork
        {
            Plan = plan,
            Changes = changes,
            SelfIterations = selfIterations,
            Evidence = evidence,
            Verification = verification,
            OpenRisks = openRisks,
            Confidence = confidence,
        };
    }

    private static WorkChanges ParseWorkChanges(string[] lines, ref int i)
    {
        var filesModified = new List<string>();
        var filesCreated = new List<string>();
        string summary = string.Empty;

        while (i + 1 < lines.Length && lines[i + 1].StartsWith("    "))
        {
            i++;
            var trimmed = lines[i].Substring(4);
            if (trimmed == "files_modified: []") { filesModified.Clear(); continue; }
            if (trimmed == "files_modified:") { filesModified = ParseList(lines, ref i, "      - "); continue; }
            if (trimmed == "files_created: []") { filesCreated.Clear(); continue; }
            if (trimmed == "files_created:") { filesCreated = ParseList(lines, ref i, "      - "); continue; }
            if (TryScalar(trimmed, "summary", out var v)) { summary = v; continue; }
        }

        return new WorkChanges
        {
            FilesModified = filesModified,
            FilesCreated = filesCreated,
            Summary = summary,
        };
    }

    private static WorkEvidence ParseWorkEvidence(string[] lines, ref int i)
    {
        var commands = new List<string>();
        var artifacts = new List<string>();

        while (i + 1 < lines.Length && lines[i + 1].StartsWith("    "))
        {
            i++;
            var trimmed = lines[i].Substring(4);
            if (trimmed == "commands: []") { commands.Clear(); continue; }
            if (trimmed == "commands:") { commands = ParseList(lines, ref i, "      - "); continue; }
            if (trimmed == "artifacts: []") { artifacts.Clear(); continue; }
            if (trimmed == "artifacts:") { artifacts = ParseList(lines, ref i, "      - "); continue; }
        }

        return new WorkEvidence
        {
            Commands = commands,
            Artifacts = artifacts,
        };
    }

    private static List<string> ParseList(string[] lines, ref int i, string itemPrefix)
    {
        var values = new List<string>();
        while (i + 1 < lines.Length && lines[i + 1].StartsWith(itemPrefix, StringComparison.Ordinal))
        {
            i++;
            values.Add(Unquote(lines[i].Substring(itemPrefix.Length)));
        }

        return values;
    }

    private static bool IsTopLevel(string line) =>
        !line.StartsWith(" ", StringComparison.Ordinal);

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
