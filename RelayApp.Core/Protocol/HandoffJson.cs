using System.Text.Json;
using System.Text.Json.Serialization;
using RelayApp.Core.Models;

namespace RelayApp.Core.Protocol;

public static class HandoffJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    public static JsonSerializerOptions CreateSerializerOptions(bool writeIndented = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            WriteIndented = writeIndented,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static string BuildSchema(RelaySide source, RelaySide target, string sessionId, int turnNumber)
    {
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                ["type"] = new Dictionary<string, object?> { ["type"] = "string", ["const"] = "dad_handoff" },
                ["version"] = new Dictionary<string, object?> { ["type"] = "integer", ["const"] = 1 },
                ["source"] = new Dictionary<string, object?> { ["type"] = "string", ["const"] = source.ToString().ToLowerInvariant() },
                ["target"] = new Dictionary<string, object?> { ["type"] = "string", ["const"] = target.ToString().ToLowerInvariant() },
                ["session_id"] = new Dictionary<string, object?> { ["type"] = "string", ["const"] = sessionId },
                ["turn"] = new Dictionary<string, object?> { ["type"] = "integer", ["const"] = turnNumber },
                ["ready"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                ["prompt"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["summary"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?> { ["type"] = "string" },
                    ["minItems"] = 1,
                    ["maxItems"] = 10,
                },
                ["requires_human"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                ["reason"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["created_at"] = new Dictionary<string, object?> { ["type"] = "string" },
            },
            ["required"] = new[]
            {
                "type",
                "version",
                "source",
                "target",
                "session_id",
                "turn",
                "ready",
                "prompt",
                "summary",
                "requires_human",
                "reason",
                "created_at",
            },
            ["additionalProperties"] = false,
        };

        return JsonSerializer.Serialize(schema);
    }
}
