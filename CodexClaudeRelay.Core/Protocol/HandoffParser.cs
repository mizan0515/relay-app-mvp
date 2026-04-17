using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Protocol;

public static partial class HandoffParser
{
    public static bool TryParseWithFallback(
        string text,
        string originalPrompt,
        RelaySide expectedSource,
        RelaySide expectedTarget,
        string expectedSessionId,
        int expectedTurn,
        out HandoffEnvelope? handoff,
        out string? error,
        out string? normalizationNote)
    {
        normalizationNote = null;
        if (TryParse(text, out handoff, out error))
        {
            return true;
        }

        var candidate = ExtractJsonCandidate(text);
        if (candidate is null)
        {
            return TryNormalizeFromPlainText(
                text,
                originalPrompt,
                expectedSource,
                expectedTarget,
                expectedSessionId,
                expectedTurn,
                out handoff,
                out error,
                out normalizationNote);
        }

        if (!TryNormalize(
                candidate,
                expectedSource,
                expectedTarget,
                expectedSessionId,
                expectedTurn,
                out handoff,
                out error,
                out normalizationNote))
        {
            return false;
        }

        return true;
    }

    private static bool TryNormalizeFromPlainText(
        string text,
        string originalPrompt,
        RelaySide expectedSource,
        RelaySide expectedTarget,
        string expectedSessionId,
        int expectedTurn,
        out HandoffEnvelope? handoff,
        out string? error,
        out string? normalizationNote)
    {
        handoff = null;
        error = "No bounded marker block, raw JSON object, or fenced JSON handoff block was found.";
        normalizationNote = null;

        if (!LooksLikeSmokeTestPrompt(originalPrompt))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var lower = trimmed.ToLowerInvariant();
        var looksRelayish =
            lower.Contains("relay") ||
            lower.Contains("transport") ||
            lower.Contains("compliance") ||
            lower.Contains("payload") ||
            lower.Contains("handoff");

        if (!looksRelayish)
        {
            return false;
        }

        handoff = new HandoffEnvelope
        {
            Type = "dad_handoff",
            Version = 1,
            Source = expectedSource,
            Target = expectedTarget,
            SessionId = expectedSessionId,
            Turn = expectedTurn,
            Ready = true,
            Prompt = BuildFallbackPrompt(expectedSource, expectedTarget),
            Summary =
            [
                ShortenSentence(trimmed)
            ],
            RequiresHuman = false,
            Reason = string.Empty,
            CreatedAt = DateTimeOffset.Now,
        };

        normalizationNote = "Accepted a synthesized smoke-test handoff from prose output.";
        error = null;
        return true;
    }

