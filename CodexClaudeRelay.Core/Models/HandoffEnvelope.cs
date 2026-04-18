using System.Text.Json.Serialization;

namespace CodexClaudeRelay.Core.Models;

public sealed record HandoffEnvelope
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "dad_handoff";

    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("source")]
    public string Source { get; init; } = AgentRole.Codex;

    [JsonPropertyName("target")]
    public string Target { get; init; } = AgentRole.Claude;

    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("turn")]
    public int Turn { get; init; }

    [JsonPropertyName("ready")]
    public bool Ready { get; init; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("summary")]
    public IReadOnlyList<string> Summary { get; init; } = [];

    [JsonPropertyName("completed")]
    public IReadOnlyList<string> Completed { get; init; } = [];

    [JsonPropertyName("constraints")]
    public IReadOnlyList<string> Constraints { get; init; } = [];

    [JsonPropertyName("requires_human")]
    public bool RequiresHuman { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("checkpoint_results")]
    public IReadOnlyList<CheckpointResult> CheckpointResults { get; init; } = [];
}
