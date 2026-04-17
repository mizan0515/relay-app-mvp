using System.Diagnostics;
using System.IO;
using System.Text;
using RelayApp.Core.Broker;

namespace RelayApp.Desktop.Adapters;

internal sealed class ProcessCommandRunner
{
    private readonly RelayJobObjectOptions _jobObjectOptions;

    public ProcessCommandRunner(RelayJobObjectOptions? jobObjectOptions = null)
    {
        _jobObjectOptions = jobObjectOptions ?? new RelayJobObjectOptions();
    }

    public async Task<ProcessInvocationResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw new InvalidOperationException($"Working directory does not exist: {workingDirectory}");
        }

        if (OperatingSystem.IsWindows())
        {
            return await RunWindowsAsync(fileName, arguments, workingDirectory, cancellationToken);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process '{fileName}'.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not start '{fileName}'. If this is a CLI wrapper, configure an executable path the desktop app can launch directly. Details: {ex.Message}",
                ex);
        }

        using var jobObject = WindowsJobObject.TryAttach(process, _jobObjectOptions);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                jobObject?.Dispose();
            }
            catch
            {
                // Fall through to the process-level fallback.
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The Windows job object remains the primary cleanup path.
            }

            throw;
        }

        return new ProcessInvocationResult(
            process.ExitCode,
            stdout.ToString().Trim(),
            stderr.ToString().Trim());
    }

    private async Task<ProcessInvocationResult> RunWindowsAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var launch = NativeProcessLauncher.StartSuspended(fileName, arguments, workingDirectory);
        using var process = launch.Process;

        WindowsJobObject? jobObject;
        try
        {
            jobObject = WindowsJobObject.TryAttach(process, _jobObjectOptions);
        }
        catch
        {
            // TryAttach throws after CreateProcessW has already returned a CREATE_SUSPENDED
            // child. launch.Dispose() closes stdio and the main-thread handle but never
            // calls ResumeThread or TerminateProcess, and Process.Dispose() only releases
            // the managed handle — without a best-effort kill the OS process would stay
            // suspended until system shutdown.
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort; rethrow the original attach failure.
            }
            throw;
        }

        using var jobObjectGuard = jobObject;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = ReadStreamAsync(launch.StandardOutput, stdout, cancellationToken);
        var stderrTask = ReadStreamAsync(launch.StandardError, stderr, cancellationToken);

        launch.Resume();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (OperationCanceledException)
        {
            try
            {
                jobObject?.Dispose();
            }
            catch
            {
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            throw;
        }

        return new ProcessInvocationResult(
            process.ExitCode,
            stdout.ToString().Trim(),
            stderr.ToString().Trim());
    }

    private static async Task ReadStreamAsync(
        TextReader reader,
        StringBuilder builder,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            builder.AppendLine(line);
        }
    }
}
