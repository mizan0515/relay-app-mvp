using System.Text.Json;

namespace RelayApp.Core.Models;

public sealed record RelayUsageMetrics(
    long? InputTokens = null,
    long? OutputTokens = null,
    long? CacheCreationInputTokens = null,
    long? CacheReadInputTokens = null,
    double? CostUsd = null,
    string? RawJson = null,
    string? Model = null,
    IReadOnlyDictionary<string, double>? ModelUsageUsd = null,
    string? PricingFallbackReason = null,
    string? CliVersion = null,
    string? AuthMethod = null)
{
    public bool HasValues =>
        InputTokens.HasValue ||
        OutputTokens.HasValue ||
        CacheCreationInputTokens.HasValue ||
        CacheReadInputTokens.HasValue ||
        CostUsd.HasValue ||
        !string.IsNullOrWhiteSpace(RawJson);

    public static RelayUsageMetrics? FromClaudeStreamJson(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        var inputTokens = ReadLongProperty(element, "input_tokens");
        var outputTokens = ReadLongProperty(element, "output_tokens");
        var cacheCreationInputTokens = ReadLongProperty(element, "cache_creation_input_tokens");
        var cacheReadInputTokens = ReadLongProperty(element, "cache_read_input_tokens");
        var metrics = new RelayUsageMetrics(
            inputTokens,
            outputTokens,
            cacheCreationInputTokens,
            cacheReadInputTokens,
            null,
            element.GetRawText());

        return metrics.HasValues ? metrics : null;
    }

    public static RelayUsageMetrics? FromClaudeJson(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        RelayUsageMetrics? usage = null;
        if (element.TryGetProperty("usage", out var usageElement))
        {
            usage = FromClaudeStreamJson(usageElement);
        }

        var costUsd = ReadDoubleProperty(element, "total_cost_usd");
        var model = ReadClaudeModel(element);
        var modelUsageUsd = ReadClaudeModelUsageUsd(element);
        if (usage is null && !costUsd.HasValue && string.IsNullOrWhiteSpace(model) && modelUsageUsd is null)
        {
            return null;
        }

        if (usage is null)
        {
            usage = new RelayUsageMetrics(
                CostUsd: costUsd,
                RawJson: element.GetRawText(),
                Model: model,
                ModelUsageUsd: modelUsageUsd);
        }
        else if (costUsd.HasValue)
        {
            usage = usage with
            {
                CostUsd = costUsd,
                RawJson = element.GetRawText(),
                Model = model ?? usage.Model,
                ModelUsageUsd = modelUsageUsd ?? usage.ModelUsageUsd
            };
        }
        else if (!string.IsNullOrWhiteSpace(model) || modelUsageUsd is not null)
        {
            usage = usage with
            {
                Model = model ?? usage.Model,
                RawJson = element.GetRawText(),
                ModelUsageUsd = modelUsageUsd ?? usage.ModelUsageUsd
            };
        }

        return usage;
    }

    public static string[] ReadClaudeModelUsageKeys(JsonElement element)
    {
        var modelUsageUsd = ReadClaudeModelUsageUsd(element);
        return modelUsageUsd?.Keys.ToArray() ?? [];
    }

    public static IReadOnlyDictionary<string, double>? ReadClaudeModelUsageUsd(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("model_usage", out var modelUsageElement) ||
            modelUsageElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var costs = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var property in modelUsageElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty("costUSD", out var costElement))
            {
                continue;
            }

            if (costElement.ValueKind == JsonValueKind.Number && costElement.TryGetDouble(out var numericCost))
            {
                costs[property.Name] = numericCost;
            }
            else if (costElement.ValueKind == JsonValueKind.String &&
                     double.TryParse(costElement.GetString(), out var parsedCost))
            {
                costs[property.Name] = parsedCost;
            }
        }

        return costs.Count == 0 ? null : costs;
    }

    public static RelayUsageMetrics? FromCodexTokenCount(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        if (!element.TryGetProperty("tokenUsage", out var tokenUsageElement) ||
            tokenUsageElement.ValueKind != JsonValueKind.Object ||
            !tokenUsageElement.TryGetProperty("total", out var totalElement) ||
            totalElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // NOTE: Reading tokenUsage.total is correct for the current one-shot Codex thread model.
        // If a caller preserves the same app-server thread across broker turns, the returned values
        // are cumulative thread totals and the adapter must mark UsageIsCumulative=true so the broker
        // can convert them back into per-turn deltas before applying budget counters.
        var inputTokens = ReadLongProperty(totalElement, "inputTokens");
        var cachedInputTokens = ReadLongProperty(totalElement, "cachedInputTokens");
        long? nonCachedInputTokens = inputTokens.HasValue
            ? Math.Max(0, inputTokens.Value - (cachedInputTokens ?? 0))
            : null;
        var outputTokens = ReadLongProperty(totalElement, "outputTokens");

        var metrics = new RelayUsageMetrics(
            nonCachedInputTokens,
            outputTokens,
            null,
            cachedInputTokens,
            null,
            element.GetRawText());

        return metrics.HasValues ? metrics : null;
    }

    public static RelayUsageMetrics? FromCodexExecUsage(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var inputTokens = ReadLongProperty(element, "input_tokens");
        var cachedInputTokens = ReadLongProperty(element, "cached_input_tokens");
        long? nonCachedInputTokens = inputTokens.HasValue
            ? Math.Max(0, inputTokens.Value - (cachedInputTokens ?? 0))
            : null;
        var outputTokens = ReadLongProperty(element, "output_tokens");

        var metrics = new RelayUsageMetrics(
            nonCachedInputTokens,
            outputTokens,
            null,
            cachedInputTokens,
            null,
            element.GetRawText());

        return metrics.HasValues ? metrics : null;
    }

    private static long? ReadLongProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        if (propertyValue.ValueKind == JsonValueKind.Number && propertyValue.TryGetInt64(out var numericValue))
        {
            return numericValue;
        }

        if (propertyValue.ValueKind == JsonValueKind.String &&
            long.TryParse(propertyValue.GetString(), out var parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    private static double? ReadDoubleProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        if (propertyValue.ValueKind == JsonValueKind.Number && propertyValue.TryGetDouble(out var numericValue))
        {
            return numericValue;
        }

        if (propertyValue.ValueKind == JsonValueKind.String &&
            double.TryParse(propertyValue.GetString(), out var parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    private static string? ReadClaudeModel(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("model", out var modelElement) &&
            modelElement.ValueKind == JsonValueKind.String)
        {
            var model = modelElement.GetString();
            return string.IsNullOrWhiteSpace(model) ? null : model;
        }

        var modelUsageKeys = ReadClaudeModelUsageKeys(element);
        return modelUsageKeys.Length == 1 ? modelUsageKeys[0] : null;
    }
}
