namespace CodexClaudeRelay.Desktop;

internal sealed class RelayUiSettings
{
    public string? WorkingDirectory { get; set; }

    public string? SessionId { get; set; }

    public string? InitialPrompt { get; set; }

    public string? AdmissionManifestPath { get; set; }

    public string? ManagedCardGameRoot { get; set; }

    public string? ManagedTaskSlug { get; set; }

    public int ManagedTurns { get; set; } = 2;

    public int ManagedLoopSessions { get; set; } = 3;

    public bool UseInteractiveAdapters { get; set; }

    public bool AutoApproveAllRequests { get; set; }
}
