using System.Text;

namespace RelayApp.Core.Models;

public static class ClaudeAuthMethod
{
    public static bool IsApiKey(string? authMethod) =>
        string.Equals(Normalize(authMethod), "apikey", StringComparison.Ordinal);

    public static string? Normalize(string? authMethod)
    {
        if (string.IsNullOrWhiteSpace(authMethod))
        {
            return null;
        }

        var builder = new StringBuilder(authMethod.Length);
        foreach (var character in authMethod)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
