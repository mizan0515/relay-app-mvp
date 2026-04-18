using System.Text;
using CodexClaudeRelay.Core.Models;

namespace CodexClaudeRelay.Core.Protocol;

public sealed record SessionStateSnapshot(
    string SessionId,
    int CurrentTurn,
    string ActiveAgent,
    DateTimeOffset UpdatedAt);

public static class SessionStatePersister
{
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

        var sb = new StringBuilder();
        sb.Append('{').Append('\n');
        sb.Append("  \"session_id\": ").Append(JsonString(state.SessionId)).Append(",\n");
        sb.Append("  \"current_turn\": ").Append(state.CurrentTurn).Append(",\n");
        sb.Append("  \"active_agent\": ").Append(JsonString(state.ActiveAgent)).Append(",\n");
        sb.Append("  \"updated_at\": ").Append(JsonString(state.UpdatedAt.ToString("O"))).Append('\n');
        sb.Append('}').Append('\n');
        return sb.ToString();
    }

    private static string JsonString(string? value)
    {
        if (value is null) return "null";
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
