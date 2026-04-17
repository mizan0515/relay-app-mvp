using RelayApp.Core.Models;

namespace RelayApp.Core.Pricing;

public sealed record CodexRateCard(
    double InputPerMillion,
    double CachedInputPerMillion,
    double OutputPerMillion);

public static class CodexPricing
{
    public const string RateCardVersion = "2026-02";
    public static readonly DateOnly RateCardAsOf = new(2026, 2, 1);

    public static readonly IReadOnlyDictionary<string, CodexRateCard> Defaults =
        new Dictionary<string, CodexRateCard>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-5-codex"] = new(1.25, 0.125, 10.00),
            ["gpt-5-codex-mini"] = new(0.25, 0.025, 2.00),
            ["default"] = new(1.25, 0.125, 10.00),
        };

    public static string DescribeRateCard() =>
        $"{RateCardVersion} (as-of {RateCardAsOf:yyyy-MM-dd})";

    public static (double? CostUsd, bool ModelRecognized) EstimateUsdWithRecognition(
        RelayUsageMetrics? usage,
        string? model = null)
    {
        if (usage is null || !usage.HasValues)
        {
            return (null, false);
        }

        var rateKey = string.IsNullOrWhiteSpace(model) ? "default" : model;
        var modelRecognized = Defaults.TryGetValue(rateKey, out var selected);
        var rates = modelRecognized
            ? selected!
            : Defaults["default"];

        var nonCachedInput = usage.InputTokens ?? 0;
        var cachedInput = usage.CacheReadInputTokens ?? 0;
        var output = usage.OutputTokens ?? 0;

        return
        (
            (nonCachedInput * rates.InputPerMillion
           + cachedInput * rates.CachedInputPerMillion
           + output * rates.OutputPerMillion) / 1_000_000.0,
            modelRecognized
        );
    }

    public static double? EstimateUsd(RelayUsageMetrics? usage, string? model = null)
    {
        var (costUsd, _) = EstimateUsdWithRecognition(usage, model);
        return costUsd;
    }
}
