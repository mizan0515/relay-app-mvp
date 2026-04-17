namespace RelayApp.CodexProtocol;

public static class CodexProtocolSpikeRunner
{
    public static async Task<CodexProtocolSpikeRunResult> RunAsync(
        string workingDirectory,
        string? prompt,
        CancellationToken cancellationToken)
    {
        var turnResult = await CodexProtocolTurnRunner.RunOneShotAsync(
            workingDirectory,
            string.IsNullOrWhiteSpace(prompt) ? "Return exactly the word ok." : prompt,
            jobObjectOptions: null,
            serverRequestHandler: null,
            cancellationToken);

        return new CodexProtocolSpikeRunResult
        {
            WorkingDirectory = workingDirectory,
            InitializeResult = turnResult.InitializeResult,
            AuthStatus = turnResult.AuthStatus,
            ThreadStartResult = turnResult.ThreadStartResult,
            TurnStartResult = turnResult.TurnStartResult,
            TurnCompletedNotification = turnResult.TurnCompletedNotification,
            ThreadId = turnResult.ThreadId,
            Messages = turnResult.Messages
        };
    }
}
