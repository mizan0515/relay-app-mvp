using RelayApp.Core.Broker;
using System.Collections.Concurrent;
using System.Text.Json;

namespace RelayApp.CodexProtocol;

public static class CodexProtocolTurnRunner
{
    public static async Task<CodexProtocolTurnResult> RunOneShotAsync(
        string workingDirectory,
        string prompt,
        RelayJobObjectOptions? jobObjectOptions,
        Func<CodexProtocolServerRequest, CancellationToken, Task<CodexProtocolServerRequestResponse>>? serverRequestHandler,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var messages = new ConcurrentQueue<CodexProtocolMessage>();
        string? finalAgentMessageText = null;
        string? lastAgentMessageDelta = null;
        JsonElement lastTokenUsageNotification = default;

        await using var connection = await CodexProtocolConnection.StartAsync(
            new CodexProtocolConnectionOptions
            {
                WorkingDirectory = workingDirectory,
                JobObjectOptions = jobObjectOptions,
                ServerRequestHandler = serverRequestHandler,
                MessageObserver = message =>
                {
                    messages.Enqueue(CloneMessage(message));

                    if (message.Kind != CodexProtocolMessageKind.Notification || message.Method is null)
                    {
                        return;
                    }

                    var payloadIsObject = message.Payload.ValueKind == JsonValueKind.Object;

                    if (message.Method == CodexProtocolMethods.ItemAgentMessageDeltaNotification &&
                        payloadIsObject &&
                        message.Payload.TryGetProperty("delta", out var deltaElement) &&
                        deltaElement.ValueKind == JsonValueKind.String)
                    {
                        lastAgentMessageDelta = deltaElement.GetString();
                        return;
                    }

                    if (message.Method == CodexProtocolMethods.ItemCompletedNotification &&
                        TryExtractCompletedAgentMessage(message.Payload, out var completedText))
                    {
                        finalAgentMessageText = completedText;
                        return;
                    }

                    if (message.Method == CodexProtocolMethods.ThreadTokenUsageUpdatedNotification)
                    {
                        lastTokenUsageNotification = payloadIsObject
                            ? message.Payload.Clone()
                            : default;
                    }
                }
            },
            cancellationToken);

        var initializeResult = await connection.SendRequestAsync(
            CodexProtocolMethods.Initialize,
            new
            {
                clientInfo = new
                {
                    name = "relay-app-codex-spike",
                    title = "Relay App Codex Protocol Spike",
                    version = "0.1.0"
                },
                capabilities = new
                {
                    experimentalApi = true,
                    optOutNotificationMethods = Array.Empty<string>()
                }
            },
            cancellationToken);

        var authStatus = await connection.SendRequestAsync(CodexProtocolMethods.GetAuthStatus, new { }, cancellationToken);

        var threadStartResult = await connection.SendRequestAsync(
            CodexProtocolMethods.ThreadStart,
            new
            {
                model = (string?)null,
                modelProvider = (string?)null,
                serviceTier = (string?)null,
                cwd = workingDirectory,
                approvalPolicy = "on-request",
                approvalsReviewer = (object?)null,
                sandbox = "workspace-write",
                config = (object?)null,
                serviceName = "relay-app-codex-spike",
                baseInstructions = (string?)null,
                developerInstructions = "This is a transport spike. Return concise answers.",
                personality = (string?)null,
                ephemeral = true,
                experimentalRawEvents = false,
                persistExtendedHistory = true
            },
            cancellationToken);

        var threadId = threadStartResult
            .GetProperty("thread")
            .GetProperty("id")
            .GetString();

        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("Thread start did not return a thread id.");
        }

        // Register the turn/completed waiter BEFORE issuing turn.start so a fast-path
        // notification that arrives immediately after the turn.start response cannot be
        // dropped by CodexProtocolConnection.ResolveNotificationWaiter (no replay buffer).
        var turnCompletedTask = connection.WaitForNotificationAsync(
            CodexProtocolMethods.TurnCompletedNotification,
            cancellationToken);

        var turnStartResult = await connection.SendRequestAsync(
            CodexProtocolMethods.TurnStart,
            new
            {
                threadId,
                input = new object[]
                {
                    new
                    {
                        type = "text",
                        text = prompt,
                        text_elements = Array.Empty<object>()
                }
            },
            approvalPolicy = "on-request",
            outputSchema = (object?)null
        },
        cancellationToken);

        var turnId = turnStartResult
            .GetProperty("turn")
            .GetProperty("id")
            .GetString();

        if (string.IsNullOrWhiteSpace(turnId))
        {
            throw new InvalidOperationException("Turn start did not return a turn id.");
        }

        // No internal timeout: the caller's cancellationToken (broker PerTurnTimeout) is
        // the sole authority for how long an in-flight Codex turn may run.
        var turnCompleted = await turnCompletedTask;

        return new CodexProtocolTurnResult
        {
            WorkingDirectory = workingDirectory,
            Prompt = prompt,
            ThreadId = threadId,
            TurnId = turnId,
            InitializeResult = initializeResult,
            AuthStatus = authStatus,
            ThreadStartResult = threadStartResult,
            TurnStartResult = turnStartResult,
            TurnCompletedNotification = turnCompleted,
            FinalAgentMessageText = finalAgentMessageText,
            LastAgentMessageDelta = lastAgentMessageDelta,
            LastTokenUsageNotification = lastTokenUsageNotification,
            Messages = messages.ToArray()
        };
    }

    private static bool TryExtractCompletedAgentMessage(JsonElement payload, out string? completedText)
    {
        completedText = null;

        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("item", out var itemElement) ||
            !itemElement.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String ||
            !string.Equals(typeElement.GetString(), "agentMessage", StringComparison.Ordinal))
        {
            return false;
        }

        if (!itemElement.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        completedText = textElement.GetString();
        return true;
    }

    private static CodexProtocolMessage CloneMessage(CodexProtocolMessage message)
    {
        var payload = message.Payload.ValueKind == JsonValueKind.Undefined
            ? default
            : message.Payload.Clone();

        return message with { Payload = payload };
    }
}