    public static bool TryParse(string text, out HandoffEnvelope? handoff, out string? error)
    {
        handoff = null;
        error = null;

        var candidate = ExtractJsonCandidate(text);
        if (candidate is null)
        {
            error = "No bounded marker block, raw JSON object, or fenced JSON handoff block was found.";
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<HandoffEnvelope>(candidate, HandoffJson.SerializerOptions);
            if (parsed is null)
            {
                error = "Handoff JSON deserialized to null.";
                return false;
            }

            var validationError = Validate(parsed);
            if (validationError is not null)
            {
                error = validationError;
                return false;
            }

            handoff = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    public static string ComputeCanonicalHash(HandoffEnvelope handoff)
    {
        var canonicalFields = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["created_at"] = handoff.CreatedAt.ToString("O"),
            ["prompt"] = handoff.Prompt.Replace("\r\n", "\n").TrimEnd(),
            ["ready"] = handoff.Ready,
            ["reason"] = handoff.Reason.TrimEnd(),
            ["requires_human"] = handoff.RequiresHuman,
            ["session_id"] = handoff.SessionId.TrimEnd(),
            ["source"] = handoff.Source.ToString().ToLowerInvariant(),
            ["summary"] = handoff.Summary.Select(item => item.TrimEnd()).ToArray(),
            ["target"] = handoff.Target.ToString().ToLowerInvariant(),
            ["turn"] = handoff.Turn,
            ["type"] = handoff.Type.TrimEnd(),
            ["version"] = handoff.Version,
        };

        var canonical = JsonSerializer.Serialize(canonicalFields, HandoffJson.SerializerOptions);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private static string? ExtractJsonCandidate(string text)
    {
        var markerCandidate = ExtractMarkedJsonCandidate(text);
        if (markerCandidate is not null)
        {
            return markerCandidate;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var match = JsonFenceRegex().Match(text);
        if (match.Success)
        {
            return match.Groups["json"].Value.Trim();
        }

        return null;
    }

    private static string? ExtractMarkedJsonCandidate(string text)
    {
        var startIndex = text.IndexOf(RelayPromptBuilder.HandoffStartMarker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return null;
        }

        startIndex += RelayPromptBuilder.HandoffStartMarker.Length;
        var endIndex = text.IndexOf(RelayPromptBuilder.HandoffEndMarker, startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return null;
        }

        var candidate = text[startIndex..endIndex].Trim();
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static bool TryNormalize(
        string candidate,
        RelaySide expectedSource,
        RelaySide expectedTarget,
        string expectedSessionId,
        int expectedTurn,
        out HandoffEnvelope? handoff,
        out string? error,
        out string? normalizationNote)
    {
        handoff = null;
        error = null;
        normalizationNote = null;

        try
        {
            using var document = JsonDocument.Parse(candidate);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "JSON candidate is not an object.";
                return false;
            }

            var root = document.RootElement;
            var source = TryGetRelaySide(root, "source", out var parsedSource) ? parsedSource : expectedSource;
            var target = TryGetRelaySide(root, "target", out var parsedTarget) ? parsedTarget : expectedTarget;
            var ready = TryGetBoolean(root, "ready", out var readyValue)
                ? readyValue
                : InferReady(root);
            var requiresHuman = TryGetBoolean(root, "requires_human", out var requiresHumanValue)
                ? requiresHumanValue
                : false;

            var prompt = GetString(root, "prompt");
            if (string.IsNullOrWhiteSpace(prompt) && ready)
            {
                prompt = BuildFallbackPrompt(expectedSource, expectedTarget);
            }

            var reason = GetString(root, "reason");
            if (string.IsNullOrWhiteSpace(reason) && (!ready || requiresHuman))
            {
                reason = GetString(root, "message") ??
                         GetNestedString(root, "payload", "message") ??
                         GetString(root, "awaiting") ??
                         GetNestedString(root, "payload", "awaiting") ??
                         "Relay requires manual review.";
            }

            var summary = GetSummary(root);
            if (summary.Count == 0)
            {
                summary =
                [
                    GetString(root, "message") ??
                    GetNestedString(root, "payload", "message") ??
                    "Normalized relay handoff."
                ];
            }

            var normalized = new HandoffEnvelope
            {
                Type = "dad_handoff",
                Version = TryGetInt32(root, "version", out var version) ? version : 1,
                Source = source,
                Target = target,
                SessionId = GetString(root, "session_id") ?? expectedSessionId,
                Turn = TryGetInt32(root, "turn", out var turn) ? turn : expectedTurn,
                Ready = ready,
                Prompt = prompt ?? string.Empty,
                Summary = summary,
                Completed = GetStringArray(root, "completed"),
                Constraints = GetStringArray(root, "constraints"),
                RequiresHuman = requiresHuman,
                Reason = reason ?? string.Empty,
                CreatedAt = TryGetDateTimeOffset(root, "created_at", out var createdAt) ? createdAt : DateTimeOffset.Now,
            };

            var validationError = Validate(normalized);
            if (validationError is not null)
            {
                error = validationError;
                return false;
            }

            normalizationNote = "Accepted a normalized handoff from a near-valid JSON object.";
            handoff = normalized;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetRelaySide(JsonElement root, string propertyName, out RelaySide side)
    {
        side = default;
        var value = GetString(root, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(value, "codex", StringComparison.OrdinalIgnoreCase))
        {
            side = RelaySide.Codex;
            return true;
        }

        if (string.Equals(value, "claude", StringComparison.OrdinalIgnoreCase))
        {
            side = RelaySide.Claude;
            return true;
        }

        return false;
    }

    private static bool TryGetBoolean(JsonElement root, string propertyName, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            bool.TryParse(property.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryGetInt32(JsonElement root, string propertyName, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryGetDateTimeOffset(JsonElement root, string propertyName, out DateTimeOffset value)
    {
        value = default;
        var text = GetString(root, propertyName);
        return !string.IsNullOrWhiteSpace(text) &&
               DateTimeOffset.TryParse(text, out value);
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string? GetNestedString(JsonElement root, string objectPropertyName, string valuePropertyName)
    {
        if (!root.TryGetProperty(objectPropertyName, out var property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(property, valuePropertyName);
    }

    private static List<string> GetSummary(JsonElement root)
    {
        if (!root.TryGetProperty("summary", out var summaryElement) ||
            summaryElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return summaryElement.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Take(10)
            .ToList();
    }

    private static List<string> GetStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Take(10)
            .ToList();
    }

    private static bool InferReady(JsonElement root)
    {
        var status = (GetString(root, "status") ?? GetNestedString(root, "payload", "status"))?.Trim();
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        if (string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (status.StartsWith("awaiting", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string BuildFallbackPrompt(RelaySide expectedSource, RelaySide expectedTarget) =>
        $"Acknowledge the relay smoke test from {expectedSource.ToString().ToLowerInvariant()} while acting as {expectedTarget.ToString().ToLowerInvariant()}, then return one valid dad_handoff JSON object targeting {expectedSource.ToString().ToLowerInvariant()}.";

    private static bool LooksLikeSmokeTestPrompt(string originalPrompt) =>
        originalPrompt.Contains("relay transport smoke test", StringComparison.OrdinalIgnoreCase) ||
        originalPrompt.Contains("minimal dad_handoff", StringComparison.OrdinalIgnoreCase);

    private static string ShortenSentence(string text)
    {
        var firstSentence = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstSentence))
        {
            return "Normalized prose handoff.";
        }

        return firstSentence.Length <= 120 ? firstSentence : $"{firstSentence[..120]}...";
    }

    private static string? Validate(HandoffEnvelope handoff)
    {
        if (!string.Equals(handoff.Type, "dad_handoff", StringComparison.Ordinal))
        {
            return "type must equal 'dad_handoff'.";
        }

        if (handoff.Version != 1)
        {
            return "Only handoff schema version 1 is supported.";
        }

        if (string.IsNullOrWhiteSpace(handoff.SessionId))
        {
            return "session_id is required.";
        }

        if (handoff.Source == handoff.Target)
        {
            return "source and target must be different.";
        }

        if (handoff.Turn <= 0)
        {
            return "turn must be a positive integer.";
        }

        if (handoff.Ready && string.IsNullOrWhiteSpace(handoff.Prompt))
        {
            return "prompt is required when ready=true.";
        }

        if ((!handoff.Ready || handoff.RequiresHuman) && string.IsNullOrWhiteSpace(handoff.Reason))
        {
            return "reason is required when ready=false or requires_human=true.";
        }

        if (handoff.CreatedAt == default)
        {
            return "created_at is required.";
        }

        return null;
    }

    [GeneratedRegex("```json\\s*(?<json>\\{.*?\\})\\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonFenceRegex();
}
