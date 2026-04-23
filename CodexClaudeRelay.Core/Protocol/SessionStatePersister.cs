using System.Text.Json;
using System.Text.Json.Serialization;
using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Protocol;

public sealed class SessionStateSnapshot
{
    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; init; } = "dad-v2";

    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("session_status")]
    public string SessionStatus { get; init; } = "active";

    [JsonPropertyName("superseded_by")]
    public string? SupersededBy { get; init; }

    [JsonPropertyName("closed_reason")]
    public string? ClosedReason { get; init; }

    [JsonPropertyName("relay_mode")]
    public string RelayMode { get; init; } = "user-bridged";

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "hybrid";

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = "medium";

    [JsonPropertyName("current_turn")]
    public int CurrentTurn { get; init; }

    [JsonPropertyName("max_turns")]
    public int MaxTurns { get; init; }

    [JsonPropertyName("last_agent")]
    public string? LastAgent { get; init; }

    [JsonPropertyName("origin_backlog_id")]
    public string OriginBacklogId { get; init; } = string.Empty;

    [JsonPropertyName("task_bucket")]
    public string TaskBucket { get; init; } = string.Empty;

    [JsonPropertyName("task_summary")]
    public string TaskSummary { get; init; } = string.Empty;

    [JsonPropertyName("contract_status")]
    public string ContractStatus { get; init; } = "accepted";

    [JsonPropertyName("packets")]
    public IReadOnlyList<string> Packets { get; init; } = Array.Empty<string>();

    [JsonPropertyName("decisions")]
    public IReadOnlyList<string> Decisions { get; init; } = Array.Empty<string>();

    [JsonPropertyName("meta_improvements")]
    public IReadOnlyList<string> MetaImprovements { get; init; } = Array.Empty<string>();
}

public static class SessionStatePersister
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static async Task<long> WriteAsync(SessionStateSnapshot state, string outPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(outPath);

        var body = Render(state);

        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = outPath + ".tmp";
        await File.WriteAllTextAsync(tmp, body, ct).ConfigureAwait(false);
        File.Move(tmp, outPath, overwrite: true);

        return new FileInfo(outPath).Length;
    }

    public static string Render(SessionStateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, SerializerOptions) + '\n';
    }
}
