using System.Security.Cryptography;
using System.Text;

namespace CodexClaudeRelay.Core.Protocol;

/// <summary>
/// Deterministic SHA-256 of text after whitespace normalization. Foundation
/// for G8 audit-log integrity — callers decide the canonical payload
/// (packet YAML, handoff JSON, state snapshot JSON) and feed it here to
/// stamp each event with a reproducible hash that survives reformatting.
/// Normalization rules (frozen — changing them invalidates every prior hash):
///   · CRLF / CR → LF
///   · trailing spaces/tabs on each line stripped
///   · trailing newlines trimmed
///   · empty string → all-zero SHA-256 is NOT used; we hash the empty byte span.
/// </summary>
public static class CanonicalHash
{
    public static string OfString(string? value)
    {
        var normalized = Normalize(value ?? string.Empty);
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public static string OfBytes(ReadOnlySpan<byte> bytes)
    {
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public static string Normalize(string value)
    {
        if (value.Length == 0) return string.Empty;
        var sb = new StringBuilder(value.Length);
        var line = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\r')
            {
                AppendTrimmed(sb, line);
                sb.Append('\n');
                if (i + 1 < value.Length && value[i + 1] == '\n') i++;
            }
            else if (ch == '\n')
            {
                AppendTrimmed(sb, line);
                sb.Append('\n');
            }
            else
            {
                line.Append(ch);
            }
        }
        AppendTrimmed(sb, line);
        while (sb.Length > 0 && sb[^1] == '\n') sb.Length--;
        return sb.ToString();
    }

    private static void AppendTrimmed(StringBuilder sb, StringBuilder line)
    {
        var end = line.Length;
        while (end > 0 && (line[end - 1] == ' ' || line[end - 1] == '\t')) end--;
        for (var i = 0; i < end; i++) sb.Append(line[i]);
        line.Clear();
    }
}
