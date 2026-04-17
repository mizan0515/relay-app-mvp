using System.IO;
using System.Text;

namespace RelayApp.Desktop.Adapters;

internal static class CodexModelResolver
{
    public static string? TryResolveConfiguredModel()
    {
        try
        {
            return TryResolveConfiguredModelCore();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string? TryResolveConfiguredModelCore()
    {
        var configPath = GetConfigPath();
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return null;
        }

        string? rootModel = null;
        string? selectedProfile = null;
        var profileModels = new Dictionary<string, string>(StringComparer.Ordinal);
        var currentSection = string.Empty;

        foreach (var rawLine in File.ReadLines(configPath))
        {
            var line = StripInlineComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            var (key, value) = TrySplitKeyValue(line);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (currentSection.Length == 0)
            {
                if (string.Equals(key, "model", StringComparison.Ordinal))
                {
                    rootModel = value;
                }
                else if (string.Equals(key, "profile", StringComparison.Ordinal))
                {
                    selectedProfile = value;
                }

                continue;
            }

            if (!currentSection.StartsWith("profiles.", StringComparison.Ordinal) ||
                !string.Equals(key, "model", StringComparison.Ordinal))
            {
                continue;
            }

            var profileName = currentSection["profiles.".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                profileModels[profileName] = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedProfile) &&
            profileModels.TryGetValue(selectedProfile, out var profileModel) &&
            !string.IsNullOrWhiteSpace(profileModel))
        {
            return profileModel;
        }

        return string.IsNullOrWhiteSpace(rootModel) ? null : rootModel;
    }

    private static string StripInlineComment(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (current == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (current == '#' && !inSingleQuote && !inDoubleQuote)
            {
                return line[..i];
            }
        }

        return line;
    }

    private static (string? Key, string? Value) TrySplitKeyValue(string line)
    {
        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
        {
            return (null, null);
        }

        var key = line[..separatorIndex].Trim();
        var rawValue = line[(separatorIndex + 1)..].Trim();
        if (rawValue.Length < 2)
        {
            return (null, null);
        }

        if ((rawValue[0] == '"' && rawValue[^1] == '"') ||
            (rawValue[0] == '\'' && rawValue[^1] == '\''))
        {
            return (key, rawValue[1..^1]);
        }

        return (null, null);
    }

    private static string? GetConfigPath()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            return Path.Combine(codexHome, "config.toml");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? null
            : Path.Combine(userProfile, ".codex", "config.toml");
    }
}
