namespace RelayApp.Desktop;

internal sealed class RelayUiSettings
{
    public string? WorkingDirectory { get; set; }

    public string? SessionId { get; set; }

    public string? InitialPrompt { get; set; }

    public bool UseInteractiveAdapters { get; set; }

    public bool AutoApproveAllRequests { get; set; }
}
