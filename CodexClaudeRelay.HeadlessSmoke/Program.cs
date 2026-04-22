using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Desktop.Adapters;

// Headless live-CLI smoke: exercises the real CodexCliAdapter and ClaudeCliAdapter
// against the operator's logged-in CLIs and reports adapter health.
// Goal: prove the production CLI invocation pipeline (ProcessCommandRunner +
// adapter parsing) works end-to-end without launching the WPF UI. Useful in CI,
// in remote shells, and for agents that cannot click a window.

var workingDirectory = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

Console.WriteLine($"=== headless smoke @ {DateTimeOffset.Now:O} ===");
Console.WriteLine($"working dir: {workingDirectory}");
Console.WriteLine();

var adapters = new IRelayAdapter[]
{
    new CodexCliAdapter(workingDirectory),
    new ClaudeCliAdapter(workingDirectory),
};

var allHealthy = true;
foreach (var adapter in adapters)
{
    Console.Write($"[{adapter.Role,-7}] probing... ");
    try
    {
        var status = await adapter.GetStatusAsync(cts.Token);
        var marker = status.Health == RelayHealthStatus.Healthy ? "OK  " : "FAIL";
        Console.WriteLine($"{marker} health={status.Health} authenticated={status.IsAuthenticated}");
        if (!string.IsNullOrWhiteSpace(status.Message))
        {
            Console.WriteLine($"          detail: {status.Message.Replace("\n", " | ")}");
        }
        if (status.Health != RelayHealthStatus.Healthy)
        {
            allHealthy = false;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL exception: {ex.GetType().Name} {ex.Message}");
        allHealthy = false;
    }
}

Console.WriteLine();
Console.WriteLine(allHealthy
    ? "=== ALL ADAPTERS HEALTHY — relay can drive both peers from this machine."
    : "=== SOME ADAPTERS UNHEALTHY — relay would not be able to start a session.");
return allHealthy ? 0 : 1;
