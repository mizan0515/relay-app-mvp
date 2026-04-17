namespace RelayApp.Desktop.Adapters;

internal sealed record ProcessInvocationResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
