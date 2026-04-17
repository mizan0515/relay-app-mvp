using RelayApp.CodexProtocol;
using System.Text.Json;

namespace RelayApp.CodexProtocol.Spike;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var workingDirectory = args.Length > 0 ? args[0] : @"D:\dad-relay-mvp-temp";
        var prompt = args.Length > 1 ? string.Join(' ', args.Skip(1)) : null;
        if (!Directory.Exists(workingDirectory))
        {
            Console.Error.WriteLine($"Working directory does not exist: {workingDirectory}");
            return 1;
        }

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var cancellationToken = cancellationSource.Token;

        Console.WriteLine($"Starting codex app-server spike in: {workingDirectory}");
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            Console.WriteLine("Using custom prompt.");
        }

        var result = await CodexProtocolSpikeRunner.RunAsync(workingDirectory, prompt, cancellationToken);

        Console.WriteLine($"Initialize result: {result.InitializeResult.GetRawText()}");
        Console.WriteLine($"Auth status: {result.AuthStatus.GetRawText()}");
        Console.WriteLine($"Thread start result: {result.ThreadStartResult.GetRawText()}");
        Console.WriteLine($"Turn start result: {result.TurnStartResult.GetRawText()}");
        Console.WriteLine($"Turn completed notification: {result.TurnCompletedNotification.GetRawText()}");
        Console.WriteLine("Observed protocol messages:");

        foreach (var message in result.Messages)
        {
            WriteMessage(message);
        }

        Console.WriteLine("Spike complete.");
        return 0;
    }

    private static void WriteMessage(CodexProtocolMessage message)
    {
        switch (message.Kind)
        {
            case CodexProtocolMessageKind.StdoutLine:
                Console.WriteLine($"[OUT] {message.Text}");
                break;
            case CodexProtocolMessageKind.StderrLine:
                Console.WriteLine($"[ERR] {message.Text}");
                break;
            case CodexProtocolMessageKind.Notification:
            case CodexProtocolMessageKind.ServerRequest:
                var pretty = message.Payload.ValueKind == JsonValueKind.Undefined
                    ? "(no params)"
                    : JsonSerializer.Serialize(message.Payload, new JsonSerializerOptions { WriteIndented = false });
                Console.WriteLine($"[{message.Kind}] {message.Method} {pretty}");
                break;
            default:
                Console.WriteLine($"[{message.Kind}] {message.Text ?? message.Method ?? "(empty)"}");
                break;
        }
    }
}
