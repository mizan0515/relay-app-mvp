using System.Text.Json;
using System.Text.Json.Serialization;
using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Protocol;

public sealed class SessionLearningRecord
{
    [JsonPropertyName("recorded_at")]
    public DateTimeOffset RecordedAt { get; init; }

    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("session_status")]
    public string SessionStatus { get; init; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = string.Empty;

    [JsonPropertyName("origin_backlog_id")]
    public string OriginBacklogId { get; init; } = string.Empty;

    [JsonPropertyName("task_bucket")]
    public string TaskBucket { get; init; } = string.Empty;

    [JsonPropertyName("task_summary")]
    public string TaskSummary { get; init; } = string.Empty;

    [JsonPropertyName("contract_status")]
    public string ContractStatus { get; init; } = string.Empty;

    [JsonPropertyName("current_turn")]
    public int CurrentTurn { get; init; }

    [JsonPropertyName("total_input_tokens")]
    public long TotalInputTokens { get; init; }

    [JsonPropertyName("total_output_tokens")]
    public long TotalOutputTokens { get; init; }

    [JsonPropertyName("total_cache_read_input_tokens")]
    public long TotalCacheReadInputTokens { get; init; }

    [JsonPropertyName("total_cache_creation_input_tokens")]
    public long TotalCacheCreationInputTokens { get; init; }

    [JsonPropertyName("total_cost_claude_usd")]
    public double TotalCostClaudeUsd { get; init; }

    [JsonPropertyName("total_cost_codex_usd")]
    public double TotalCostCodexUsd { get; init; }

    [JsonPropertyName("last_agent")]
    public string LastAgent { get; init; } = string.Empty;

    [JsonPropertyName("closeout_kind")]
    public string CloseoutKind { get; init; } = string.Empty;

    [JsonPropertyName("suggest_done")]
    public bool SuggestDone { get; init; }

    [JsonPropertyName("done_reason")]
    public string DoneReason { get; init; } = string.Empty;

    [JsonPropertyName("decision_count")]
    public int DecisionCount { get; init; }

    [JsonPropertyName("constraint_count")]
    public int ConstraintCount { get; init; }

    [JsonPropertyName("decisions")]
    public IReadOnlyList<string> Decisions { get; init; } = Array.Empty<string>();

    [JsonPropertyName("constraints")]
    public IReadOnlyList<string> Constraints { get; init; } = Array.Empty<string>();

    [JsonPropertyName("checkpoint_summary")]
    public IReadOnlyList<string> CheckpointSummary { get; init; } = Array.Empty<string>();
}

public static class SessionLearningRecordWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ResolvePath() =>
        Path.Combine("Document", "dialogue", "learning-memory", "session-outcomes.jsonl");

    public static SessionLearningRecord Build(
        RelaySessionState state,
        HandoffEnvelope? handoff,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(state);

        var checkpoints = handoff?.CheckpointResults
            .Where(static item => !string.IsNullOrWhiteSpace(item.CheckpointId))
            .Select(static item =>
                $"{item.CheckpointId}:{item.Status}{(string.IsNullOrWhiteSpace(item.EvidenceRef) ? string.Empty : $"@{item.EvidenceRef}")}")
            .ToArray() ?? Array.Empty<string>();

        return new SessionLearningRecord
        {
            RecordedAt = recordedAt,
            SessionId = state.SessionId,
            SessionStatus = state.Status.ToString().ToLowerInvariant(),
            Mode = state.Mode,
            Scope = state.Scope,
            OriginBacklogId = state.OriginBacklogId,
            TaskBucket = state.TaskBucket,
            TaskSummary = state.TaskSummary,
            ContractStatus = state.ContractStatus,
            CurrentTurn = state.CurrentTurn,
            TotalInputTokens = state.TotalInputTokens,
            TotalOutputTokens = state.TotalOutputTokens,
            TotalCacheReadInputTokens = state.TotalCacheReadInputTokens,
            TotalCacheCreationInputTokens = state.TotalCacheCreationInputTokens,
            TotalCostClaudeUsd = state.TotalCostClaudeUsd,
            TotalCostCodexUsd = state.TotalCostCodexUsd,
            LastAgent = state.ActiveAgent,
            CloseoutKind = handoff?.CloseoutKind ?? string.Empty,
            SuggestDone = handoff?.SuggestDone ?? false,
            DoneReason = handoff?.DoneReason ?? string.Empty,
            DecisionCount = state.Decisions.Count,
            ConstraintCount = state.Constraints.Count,
            Decisions = state.Decisions.ToArray(),
            Constraints = state.Constraints.ToArray(),
            CheckpointSummary = checkpoints,
        };
    }

    public static string Render(SessionLearningRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(record, SerializerOptions);
    }

    public static async Task<long> AppendAsync(
        SessionLearningRecord record,
        string outPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(outPath);

        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var line = Render(record) + Environment.NewLine;
        await File.AppendAllTextAsync(outPath, line, ct).ConfigureAwait(false);
        return new FileInfo(outPath).Length;
    }
}
