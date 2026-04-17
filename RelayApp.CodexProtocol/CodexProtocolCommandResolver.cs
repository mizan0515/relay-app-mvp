namespace RelayApp.CodexProtocol;

public static class CodexProtocolCommandResolver
{
    public static string Resolve()
    {
        var preferredPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.ps1"),
        };

        var resolved = preferredPaths.FirstOrDefault(File.Exists);
        return !string.IsNullOrWhiteSpace(resolved) ? resolved : "codex.cmd";
    }
}
