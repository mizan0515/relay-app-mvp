using System.Globalization;
using System.Text;
using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Runtime;

public sealed record RollingSummaryFields(
    string SessionId,
    int SegmentNumber,
    string RotationReason,
    DateTimeOffset SessionStartedAt,
    int TurnsSinceLastRotation,
    RelaySide ActiveSideAtRotation,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalCacheReadInputTokens,
    long TotalCacheCreationInputTokens,
    double TotalCostClaudeUsd,
    double TotalCostCodexUsd,
    HandoffEnvelope? LastHandoff,
    string? PendingPrompt);

public sealed record RollingSummaryResult(string Path, int Bytes, string Markdown);

/// <summary>
/// Durable rolling-summary write extracted from <c>RelayBroker.WriteRollingSummaryAsync</c>.
/// Pure file IO + markdown construction — broker still owns event emission
/// (<c>summary.generated</c> / <c>summary.failed</c>) via its own PersistAndLogAsync.
/// Lives here so RotationSmokeRunner can drive G6 evidence headlessly.
/// </summary>
public static class RollingSummaryWriter
{
    public static string ResolveBaseDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexClaudeRelayMvp",
            "summaries");

    public static string ResolvePath(string baseDirectory, string sessionId, int segmentNumber) =>
        Path.Combine(baseDirectory, $"{sessionId}-segment-{segmentNumber}.md");

    public static string BuildMarkdown(RollingSummaryFields f)
    {
        var handoffBlock = f.LastHandoff is { } h
            ? $"- source: {h.Source}{Environment.NewLine}" +
              $"- target: {h.Target}{Environment.NewLine}" +
              $"- turn: {h.Turn}{Environment.NewLine}" +
              $"- ready: {h.Ready}{Environment.NewLine}" +
              $"- reason: {(string.IsNullOrWhiteSpace(h.Reason) ? "(none)" : h.Reason)}"
            : "- (no handoff captured this segment)";

        var pendingPrompt = string.IsNullOrWhiteSpace(f.PendingPrompt) ? "(none)" : f.PendingPrompt;
        var now = DateTimeOffset.Now;

        return
            $"# Session {f.SessionId} — segment {f.SegmentNumber}{Environment.NewLine}{Environment.NewLine}" +
            $"- Closed at: {now:O}{Environment.NewLine}" +
            $"- Segment started at: {f.SessionStartedAt:O}{Environment.NewLine}" +
            $"- Rotation reason: {f.RotationReason}{Environment.NewLine}" +
            $"- Turns in this segment: {f.TurnsSinceLastRotation}{Environment.NewLine}" +
            $"- Active side at rotation: {f.ActiveSideAtRotation}{Environment.NewLine}{Environment.NewLine}" +
            $"## Cumulative totals{Environment.NewLine}" +
            $"- input_tokens: {f.TotalInputTokens}{Environment.NewLine}" +
            $"- output_tokens: {f.TotalOutputTokens}{Environment.NewLine}" +
            $"- cache_read_input_tokens: {f.TotalCacheReadInputTokens}{Environment.NewLine}" +
            $"- cache_creation_input_tokens: {f.TotalCacheCreationInputTokens}{Environment.NewLine}" +
            $"- cost_claude_usd: {f.TotalCostClaudeUsd.ToString("F4", CultureInfo.InvariantCulture)}{Environment.NewLine}" +
            $"- cost_codex_usd: {f.TotalCostCodexUsd.ToString("F4", CultureInfo.InvariantCulture)}{Environment.NewLine}{Environment.NewLine}" +
            $"## Last handoff{Environment.NewLine}" +
            $"{handoffBlock}{Environment.NewLine}{Environment.NewLine}" +
            $"## Pending prompt at rotation boundary{Environment.NewLine}" +
            $"{pendingPrompt}{Environment.NewLine}";
    }

    public static async Task<RollingSummaryResult> WriteAsync(
        RollingSummaryFields fields,
        CancellationToken cancellationToken)
    {
        var baseDir = ResolveBaseDirectory();
        Directory.CreateDirectory(baseDir);
        var path = ResolvePath(baseDir, fields.SessionId, fields.SegmentNumber);
        var markdown = BuildMarkdown(fields);
        await File.WriteAllTextAsync(path, markdown, Encoding.UTF8, cancellationToken);
        var bytes = Encoding.UTF8.GetByteCount(markdown);
        return new RollingSummaryResult(path, bytes, markdown);
    }

    public static string BuildGeneratedEventPayload(
        string path,
        int bytes,
        int segmentNumber,
        string sessionId,
        int turns,
        double costClaudeUsd,
        double costCodexUsd)
    {
        return "{" +
            $"\"path\":\"{EscapeJsonString(path)}\"," +
            $"\"bytes\":{bytes}," +
            $"\"segment\":{segmentNumber}," +
            $"\"session_id\":\"{EscapeJsonString(sessionId)}\"," +
            $"\"turns\":{turns}," +
            $"\"cost_claude_usd\":{costClaudeUsd.ToString("F4", CultureInfo.InvariantCulture)}," +
            $"\"cost_codex_usd\":{costCodexUsd.ToString("F4", CultureInfo.InvariantCulture)}" +
            "}";
    }

    internal static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                    {
                        builder.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }
        return builder.ToString();
    }
}
