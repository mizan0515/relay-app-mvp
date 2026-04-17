using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace RelayApp.CodexProtocol;

public sealed class CodexProtocolConnection : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TaskCompletionSource<JsonElement>>> _notificationWaiters = new(StringComparer.Ordinal);
    private readonly Process _process;
    private readonly TextWriter _standardInput;
    private readonly TextReader _standardOutput;
    private readonly TextReader _standardError;
    private readonly IDisposable? _nativeLaunch;
    private readonly IDisposable? _jobObject;
    private readonly Action<CodexProtocolMessage>? _messageObserver;
    private readonly Func<CodexProtocolServerRequest, CancellationToken, Task<CodexProtocolServerRequestResponse>>? _serverRequestHandler;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _nextId;

    private CodexProtocolConnection(
        Process process,
        TextWriter standardInput,
        TextReader standardOutput,
        TextReader standardError,
        Action<CodexProtocolMessage>? messageObserver,
        Func<CodexProtocolServerRequest, CancellationToken, Task<CodexProtocolServerRequestResponse>>? serverRequestHandler,
        IDisposable? nativeLaunch = null,
        IDisposable? jobObject = null)
    {
        _process = process;
        _standardInput = standardInput;
        _standardOutput = standardOutput;
        _standardError = standardError;
        _messageObserver = messageObserver;
        _serverRequestHandler = serverRequestHandler;
        _nativeLaunch = nativeLaunch;
        _jobObject = jobObject;
    }

    public static async Task<CodexProtocolConnection> StartAsync(
        CodexProtocolConnectionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);

        if (!Directory.Exists(options.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"Working directory does not exist: {options.WorkingDirectory}");
        }

        CodexProtocolProcessLaunch? nativeLaunch = null;
        CodexProtocolWindowsJobObject? jobObject = null;
        Process? process = null;
        CodexProtocolConnection? connection = null;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                nativeLaunch = CodexProtocolWindowsProcessLauncher.StartSuspended(
                    CodexProtocolCommandResolver.Resolve(),
                    ["app-server", "--listen", "stdio://"],
                    options.WorkingDirectory);
                process = nativeLaunch.Process;
                jobObject = CodexProtocolWindowsJobObject.TryAttach(process, options.JobObjectOptions);
                connection = new CodexProtocolConnection(
                    process,
                    nativeLaunch.StandardInput,
                    nativeLaunch.StandardOutput,
                    nativeLaunch.StandardError,
                    options.MessageObserver,
                    options.ServerRequestHandler,
                    nativeLaunch,
                    jobObject);
                nativeLaunch.Resume();
            }
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = CodexProtocolCommandResolver.Resolve(),
                    WorkingDirectory = options.WorkingDirectory,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                startInfo.ArgumentList.Add("app-server");
                startInfo.ArgumentList.Add("--listen");
                startInfo.ArgumentList.Add("stdio://");

                process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true,
                };

                if (!process.Start())
                {
                    throw new InvalidOperationException("Failed to start codex app-server.");
                }

                connection = new CodexProtocolConnection(
                    process,
                    process.StandardInput,
                    process.StandardOutput,
                    process.StandardError,
                    options.MessageObserver,
                    options.ServerRequestHandler);
            }
        }
        catch
        {
            try
            {
                if (process is { HasExited: false })
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort cleanup for attach/startup failures.
            }

            jobObject?.Dispose();
            nativeLaunch?.Dispose();
            process?.Dispose();
            throw;
        }

        try
        {
            _ = Task.Run(() => connection.ReadStdoutLoopAsync(cancellationToken), cancellationToken);
            _ = Task.Run(() => connection.ReadStderrLoopAsync(cancellationToken), cancellationToken);

            // If the caller cancels during the startup warmup window (or any exception
            // slips out of Task.Run scheduling), we must dispose the already-launched
            // process, Job Object, and native launch handles ourselves: the caller's
            // `await using` binding does not take effect until StartAsync returns, so
            // DisposeAsync would otherwise never run and the codex child would leak.
            await Task.Delay(250, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<JsonElement> SendRequestAsync(string method, object? @params, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        ThrowIfExited();

        var id = Interlocked.Increment(ref _nextId).ToString();
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException($"Duplicate request id '{id}'.");
        }

        var payload = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["id"] = id,
                ["method"] = method,
                ["params"] = @params
            },
            JsonOptions);

        try
        {
            await WriteJsonLineAsync(payload, cancellationToken);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        using var registration = cancellationToken.Register(
            static state =>
            {
                if (state is PendingRequestCancellation pending)
                {
                    if (pending.Pending.TryRemove(pending.Id, out var source))
                    {
                        source.TrySetCanceled(pending.CancellationToken);
                        return;
                    }

                    pending.Completion.TrySetCanceled(pending.CancellationToken);
                }
            },
            new PendingRequestCancellation(_pending, id, completion, cancellationToken));

        return await completion.Task;
    }

    public Task<JsonElement> WaitForNotificationAsync(string method, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = _notificationWaiters.GetOrAdd(method, static _ => new ConcurrentQueue<TaskCompletionSource<JsonElement>>());
        queue.Enqueue(completion);

        cancellationToken.Register(
            static state =>
            {
                if (state is NotificationWaiterCancellation waiter)
                {
                    waiter.Completion.TrySetCanceled(waiter.CancellationToken);
                }
            },
            new NotificationWaiterCancellation(completion, cancellationToken));

        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetCanceled();
        }

        foreach (var queue in _notificationWaiters.Values)
        {
            while (queue.TryDequeue(out var waiter))
            {
                waiter.TrySetCanceled();
            }
        }

        if (!_process.HasExited)
        {
            try
            {
                _jobObject?.Dispose();
            }
            catch
            {
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            try
            {
                await _process.WaitForExitAsync();
            }
            catch
            {
            }
        }

        _nativeLaunch?.Dispose();
        _jobObject?.Dispose();
        _process.Dispose();
        _writeLock.Dispose();
    }

    private async Task ReadStdoutLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _standardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            _messageObserver?.Invoke(new CodexProtocolMessage(CodexProtocolMessageKind.StdoutLine, Text: line));

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;

                // JSON-RPC frames are always objects. Any parseable-but-non-object line
                // (null / scalar / array — e.g. a CLI startup banner that happens to be
                // valid JSON) would make TryGetProperty throw InvalidOperationException
                // and tear down this read loop, hanging every subsequent RPC until the
                // caller's cancellation token fires. Skip non-object frames defensively.
                if (root.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var hasId = root.TryGetProperty("id", out var idElement);
                var hasMethod = root.TryGetProperty("method", out var methodElement);

                if (hasId && !hasMethod)
                {
                    HandleResponse(idElement, root);
                    continue;
                }

                if (hasMethod && !hasId)
                {
                    var method = methodElement.GetString() ?? "(unknown)";
                    var parameters = root.TryGetProperty("params", out var paramsElement)
                        ? paramsElement.Clone()
                        : default;
                    _messageObserver?.Invoke(new CodexProtocolMessage(CodexProtocolMessageKind.Notification, Method: method, Payload: parameters));
                    ResolveNotificationWaiter(method, parameters);
                    continue;
                }

                if (hasId && hasMethod)
                {
                    var method = methodElement.GetString() ?? "(unknown)";
                    var parameters = root.TryGetProperty("params", out var paramsElement)
                        ? paramsElement.Clone()
                        : default;
                    _messageObserver?.Invoke(new CodexProtocolMessage(CodexProtocolMessageKind.ServerRequest, Method: method, Payload: parameters));
                    await HandleServerRequestAsync(idElement.Clone(), method, parameters, cancellationToken);
                }
            }
        }
    }

    private void ResolveNotificationWaiter(string method, JsonElement parameters)
    {
        if (!_notificationWaiters.TryGetValue(method, out var queue))
        {
            return;
        }

        while (queue.TryDequeue(out var waiter))
        {
            if (waiter.Task.IsCompleted)
            {
                continue;
            }

            waiter.TrySetResult(parameters);
            break;
        }
    }

    private async Task ReadStderrLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _standardError.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            _messageObserver?.Invoke(new CodexProtocolMessage(CodexProtocolMessageKind.StderrLine, Text: line));
        }
    }

    private void HandleResponse(JsonElement idElement, JsonElement root)
    {
        var id = idElement.ValueKind switch
        {
            JsonValueKind.String => idElement.GetString(),
            JsonValueKind.Number => idElement.GetRawText(),
            _ => idElement.GetRawText()
        };

        if (id is null || !_pending.TryRemove(id, out var completion))
        {
            return;
        }

        if (root.TryGetProperty("error", out var errorElement))
        {
            completion.TrySetException(new InvalidOperationException($"RPC error for id {id}: {errorElement.GetRawText()}"));
            return;
        }

        if (root.TryGetProperty("result", out var resultElement))
        {
            completion.TrySetResult(resultElement.Clone());
            return;
        }

        completion.TrySetException(new InvalidOperationException($"RPC response for id {id} had neither result nor error."));
    }

    private async Task ReplyNotImplementedAsync(JsonElement idElement, CancellationToken cancellationToken)
    {
        await ReplyErrorAsync(idElement, -32601, "Relay app prototype does not implement this server-initiated request yet.", cancellationToken);
    }

    private async Task HandleServerRequestAsync(
        JsonElement idElement,
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_serverRequestHandler is null)
            {
                await ReplyNotImplementedAsync(idElement, cancellationToken);
                return;
            }

            var request = new CodexProtocolServerRequest(method, parameters);
            var response = await _serverRequestHandler(request, cancellationToken);
            if (!response.Handled)
            {
                await ReplyNotImplementedAsync(idElement, cancellationToken);
                return;
            }

            await ReplyResultAsync(idElement, response.Result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _messageObserver?.Invoke(
                new CodexProtocolMessage(
                    CodexProtocolMessageKind.StderrLine,
                    Text: $"Failed to reply to server-initiated request: {ex.Message}"));
            try
            {
                await ReplyErrorAsync(idElement, -32000, $"Server request handling failed: {ex.Message}", cancellationToken);
            }
            catch
            {
                // Best-effort only.
            }
        }
    }

    private async Task ReplyResultAsync(JsonElement idElement, object? result, CancellationToken cancellationToken)
    {
        var response = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["id"] = DeserializeRequestId(idElement),
                ["result"] = result
            },
            JsonOptions);

        await WriteJsonLineAsync(response, cancellationToken);
    }

    private async Task ReplyErrorAsync(JsonElement idElement, int code, string message, CancellationToken cancellationToken)
    {
        var response = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["id"] = DeserializeRequestId(idElement),
                ["error"] = new Dictionary<string, object?>
                {
                    ["code"] = code,
                    ["message"] = message
                }
            },
            JsonOptions);

        await WriteJsonLineAsync(response, cancellationToken);
    }

    private static object? DeserializeRequestId(JsonElement idElement) =>
        idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()
            : JsonSerializer.Deserialize<object>(idElement.GetRawText());

    private async Task WriteJsonLineAsync(string payload, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _standardInput.WriteLineAsync(payload.AsMemory(), cancellationToken);
            await _standardInput.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void ThrowIfExited()
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException($"codex app-server exited with code {_process.ExitCode}.");
        }
    }

    private sealed record PendingRequestCancellation(
        ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> Pending,
        string Id,
        TaskCompletionSource<JsonElement> Completion,
        CancellationToken CancellationToken);

    private sealed record NotificationWaiterCancellation(
        TaskCompletionSource<JsonElement> Completion,
        CancellationToken CancellationToken);
}
