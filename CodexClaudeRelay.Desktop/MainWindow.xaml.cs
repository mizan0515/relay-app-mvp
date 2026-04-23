using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using CodexClaudeRelay.Core.Adapters;
using CodexClaudeRelay.Core.Broker;
using CodexClaudeRelay.Core.Models;
using CodexClaudeRelay.Core.Policy;
using CodexClaudeRelay.Core.Persistence;
using CodexClaudeRelay.Core.Protocol;
using CodexClaudeRelay.Desktop.Adapters;

namespace CodexClaudeRelay.Desktop;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions DisplayJsonOptions = HandoffJson.CreateSerializerOptions(writeIndented: true);
    private const string SmokeTestPrompt =
        "Run a relay transport smoke test only. Do not do repository work. Emit a minimal dad_handoff JSON object that tells the other role to acknowledge the smoke test and return one more minimal dad_handoff JSON object.";

    private readonly string _appDataDirectory;
    private readonly List<IAsyncDisposable> _ownedDisposables = [];
    private RelayBrokerOptions _brokerOptions = new();
    private RelayBroker? _broker;
    private IReadOnlyDictionary<string, AdapterStatus> _latestAdapterStatuses = new Dictionary<string, AdapterStatus>(StringComparer.Ordinal);
    private string _latestSmokeTestReport = "No smoke test has run yet.";
    private string _optionsLoadDiagnostic = "Built-in defaults active.";
    private bool _latestSmokeTestSucceeded;
    private bool _isBusy;
    private RelayPendingApproval? _livePendingApproval;
    private TaskCompletionSource<RelayApprovalDecision>? _pendingApprovalDecisionSource;
    private string? _currentAdmissionManifestPath;
    private bool _suppressUiSettingCallbacks;
    private readonly DispatcherTimer _statusRefreshTimer;
    private string? _relaySignalWatcherKey;

    public MainWindow()
    {
        InitializeComponent();

        _appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexClaudeRelayMvp");

        Directory.CreateDirectory(_appDataDirectory);

        _statusRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _statusRefreshTimer.Tick += StatusRefreshTimer_Tick;

        UseInteractiveAdaptersCheckBox.Checked += UseInteractiveAdaptersCheckBox_Changed;
        UseInteractiveAdaptersCheckBox.Unchecked += UseInteractiveAdaptersCheckBox_Changed;
        AutoApproveAllRequestsCheckBox.Checked += AutoApproveAllRequestsCheckBox_Changed;
        AutoApproveAllRequestsCheckBox.Unchecked += AutoApproveAllRequestsCheckBox_Changed;
        SessionIdTextBox.TextChanged += PersistUiSettingsTextChanged;
        InitialPromptTextBox.TextChanged += PersistUiSettingsTextChanged;
        WorkingDirectoryTextBox.TextChanged += PersistUiSettingsTextChanged;
        ManagedCardGameRootTextBox.TextChanged += PersistUiSettingsTextChanged;
        ManagedTaskSlugTextBox.TextChanged += PersistUiSettingsTextChanged;
        ManagedTurnsTextBox.TextChanged += PersistUiSettingsTextChanged;
        ManagedLoopSessionsTextBox.TextChanged += PersistUiSettingsTextChanged;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadUiSettings();
        NormalizeStaleRelaySignalsOnStartup();
        await EnsureBrokerAsync(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(SessionIdTextBox.Text))
        {
            SessionIdTextBox.Text = $"relay-{DateTime.Now:yyyyMMdd-HHmmss}";
        }

        if (string.IsNullOrWhiteSpace(InitialPromptTextBox.Text))
        {
            InitialPromptTextBox.Text = "Read PROJECT-RULES.md first. Start the relay session.";
        }
        _statusRefreshTimer.Start();
        await RefreshAdapterStatusAsync();
        await RefreshManagedStatusAsync(CancellationToken.None);
        RefreshUi();
        SaveUiSettings();
    }

    private async void UseInteractiveAdaptersCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressUiSettingCallbacks)
        {
            return;
        }

        await RunOperationAsync("Switching adapter runtime...", async () =>
        {
            _latestSmokeTestSucceeded = false;
            _latestSmokeTestReport = "No smoke test has run yet.";
            StatusMessageTextBlock.Text = GetRuntimeMode() == RelayRuntimeMode.Interactive
                ? "Switched to INTERACTIVE runtime. Codex now uses app-server. Claude now uses stream-json. Both are still experimental."
                : "Switched to NON_INTERACTIVE runtime.";
            await EnsureBrokerAsync(CancellationToken.None, recreate: true);
            await RefreshAdapterStatusAsync();
            SaveUiSettings();
        });
    }

    private async void AutoApproveAllRequestsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressUiSettingCallbacks)
        {
            return;
        }

        await RunOperationAsync("Updating approval mode...", async () =>
        {
            StatusMessageTextBlock.Text = IsAutoApproveAllRequestsEnabled()
                ? "Dangerous auto-approve mode enabled. Requests will be logged and auto-approved."
                : "Dangerous auto-approve mode disabled. Requests require broker policy or operator action.";
            await EnsureBrokerAsync(CancellationToken.None, recreate: true);
            await RefreshAdapterStatusAsync();
            SaveUiSettings();
        });
    }

    private void PersistUiSettingsTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressUiSettingCallbacks)
        {
            return;
        }

        SaveUiSettings();
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        _statusRefreshTimer.Stop();
        SaveUiSettings();
        MarkLiveRelaySignalAsClosed();
        await DisposeOwnedDisposablesAsync(CancellationToken.None);
    }

    private void StatusRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_broker is null)
        {
            return;
        }

        RefreshUi();
    }

    private async void StartSessionButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Starting relay session...", async () =>
        {
            await EnsureBrokerAsync(CancellationToken.None, recreate: true);
            await EnsureAdaptersReadyForSessionAsync();

            var sessionId = string.IsNullOrWhiteSpace(SessionIdTextBox.Text)
                ? $"relay-{DateTime.Now:yyyyMMdd-HHmmss}"
                : SessionIdTextBox.Text.Trim();

            var firstPrompt = string.IsNullOrWhiteSpace(InitialPromptTextBox.Text)
                ? "Read PROJECT-RULES.md first. Start the relay session."
                : InitialPromptTextBox.Text.Trim();

            await _broker!.StartSessionAsync(
                sessionId,
                AgentRole.Codex,
                firstPrompt,
                TryLoadSessionBootstrap(),
                CancellationToken.None);
        });
    }

    private async void AdvanceButton_Click(object sender, RoutedEventArgs e)
    {
        await AdvanceAsync(1);
    }

    private async void RunCycleButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTurnBatchWithPauseAsync(
            turns: 2,
            busyMessage: "Running one relay cycle...",
            successPauseReason: "Paused intentionally after one successful relay cycle.",
            successStatusMessage: "One relay cycle completed successfully. Session paused intentionally.");
    }

    private async void RunManagedCardGameButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Preparing and running a managed card-game relay slice...", async () =>
        {
            var managedCardGameRoot = string.IsNullOrWhiteSpace(ManagedCardGameRootTextBox.Text)
                ? @"D:\Unity\card game"
                : ManagedCardGameRootTextBox.Text.Trim();
            var managedTaskSlug = ManagedTaskSlugTextBox.Text?.Trim() ?? string.Empty;
            var turns = ParseManagedTurns();

            var prepared = await PrepareManagedCardGameSessionAsync(managedCardGameRoot, managedTaskSlug, CancellationToken.None);
            WorkingDirectoryTextBox.Text = managedCardGameRoot;
            SessionIdTextBox.Text = prepared.SessionId;
            InitialPromptTextBox.Text = prepared.Prompt;
            await EnsureBrokerAsync(CancellationToken.None, recreate: true);
            await EnsureAdaptersReadyForSessionAsync();

            await _broker!.StartSessionAsync(
                prepared.SessionId,
                AgentRole.Codex,
                prepared.Prompt,
                TryLoadSessionBootstrap(),
                CancellationToken.None);

            await RunTurnBatchCoreAsync(
                turns,
                successPauseReason: turns == 2
                    ? "Paused intentionally after one successful relay cycle."
                    : $"Paused intentionally after {turns} managed relay turns.",
                successStatusMessage: turns == 2
                    ? "Managed card-game relay completed one cycle and paused intentionally."
                    : $"Managed card-game relay completed {turns} turns and paused intentionally.");
            await RefreshManagedStatusAsync(CancellationToken.None);
        });
    }

    private async void RunManagedAutopilotButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Preparing and running an autopilot-managed relay slice...", async () =>
        {
            var managedCardGameRoot = string.IsNullOrWhiteSpace(ManagedCardGameRootTextBox.Text)
                ? @"D:\Unity\card game"
                : ManagedCardGameRootTextBox.Text.Trim();
            var turns = ParseManagedTurns();

            var autopilotPreparation = await PrepareManagedAutopilotSessionAsync(managedCardGameRoot, CancellationToken.None);
            await RefreshManagedStatusAsync(CancellationToken.None);

            if (!autopilotPreparation.ShouldStartRelay)
            {
                StatusMessageTextBlock.Text = autopilotPreparation.StatusMessage;
                return;
            }

            var prepared = await LoadPreparedManagedSessionAsync(CancellationToken.None);
            WorkingDirectoryTextBox.Text = managedCardGameRoot;
            SessionIdTextBox.Text = prepared.SessionId;
            InitialPromptTextBox.Text = prepared.Prompt;
            _currentAdmissionManifestPath = prepared.ManifestPath;
            SaveUiSettings();

            await EnsureBrokerAsync(CancellationToken.None, recreate: true);
            await EnsureAdaptersReadyForSessionAsync();

            await _broker!.StartSessionAsync(
                prepared.SessionId,
                AgentRole.Codex,
                prepared.Prompt,
                TryLoadSessionBootstrap(),
                CancellationToken.None);

            await RunTurnBatchCoreAsync(
                turns,
                successPauseReason: turns == 2
                    ? "Paused intentionally after one successful relay cycle."
                    : $"Paused intentionally after {turns} managed relay turns.",
                successStatusMessage: turns == 2
                    ? "Managed autopilot relay completed one cycle and paused intentionally."
                    : $"Managed autopilot relay completed {turns} turns and paused intentionally.");
            await RefreshManagedStatusAsync(CancellationToken.None);
        });
    }

    private async void RunManagedNextStepButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Executing the next managed desktop action...", async () =>
        {
            var managedCardGameRoot = string.IsNullOrWhiteSpace(ManagedCardGameRootTextBox.Text)
                ? @"D:\Unity\card game"
                : ManagedCardGameRootTextBox.Text.Trim();
            var turns = ParseManagedTurns();

            var result = await ExecuteManagedControllerCycleAsync(
                managedCardGameRoot,
                turns,
                CancellationToken.None);
            StatusMessageTextBlock.Text = result.Message;
            await RefreshManagedStatusAsync(CancellationToken.None);
        });
    }

    private async void RunManagedUntilAttentionButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Running the managed desktop controller until attention is required...", async () =>
        {
            var managedCardGameRoot = string.IsNullOrWhiteSpace(ManagedCardGameRootTextBox.Text)
                ? @"D:\Unity\card game"
                : ManagedCardGameRootTextBox.Text.Trim();
            var turns = ParseManagedTurns();

            for (var stepIndex = 1; stepIndex <= 24; stepIndex++)
            {
                var result = await ExecuteManagedControllerCycleAsync(
                    managedCardGameRoot,
                    turns,
                    CancellationToken.None);
                await RefreshManagedStatusAsync(CancellationToken.None);

                var managerSignal = await LoadManagerSignalAsync(managedCardGameRoot, CancellationToken.None);
                if (managerSignal is null)
                {
                    StatusMessageTextBlock.Text = $"Managed controller stopped after step {stepIndex}: managed status is not available.";
                    return;
                }

                if (!result.ShouldContinue || managerSignal.AttentionRequired || managerSignal.WaitShouldEnd)
                {
                    StatusMessageTextBlock.Text = $"Managed controller stopped after step {stepIndex}: {result.Message}";
                    return;
                }
            }

            StatusMessageTextBlock.Text = "Managed controller stopped after reaching its desktop safety limit. Refresh the manager signal and continue if needed.";
        });
    }

    private async void EasyStartButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Starting the easy operator path...", async () =>
        {
            var managedCardGameRoot = string.IsNullOrWhiteSpace(ManagedCardGameRootTextBox.Text)
                ? @"D:\Unity\card game"
                : ManagedCardGameRootTextBox.Text.Trim();
            var turns = ParseManagedTurns();

            await RefreshManagedStatusAsync(CancellationToken.None);

            for (var stepIndex = 1; stepIndex <= 24; stepIndex++)
            {
                var result = await ExecuteManagedControllerCycleAsync(
                    managedCardGameRoot,
                    turns,
                    CancellationToken.None);
                await RefreshManagedStatusAsync(CancellationToken.None);

                var managerSignal = await LoadManagerSignalAsync(managedCardGameRoot, CancellationToken.None);
                if (managerSignal is null)
                {
                    StatusMessageTextBlock.Text = "Easy Start stopped because the compact manager status is missing.";
                    return;
                }

                if (!result.ShouldContinue || managerSignal.AttentionRequired || managerSignal.WaitShouldEnd)
                {
                    StatusMessageTextBlock.Text = $"Easy Start stopped after step {stepIndex}: {result.Message}";
                    return;
                }
            }

            StatusMessageTextBlock.Text = "Easy Start stopped after reaching its desktop safety limit. Refresh the compact status and continue if needed.";
        });
    }

    private async void RunManagedAutopilotLoopButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Running a managed autopilot desktop loop...", async () =>
        {
            var managedCardGameRoot = string.IsNullOrWhiteSpace(ManagedCardGameRootTextBox.Text)
                ? @"D:\Unity\card game"
                : ManagedCardGameRootTextBox.Text.Trim();
            var turns = ParseManagedTurns();
            var loopSessions = ParseManagedLoopSessions();

            for (var sessionIndex = 1; sessionIndex <= loopSessions; sessionIndex++)
            {
                var result = await ExecuteManagedControllerCycleAsync(
                    managedCardGameRoot,
                    turns,
                    CancellationToken.None);
                await RefreshManagedStatusAsync(CancellationToken.None);

                if (!result.ShouldContinue)
                {
                    StatusMessageTextBlock.Text = $"Managed autopilot loop stopped after step {sessionIndex}: {result.Message}";
                    return;
                }
            }

            StatusMessageTextBlock.Text = $"Managed autopilot loop finished {loopSessions} session(s).";
        });
    }

    private async void RefreshManagedStatusButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Refreshing managed autopilot status...", async () =>
        {
            await RefreshManagedStatusAsync(CancellationToken.None);
            StatusMessageTextBlock.Text = "Managed autopilot status refreshed.";
        });
    }

    private async void CheckAdaptersButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Checking Codex and Claude adapters...", async () =>
        {
            await EnsureBrokerAsync(CancellationToken.None, recreate: true);
            await RefreshAdapterStatusAsync();
        });
    }

    private async void AutoRunButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTurnBatchWithPauseAsync(
            turns: 4,
            busyMessage: "Running four relay turns...",
            successPauseReason: "Paused intentionally after four successful relay turns.",
            successStatusMessage: "Four relay turns completed successfully. Session paused intentionally.");
    }

    private async void SmokeTestButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Smoke test started. Preparing adapters...", async () =>
        {
            await EnsureBrokerAsync(CancellationToken.None, recreate: true);
            await EnsureAdaptersReadyForSessionAsync();
            _latestSmokeTestSucceeded = false;
            _latestSmokeTestReport = "Smoke test started.";
            StatusMessageTextBlock.Text = "Smoke test started. Adapter checks passed. Creating a temporary smoke session...";
            RefreshUi();
            await YieldToUiAsync();

            var baseSessionId = string.IsNullOrWhiteSpace(SessionIdTextBox.Text)
                ? "smoke"
                : SessionIdTextBox.Text.Trim();
            var sessionId = $"{baseSessionId}-smoke-{DateTime.Now:yyyyMMdd-HHmmss}";

            await _broker!.StartSessionAsync(sessionId, AgentRole.Codex, SmokeTestPrompt, CancellationToken.None);

            for (var i = 0; i < 2; i++)
            {
                var turnNumber = i + 1;
                SetBusyState(true, $"Smoke test running turn {turnNumber} of 2 on {_broker.State.ActiveAgent}...");
                StatusMessageTextBlock.Text = $"Smoke test running turn {turnNumber} of 2 on {_broker.State.ActiveAgent}...";
                RefreshUi();
                await YieldToUiAsync();
                var result = await _broker.AdvanceAsync(CancellationToken.None);
                StatusMessageTextBlock.Text = result.Message;
                RefreshUi();

                if (!result.Succeeded)
                {
                    SetBusyState(true, $"Smoke test stopped on turn {turnNumber}. Final status: {result.Message}");
                    StatusMessageTextBlock.Text = $"Smoke test stopped on turn {turnNumber}. {result.Message}";
                    _latestSmokeTestReport = BuildSmokeTestReport(
                        sessionId,
                        false,
                        result.Message,
                        _broker.CurrentLogPath,
                        _broker.State);
                    return;
                }
            }

            await _broker.PauseAsync("Smoke test completed after two successful relay turns.", CancellationToken.None);
            _latestSmokeTestSucceeded = true;
            _latestSmokeTestReport = BuildSmokeTestReport(
                sessionId,
                true,
                "Two relay turns completed successfully. Session paused intentionally.",
                _broker.CurrentLogPath,
                _broker.State);
            StatusMessageTextBlock.Text = "Smoke test completed. Session paused after two successful relay turns.";
        });
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Pausing relay session...", async () =>
        {
            if (_broker is not null)
            {
                await _broker.PauseAsync("Paused by user.", CancellationToken.None);
            }
        });
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Stopping relay session...", async () =>
        {
            if (_broker is not null)
            {
                await _broker.StopAsync("Stopped by user.", CancellationToken.None);
            }
        });
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _appDataDirectory,
            UseShellExecute = true,
        });
    }

    private async void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Exporting diagnostics...", async () =>
        {
            var exportPath = await ExportDiagnosticsAsync(CancellationToken.None);
            StatusMessageTextBlock.Text = $"Diagnostics exported to {exportPath}";
        });
    }

    private async Task AdvanceAsync(int turns)
    {
        await RunOperationAsync(turns == 1 ? "Advancing one relay turn..." : $"Advancing {turns} relay turns...", async () =>
        {
            for (var i = 0; i < turns; i++)
            {
                if (_broker is null)
                {
                    StatusMessageTextBlock.Text = "Broker is not initialized.";
                    break;
                }

                SetBusyState(true, turns == 1
                    ? $"Advancing turn {_broker.State.CurrentTurn} on {_broker.State.ActiveAgent}..."
                    : $"Advancing step {i + 1} of {turns} on {_broker.State.ActiveAgent}...");
                StatusMessageTextBlock.Text = turns == 1
                    ? $"Advancing turn {_broker.State.CurrentTurn} on {_broker.State.ActiveAgent}..."
                    : $"Advancing step {i + 1} of {turns} on {_broker.State.ActiveAgent}...";
                RefreshUi();
                await YieldToUiAsync();
                var result = await _broker.AdvanceAsync(CancellationToken.None);
                StatusMessageTextBlock.Text = result.Message;
                RefreshUi();

                if (!result.Succeeded)
                {
                    SetBusyState(true, $"Advance stopped. Final status: {result.Message}");
                    break;
                }
            }
        });
    }

    private async Task RunTurnBatchWithPauseAsync(
        int turns,
        string busyMessage,
        string successPauseReason,
        string successStatusMessage)
    {
        await RunOperationAsync(busyMessage, async () =>
            await RunTurnBatchCoreAsync(turns, successPauseReason, successStatusMessage));
    }

    private async Task RunTurnBatchCoreAsync(
        int turns,
        string successPauseReason,
        string successStatusMessage)
    {
        for (var i = 0; i < turns; i++)
        {
            if (_broker is null)
            {
                StatusMessageTextBlock.Text = "Broker is not initialized.";
                return;
            }

            var turnNumber = i + 1;
            SetBusyState(true, $"Running turn {turnNumber} of {turns} on {_broker.State.ActiveAgent}...");
            StatusMessageTextBlock.Text = $"Running turn {turnNumber} of {turns} on {_broker.State.ActiveAgent}...";
            RefreshUi();
            await YieldToUiAsync();
            var result = await _broker.AdvanceAsync(CancellationToken.None);
            StatusMessageTextBlock.Text = result.Message;
            RefreshUi();

            if (!result.Succeeded)
            {
                SetBusyState(true, $"Run stopped on turn {turnNumber}. Final status: {result.Message}");
                return;
            }
        }

        if (_broker is not null)
        {
            await _broker.PauseAsync(successPauseReason, CancellationToken.None);
            StatusMessageTextBlock.Text = successStatusMessage;
            RefreshUi();
        }
    }

    private async Task RunOperationAsync(string busyMessage, Func<Task> operation)
    {
        try
        {
            SetBusyState(true, busyMessage);
            await YieldToUiAsync();
            await operation();
        }
        catch (Exception ex)
        {
            StatusMessageTextBlock.Text = ex.Message;
        }
        finally
        {
            SetBusyState(false);
            RefreshUi();
        }
    }

    private void RefreshUi()
    {
        if (_broker is null)
        {
            StateSummaryTextBlock.Text = $"Runtime: {GetRuntimeModeLabel()}{Environment.NewLine}Broker not initialized.";
            SessionRiskBadgeTextBlock.Text = "Risk: none";
            AdapterStatusTextBlock.Text = "Status not checked yet.";
            SmokeTestReportTextBlock.Text = _latestSmokeTestReport;
            LatestHandoffTextBox.Text = "No handoff accepted yet.";
            LatestApprovalTextBox.Text = "No approval activity yet.";
            ApprovalQueueTextBox.Text = "No approvals queued yet.";
            LatestToolActivityTextBox.Text = "No tool activity yet.";
            LatestGitActivityTextBox.Text = "No git or PR activity yet.";
            ToolCategorySummaryTextBox.Text = "No categorized tool activity yet.";
            PolicyGapSummaryTextBox.Text = "No policy-gap advisories yet.";
            CurrentSessionRiskSummaryTextBox.Text = "No elevated session risk indicators.";
            SessionApprovalRulesTextBox.Text = "No saved session approval rules.";
            CurrentLogPathTextBlock.Text = "(no log file yet)";
            RecentEventsTextBox.Text = "No recent events.";
            EventLogTextBox.Text = "No event log written yet.";
            ApplyVisualStates();
            WriteAutomaticLogArtifacts();
            return;
        }

        var state = _broker.State;
        var activePendingApproval = _livePendingApproval ?? state.PendingApproval;
        SessionRiskBadgeTextBlock.Text = $"Risk: {BuildSessionRiskBadgeText(state, activePendingApproval, IsAutoApproveAllRequestsEnabled())}";
        var now = DateTimeOffset.Now;
        StateSummaryTextBlock.Text =
            $"Runtime: {GetRuntimeModeLabel()}{Environment.NewLine}" +
            $"Auto-approve all approvals: {(IsAutoApproveAllRequestsEnabled() ? "enabled" : "disabled")}{Environment.NewLine}" +
            $"Codex approval path: {GetApprovalPathSummary(AgentRole.Codex)}{Environment.NewLine}" +
            $"Claude approval path: {GetApprovalPathSummary(AgentRole.Claude)}{Environment.NewLine}" +
            $"Session: {state.SessionId}{Environment.NewLine}" +
            $"Status: {state.Status}{Environment.NewLine}" +
            $"Active role: {state.ActiveAgent}{Environment.NewLine}" +
            $"Current turn: {state.CurrentTurn}{Environment.NewLine}" +
            $"Repair attempts: {state.RepairAttempts}{Environment.NewLine}" +
            $"Session age: {(now - state.SessionStartedAt):mm\\:ss}{Environment.NewLine}" +
            $"Last progress at: {state.UpdatedAt:O}{Environment.NewLine}" +
            $"Last progress age: {FormatElapsed(now - state.UpdatedAt)}{Environment.NewLine}" +
            $"Turn watchdog: {FormatTurnWatchdog(state, now, _broker.Options.PerTurnTimeout)}{Environment.NewLine}" +
            $"Rotations: {state.RotationCount}{Environment.NewLine}" +
            $"Total input tokens: {state.TotalInputTokens}{Environment.NewLine}" +
            $"Total output tokens: {state.TotalOutputTokens} / {_broker.Options.MaxCumulativeOutputTokens}{Environment.NewLine}" +
            $"Total cache read: {state.TotalCacheReadInputTokens}{Environment.NewLine}" +
            $"Total cache create: {state.TotalCacheCreationInputTokens}{Environment.NewLine}" +
            $"Total cost (mixed): ${state.TotalCostUsd:F4}{Environment.NewLine}" +
            $"Claude est. cost: ${state.TotalCostClaudeUsd:F4} (CLI-estimated){Environment.NewLine}" +
            $"Codex est. cost: ${state.TotalCostCodexUsd:F4} (local rate card){Environment.NewLine}" +
            $"Claude cost ceiling: {FormatClaudeCostCeiling(state, _broker.Options)}{Environment.NewLine}" +
            $"Options: {_optionsLoadDiagnostic}{Environment.NewLine}" +
            $"Claude low-cache turns: {GetLowCacheTurns(state, AgentRole.Claude)}{Environment.NewLine}" +
            $"Last budget signal: {state.LastBudgetSignal ?? "(none)"}{Environment.NewLine}" +
            $"Claude usage: {FormatUsage(state, AgentRole.Claude)}{Environment.NewLine}" +
            $"Codex usage: {FormatUsage(state, AgentRole.Codex)}{Environment.NewLine}" +
            $"Codex handle: {GetSessionHandle(state, AgentRole.Codex)}{Environment.NewLine}" +
            $"Claude handle: {GetSessionHandle(state, AgentRole.Claude)}{Environment.NewLine}" +
            $"Pending prompt: {Shorten(state.PendingPrompt)}{Environment.NewLine}" +
            $"Session approval rules: {state.SessionApprovalRules.Count}{Environment.NewLine}" +
            $"Approval queue entries: {state.ApprovalQueue.Count}{Environment.NewLine}" +
            $"Policy gap advisories: {state.PolicyGapAdvisoriesFired.Count}{Environment.NewLine}" +
            $"Pending approval: {FormatPendingApproval(activePendingApproval)}{Environment.NewLine}" +
            $"Last error: {state.LastError ?? "(none)"}";

        SmokeTestReportTextBlock.Text = _latestSmokeTestReport;
        LatestHandoffTextBox.Text = state.LastHandoff is null
            ? "No handoff accepted yet."
            : JsonSerializer.Serialize(state.LastHandoff, DisplayJsonOptions);

        CurrentLogPathTextBlock.Text = string.IsNullOrWhiteSpace(_broker.CurrentLogPath)
            ? "(no log file yet)"
            : _broker.CurrentLogPath;

        var logPath = _broker.CurrentLogPath;
        LatestApprovalTextBox.Text = BuildLatestApprovalSummary(state, activePendingApproval, logPath);
        ApprovalQueueTextBox.Text = BuildApprovalQueueSummary(state);
        LatestToolActivityTextBox.Text = BuildLatestToolActivitySummary(logPath);
        LatestGitActivityTextBox.Text = BuildLatestGitActivitySummary(logPath);
        ToolCategorySummaryTextBox.Text = BuildToolCategorySummary(logPath);
        PolicyGapSummaryTextBox.Text = BuildPolicyGapSummary(logPath);
        CurrentSessionRiskSummaryTextBox.Text = BuildCurrentSessionRiskSummary(state, activePendingApproval, IsAutoApproveAllRequestsEnabled());
        SessionApprovalRulesTextBox.Text = BuildSessionApprovalRulesSummary(state, activePendingApproval);
        RecentEventsTextBox.Text = BuildRecentEventsSummary(logPath);
        EventLogTextBox.Text = BuildEventLogTailSummary(logPath);
        ApplyVisualStates();
        WriteAutomaticLogArtifacts();
    }

    private async Task EnsureAdaptersReadyForSessionAsync()
    {
        if (_broker is null)
        {
            return;
        }

        var statuses = await GetAdapterStatusesWithTimeoutAsync(CancellationToken.None);
        SetAdapterStatuses(statuses);

        var unhealthy = statuses.Where(pair => pair.Value.Health != RelayHealthStatus.Healthy).ToArray();
        if (unhealthy.Length == 0)
        {
            return;
        }

        var details = string.Join(
            Environment.NewLine,
            unhealthy.Select(pair => $"{pair.Key}: {pair.Value.Message}"));
        throw new InvalidOperationException($"Cannot start the relay session until all adapters are healthy.{Environment.NewLine}{details}");
    }

    private async Task RefreshAdapterStatusAsync()
    {
        if (_broker is null)
        {
            AdapterStatusTextBlock.Text = "Broker not initialized.";
            _latestAdapterStatuses = new Dictionary<string, AdapterStatus>(StringComparer.Ordinal);
            ApplyVisualStates();
            return;
        }

        try
        {
            AdapterStatusTextBlock.Text = "Checking adapter health...";
            ApplyVisualStates();
            await YieldToUiAsync();
            var statuses = await GetAdapterStatusesWithTimeoutAsync(CancellationToken.None);
            SetAdapterStatuses(statuses);
        }
        catch (Exception ex)
        {
            _latestAdapterStatuses = new Dictionary<string, AdapterStatus>(StringComparer.Ordinal);
            AdapterStatusTextBlock.Text = ex.Message;
            ApplyVisualStates();
        }
    }

    private static string Shorten(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        return text.Length <= 120 ? text : $"{text[..120]}...";
    }

    private static string GetSessionHandle(RelaySessionState state, string role)
    {
        return state.NativeSessionHandles.TryGetValue(role.ToString(), out var handle)
            ? handle
            : "(none)";
    }

    private static string FormatUsage(RelaySessionState state, string role)
    {
        if (!state.LastUsageBySide.TryGetValue(role.ToString(), out var usage))
        {
            return "(none)";
        }

        return
            $"in={usage.InputTokens?.ToString() ?? "?"}, out={usage.OutputTokens?.ToString() ?? "?"}, " +
            $"cache_read={usage.CacheReadInputTokens?.ToString() ?? "?"}, cache_create={usage.CacheCreationInputTokens?.ToString() ?? "?"}, " +
            $"cost=${usage.CostUsd?.ToString("F4") ?? "?"}, model={usage.Model ?? "unknown"}, auth={usage.AuthMethod ?? "unknown"}";
    }

    private static string FormatPendingApproval(RelayPendingApproval? pendingApproval)
    {
        if (pendingApproval is null)
        {
            return "(none)";
        }

        return $"{pendingApproval.Role} | {pendingApproval.Title} | {pendingApproval.Category} | risk={pendingApproval.RiskLevel} | {Shorten(pendingApproval.Message)}";
    }

    private static string FormatClaudeCostCeiling(RelaySessionState state, RelayBrokerOptions options)
    {
        if (!options.MaxClaudeCostUsd.HasValue)
        {
            return "(disabled)";
        }

        if (!state.LastUsageBySide.TryGetValue(AgentRole.Claude.ToString(), out var usage) ||
            string.IsNullOrWhiteSpace(usage.AuthMethod))
        {
            return $"${options.MaxClaudeCostUsd.Value:F2} (awaiting auth signal)";
        }

        return ClaudeAuthMethod.IsApiKey(usage.AuthMethod)
            ? $"${options.MaxClaudeCostUsd.Value:F2} (active; api-key, per-segment)"
            : $"${options.MaxClaudeCostUsd.Value:F2} (inactive; auth not api-key: {usage.AuthMethod})";
    }

    private static int GetLowCacheTurns(RelaySessionState state, string role) =>
        state.ConsecutiveLowCacheTurnsBySide.TryGetValue(role.ToString(), out var count)
            ? count
            : 0;

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
    }

    private static string FormatTurnWatchdog(RelaySessionState state, DateTimeOffset now, TimeSpan perTurnTimeout)
    {
        if (state.Status != RelaySessionStatus.Active)
        {
            return $"inactive ({state.Status})";
        }

        if (perTurnTimeout <= TimeSpan.Zero)
        {
            return "disabled";
        }

        var elapsed = now - state.UpdatedAt;
        var remaining = perTurnTimeout - elapsed;
        if (remaining < TimeSpan.Zero)
        {
            return $"expired {FormatElapsed(-remaining)} ago";
        }

        return $"{FormatElapsed(remaining)} remaining";
    }

    private void SetAdapterStatuses(IReadOnlyDictionary<string, AdapterStatus> statuses)
    {
        _latestAdapterStatuses = statuses;
        if (statuses.Count == 0)
        {
            AdapterStatusTextBlock.Text = $"Checked at {DateTimeOffset.Now:O}{Environment.NewLine}(no adapters reported)";
            ApplyVisualStates();
            return;
        }

        AdapterStatusTextBlock.Text = string.Join(
            Environment.NewLine,
            new[]
            {
                $"Checked at {DateTimeOffset.Now:O}"
            }.Concat(statuses.Select(pair =>
                $"{pair.Key}: {pair.Value.Health} | Auth={(pair.Value.IsAuthenticated ? "yes" : "no")} | {pair.Value.Message}")));
        ApplyVisualStates();
    }

    private void ApplyVisualStates()
    {
        ApplyBusyVisual();
        ApplyAdapterStatusVisual();
        ApplySessionStatusVisual();
        ApplyMessageVisual();
        ApplySmokeTestVisual();
        ApplyApprovalVisual();
        ApplyPolicyGapVisual();
        ApplyRiskSummaryVisual();
        ApplySessionRiskBadgeVisual();
    }

    private void ApplyAdapterStatusVisual()
    {
        var aggregate = GetAggregateHealth();
        AdapterStatusBorder.Background = CreateBrush(aggregate switch
        {
            RelayHealthStatus.Healthy => "#ECFDF3",
            RelayHealthStatus.Degraded => "#FFF7ED",
            RelayHealthStatus.Unavailable => "#FEF3F2",
            _ => "#FFF8FAFC",
        });
        AdapterStatusBorder.BorderBrush = CreateBrush(aggregate switch
        {
            RelayHealthStatus.Healthy => "#A6F4C5",
            RelayHealthStatus.Degraded => "#FED7AA",
            RelayHealthStatus.Unavailable => "#FECDCA",
            _ => "#FFD0D7DE",
        });
    }

    private void ApplySessionStatusVisual()
    {
        var status = _broker?.State.Status ?? RelaySessionStatus.Idle;
        var hasBudgetSignal = !string.IsNullOrWhiteSpace(_broker?.State.LastBudgetSignal);
        StateSummaryBorder.Background = CreateBrush(hasBudgetSignal
            ? "#7F1D1D"
            : status switch
        {
            RelaySessionStatus.Active => "#0B3B2E",
            RelaySessionStatus.AwaitingApproval => "#1D2939",
            RelaySessionStatus.Paused => "#713F12",
            RelaySessionStatus.Stopped => "#7F1D1D",
            RelaySessionStatus.Failed => "#7F1D1D",
            _ => "#FF101828",
        });
        StateSummaryBorder.BorderBrush = CreateBrush(hasBudgetSignal
            ? "#B42318"
            : status switch
        {
            RelaySessionStatus.Active => "#166534",
            RelaySessionStatus.AwaitingApproval => "#475467",
            RelaySessionStatus.Paused => "#B45309",
            RelaySessionStatus.Stopped => "#B42318",
            RelaySessionStatus.Failed => "#B42318",
            _ => "#FF101828",
        });
    }

    private void ApplyMessageVisual()
    {
        var message = StatusMessageTextBlock.Text ?? string.Empty;
        var isError = message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                      message.Contains("cannot start", StringComparison.OrdinalIgnoreCase) ||
                      message.Contains("error", StringComparison.OrdinalIgnoreCase);
        var isWarning = !isError &&
                        (message.Contains("paused", StringComparison.OrdinalIgnoreCase) ||
                         message.Contains("repair", StringComparison.OrdinalIgnoreCase) ||
                         message.Contains("warning", StringComparison.OrdinalIgnoreCase));

        StatusMessageBorder.Background = CreateBrush(isError
            ? "#FEF3F2"
            : isWarning
                ? "#FFF7ED"
                : "#ECFDF3");
        StatusMessageBorder.BorderBrush = CreateBrush(isError
            ? "#FECDCA"
            : isWarning
                ? "#FED7AA"
                : "#A6F4C5");
    }

    private void ApplySmokeTestVisual()
    {
        var isEmpty = string.Equals(_latestSmokeTestReport, "No smoke test has run yet.", StringComparison.Ordinal);
        SmokeTestReportBorder.Background = CreateBrush(isEmpty
            ? "#FFF8FAFC"
            : _latestSmokeTestSucceeded
                ? "#ECFDF3"
                : "#FEF3F2");
        SmokeTestReportBorder.BorderBrush = CreateBrush(isEmpty
            ? "#FFD0D7DE"
            : _latestSmokeTestSucceeded
                ? "#A6F4C5"
                : "#FECDCA");
    }

    private void ApplyApprovalVisual()
    {
        var pendingApproval = _livePendingApproval ?? _broker?.State.PendingApproval;
        if (pendingApproval is null)
        {
            LatestApprovalTextBox.Background = CreateBrush("#FFF8FAFC");
            LatestApprovalTextBox.BorderBrush = CreateBrush("#FFD0D7DE");
            return;
        }

        var (background, border) = GetRiskPalette(pendingApproval.RiskLevel);
        LatestApprovalTextBox.Background = CreateBrush(background);
        LatestApprovalTextBox.BorderBrush = CreateBrush(border);
    }

    private void ApplyPolicyGapVisual()
    {
        var hasPolicyGap = (_broker?.State.PolicyGapAdvisoriesFired.Count ?? 0) > 0;
        if (!hasPolicyGap)
        {
            PolicyGapSummaryTextBox.Background = CreateBrush("#FFF8FAFC");
            PolicyGapSummaryTextBox.BorderBrush = CreateBrush("#FFD0D7DE");
            return;
        }

        PolicyGapSummaryTextBox.Background = CreateBrush("#FFF7ED");
        PolicyGapSummaryTextBox.BorderBrush = CreateBrush("#FED7AA");
    }

    private void ApplyRiskSummaryVisual()
    {
        var summary = CurrentSessionRiskSummaryTextBox.Text ?? string.Empty;
        if (summary.Contains("Critical", StringComparison.OrdinalIgnoreCase))
        {
            CurrentSessionRiskSummaryTextBox.Background = CreateBrush("#FEF3F2");
            CurrentSessionRiskSummaryTextBox.BorderBrush = CreateBrush("#FECDCA");
            return;
        }

        if (summary.Contains("High", StringComparison.OrdinalIgnoreCase) ||
            summary.Contains("Policy gap", StringComparison.OrdinalIgnoreCase))
        {
            CurrentSessionRiskSummaryTextBox.Background = CreateBrush("#FFF7ED");
            CurrentSessionRiskSummaryTextBox.BorderBrush = CreateBrush("#FED7AA");
            return;
        }

        if (summary.Contains("Medium", StringComparison.OrdinalIgnoreCase))
        {
            CurrentSessionRiskSummaryTextBox.Background = CreateBrush("#EFF8FF");
            CurrentSessionRiskSummaryTextBox.BorderBrush = CreateBrush("#B2DDFF");
            return;
        }

        CurrentSessionRiskSummaryTextBox.Background = CreateBrush("#ECFDF3");
        CurrentSessionRiskSummaryTextBox.BorderBrush = CreateBrush("#A6F4C5");
    }

    private void ApplySessionRiskBadgeVisual()
    {
        var riskText = SessionRiskBadgeTextBlock.Text ?? string.Empty;
        var risk = riskText.Replace("Risk:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        var (background, border, foreground) = risk switch
        {
            "critical" => ("#FEF3F2", "#FECDCA", "#FFB42318"),
            "high" => ("#FFF7ED", "#FED7AA", "#FFB54708"),
            "medium" => ("#EFF8FF", "#B2DDFF", "#FF175CD3"),
            "low" => ("#ECFDF3", "#A6F4C5", "#FF027A48"),
            _ => ("#FFF8FAFC", "#FFD0D7DE", "#FF344054")
        };

        SessionRiskBadgeBorder.Background = CreateBrush(background);
        SessionRiskBadgeBorder.BorderBrush = CreateBrush(border);
        SessionRiskBadgeTextBlock.Foreground = CreateBrush(foreground);
    }

    private void ApplyBusyVisual()
    {
        var approvalPending = _pendingApprovalDecisionSource is not null || _broker?.State.PendingApproval is not null;
        var hasSavedSessionApprovals = _broker?.State.SessionApprovalRules.Count > 0;
        BusyBorder.Visibility = _isBusy ? Visibility.Visible : Visibility.Collapsed;
        StartSessionButton.IsEnabled = !_isBusy;
        CheckAdaptersButton.IsEnabled = !_isBusy;
        SmokeTestButton.IsEnabled = !_isBusy;
        AdvanceButton.IsEnabled = !_isBusy;
        RunCycleButton.IsEnabled = !_isBusy;
        RunManagedCardGameButton.IsEnabled = !_isBusy;
        RunManagedAutopilotButton.IsEnabled = !_isBusy;
        RunManagedNextStepButton.IsEnabled = !_isBusy;
        RunManagedUntilAttentionButton.IsEnabled = !_isBusy;
        RunManagedAutopilotLoopButton.IsEnabled = !_isBusy;
        EasyStartButton.IsEnabled = !_isBusy;
        RefreshManagedStatusButton.IsEnabled = !_isBusy;
        AutoRunButton.IsEnabled = !_isBusy;
        PauseButton.IsEnabled = !_isBusy;
        StopButton.IsEnabled = !_isBusy;
        ExportDiagnosticsButton.IsEnabled = !_isBusy;
        OpenLogFolderButton.IsEnabled = true;
        ApproveOnceButton.IsEnabled = approvalPending;
        ApproveSessionButton.IsEnabled = approvalPending;
        DenyApprovalButton.IsEnabled = approvalPending;
        CancelApprovalButton.IsEnabled = approvalPending;
        ClearSessionApprovalsButton.IsEnabled = !_isBusy && !approvalPending && hasSavedSessionApprovals;
    }

    private RelayHealthStatus GetAggregateHealth()
    {
        if (_latestAdapterStatuses.Count == 0)
        {
            return RelayHealthStatus.Unknown;
        }

        if (_latestAdapterStatuses.Values.Any(status => status.Health == RelayHealthStatus.Unavailable))
        {
            return RelayHealthStatus.Unavailable;
        }

        if (_latestAdapterStatuses.Values.Any(status => status.Health == RelayHealthStatus.Degraded))
        {
            return RelayHealthStatus.Degraded;
        }

        if (_latestAdapterStatuses.Values.All(status => status.Health == RelayHealthStatus.Healthy))
        {
            return RelayHealthStatus.Healthy;
        }

        return RelayHealthStatus.Unknown;
    }

    private static Brush CreateBrush(string hexColor) => (SolidColorBrush)new BrushConverter().ConvertFrom(hexColor)!;

    private static (string Background, string Border) GetRiskPalette(string? riskLevel) => riskLevel switch
    {
        "low" => ("#ECFDF3", "#A6F4C5"),
        "medium" => ("#EFF8FF", "#B2DDFF"),
        "high" => ("#FFF7ED", "#FED7AA"),
        "critical" => ("#FEF3F2", "#FECDCA"),
        _ => ("#FFF8FAFC", "#FFD0D7DE")
    };

    private static string BuildRecentEventsSummary(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return "No recent events.";
        }

        var lines = File.ReadLines(logPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(8)
            .ToArray();

        if (lines.Length == 0)
        {
            return "No recent events.";
        }

        var events = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            try
            {
                var logEvent = JsonSerializer.Deserialize<RelayLogEvent>(line, HandoffJson.SerializerOptions);
                if (logEvent is null)
                {
                    continue;
                }

                var role = logEvent.Role?.ToString() ?? "-";
                events.Add(
                    $"{logEvent.Timestamp:HH:mm:ss} | {logEvent.EventType} | {role} | {Shorten(logEvent.Message, 96)}");
            }
            catch
            {
                events.Add(Shorten(line, 120));
            }
        }

        return string.Join(Environment.NewLine, events);
    }

    private static string BuildLatestApprovalSummary(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return "No approval activity yet.";
        }

        var events = File.ReadLines(logPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                try
                {
                    return JsonSerializer.Deserialize<RelayLogEvent>(line, HandoffJson.SerializerOptions);
                }
                catch
                {
                    return null;
                }
            })
            .Where(logEvent => logEvent is not null)
            .Select(logEvent => logEvent!)
            .Where(logEvent =>
                string.Equals(logEvent.EventType, "approval.auto_mode.applied", StringComparison.Ordinal) ||
                string.Equals(logEvent.EventType, "policy.applied", StringComparison.Ordinal) ||
                string.Equals(logEvent.EventType, "approval.requested", StringComparison.Ordinal) ||
                string.Equals(logEvent.EventType, "approval.granted", StringComparison.Ordinal) ||
                string.Equals(logEvent.EventType, "approval.denied", StringComparison.Ordinal) ||
                string.Equals(logEvent.EventType, "approval.session_rule.saved", StringComparison.Ordinal) ||
                string.Equals(logEvent.EventType, "approval.session_rule.applied", StringComparison.Ordinal) ||
                string.Equals(logEvent.EventType, "approval.session_rules.cleared", StringComparison.Ordinal))
            .ToArray();

        if (events.Length == 0)
        {
            return "No approval activity yet.";
        }

        var latest = events[^1];
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"{latest.Timestamp:HH:mm:ss} | {latest.EventType} | {latest.Role?.ToString() ?? "-"}");
        builder.AppendLine(latest.Message);
        if (!string.IsNullOrWhiteSpace(latest.Payload))
        {
            builder.AppendLine();
            builder.AppendLine(latest.Payload);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildLatestApprovalSummary(RelaySessionState state, RelayPendingApproval? pendingApproval, string logPath)
    {
        if (pendingApproval is not null)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(CodexClaudeRelay.Core.Policy.ApprovalRiskExplainer.Render(pendingApproval));
            builder.AppendLine($"{pendingApproval.CreatedAt:HH:mm:ss} | pending | {pendingApproval.Role} | {pendingApproval.Title} | {pendingApproval.Category} | risk={pendingApproval.RiskLevel}");
            builder.AppendLine(pendingApproval.Message);
            if (RelayApprovalPolicy.TryResolveSessionDecision(state, pendingApproval, out var savedDecision, out var matchedRule) &&
                matchedRule is not null)
            {
                builder.AppendLine();
                builder.AppendLine("Saved session rule match:");
                builder.AppendLine($"{matchedRule.CreatedAt:HH:mm:ss} | {matchedRule.Title} | {matchedRule.Category} | risk={matchedRule.RiskLevel} | {savedDecision} | {matchedRule.PolicyKey}");
            }

            if (!string.IsNullOrWhiteSpace(pendingApproval.Payload))
            {
                builder.AppendLine();
                builder.AppendLine(pendingApproval.Payload);
            }

            return builder.ToString().TrimEnd();
        }

        return BuildLatestApprovalSummary(logPath);
    }

    private static string BuildSessionApprovalRulesSummary(RelaySessionState state, RelayPendingApproval? pendingApproval)
    {
        RelaySessionApprovalRule? matchedRule = null;
        RelayApprovalDecision matchedDecision = default;
        if (pendingApproval is not null)
        {
            RelayApprovalPolicy.TryResolveSessionDecision(state, pendingApproval, out matchedDecision, out matchedRule);
        }

        if (state.SessionApprovalRules.Count == 0)
        {
            return "No saved session approval rules.";
        }

        var builder = new System.Text.StringBuilder();
        if (matchedRule is not null)
        {
            builder.AppendLine("Active pending approval matches this saved rule:");
            builder.AppendLine($"{matchedRule.CreatedAt:HH:mm:ss} | {matchedRule.Title} | {matchedRule.Category} | risk={matchedRule.RiskLevel} | {matchedDecision} | {matchedRule.PolicyKey}");
            builder.AppendLine();
            builder.AppendLine("All saved rules:");
        }

        foreach (var rule in state.SessionApprovalRules.OrderBy(rule => rule.CreatedAt))
        {
            var marker = matchedRule is not null &&
                         string.Equals(rule.PolicyKey, matchedRule.PolicyKey, StringComparison.Ordinal) &&
                         rule.CreatedAt.Equals(matchedRule.CreatedAt)
                ? "* "
                : "  ";
            builder.AppendLine($"{marker}{rule.CreatedAt:HH:mm:ss} | {rule.Title} | {rule.Category} | risk={rule.RiskLevel} | {rule.Decision} | {rule.PolicyKey}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildApprovalQueueSummary(RelaySessionState state)
    {
        if (state.ApprovalQueue.Count == 0)
        {
            return "No approvals queued yet.";
        }

        var pendingCount = state.ApprovalQueue.Count(item => string.Equals(item.Status, "pending", StringComparison.Ordinal));
        var approvedCount = state.ApprovalQueue.Count(item =>
            string.Equals(item.Status, "approved_once", StringComparison.Ordinal) ||
            string.Equals(item.Status, "approved_session", StringComparison.Ordinal));
        var deniedCount = state.ApprovalQueue.Count(item => string.Equals(item.Status, "denied", StringComparison.Ordinal));
        var expiredCount = state.ApprovalQueue.Count(item => string.Equals(item.Status, "expired", StringComparison.Ordinal));

        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"Pending: {pendingCount}");
        builder.AppendLine($"Approved: {approvedCount}");
        builder.AppendLine($"Denied: {deniedCount}");
        builder.AppendLine($"Expired: {expiredCount}");
        builder.AppendLine();
        builder.AppendLine("Recent:");

        foreach (var item in state.ApprovalQueue
                     .OrderByDescending(queueItem => queueItem.CreatedAt)
                     .Take(8))
        {
            builder.AppendLine(
                $"{item.CreatedAt:HH:mm:ss} | {item.Status} | {item.Title} | {item.Category} | risk={item.RiskLevel}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildLatestToolActivitySummary(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return "No tool activity yet.";
        }

        var events = File.ReadLines(logPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                try
                {
                    return JsonSerializer.Deserialize<RelayLogEvent>(line, HandoffJson.SerializerOptions);
                }
                catch
                {
                    return null;
                }
            })
            .Where(logEvent => logEvent is not null)
            .Select(logEvent => logEvent!)
            .Where(IsToolActivityEvent)
            .ToArray();

        if (events.Length == 0)
        {
            return "No tool activity yet.";
        }

        var latest = events[^1];
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"{latest.Timestamp:HH:mm:ss} | {latest.EventType} | {latest.Role?.ToString() ?? "-"}");
        builder.AppendLine(latest.Message);
        if (!string.IsNullOrWhiteSpace(latest.Payload))
        {
            builder.AppendLine();
            builder.AppendLine(latest.Payload);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildLatestGitActivitySummary(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return "No git or PR activity yet.";
        }

        var events = File.ReadLines(logPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                try
                {
                    return JsonSerializer.Deserialize<RelayLogEvent>(line, HandoffJson.SerializerOptions);
                }
                catch
                {
                    return null;
                }
            })
            .Where(logEvent => logEvent is not null)
            .Select(logEvent => logEvent!)
            .Where(IsGitOrPrEvent)
            .ToArray();

        if (events.Length == 0)
        {
            return "No git or PR activity yet.";
        }

        var latest = events[^1];
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"{latest.Timestamp:HH:mm:ss} | {latest.EventType} | {latest.Role?.ToString() ?? "-"}");
        builder.AppendLine(latest.Message);
        if (!string.IsNullOrWhiteSpace(latest.Payload))
        {
            builder.AppendLine();
            builder.AppendLine(latest.Payload);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildEventLogTailSummary(string logPath, int maxLines = 80)
    {
        if (!File.Exists(logPath))
        {
            return "No event log written yet.";
        }

        var tailLines = File.ReadLines(logPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(maxLines)
            .ToArray();

        if (tailLines.Length == 0)
        {
            return "No event log written yet.";
        }

        return string.Join(Environment.NewLine, tailLines);
    }

    private static string BuildToolCategorySummary(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return "No categorized tool activity yet.";
        }

        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(logPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            RelayLogEvent? logEvent;
            try
            {
                logEvent = JsonSerializer.Deserialize<RelayLogEvent>(line, HandoffJson.SerializerOptions);
            }
            catch
            {
                continue;
            }

            if (logEvent is null || !TrySplitToolCategoryEvent(logEvent.EventType, out var category, out var stage))
            {
                continue;
            }

            if (!counts.TryGetValue(category, out var stages))
            {
                stages = new Dictionary<string, int>(StringComparer.Ordinal);
                counts[category] = stages;
            }

            stages[stage] = stages.TryGetValue(stage, out var existing) ? existing + 1 : 1;
        }

        if (counts.Count == 0)
        {
            return "No categorized tool activity yet.";
        }

        return string.Join(
            Environment.NewLine,
            counts
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                    $"{pair.Key}: {string.Join(", ", pair.Value.OrderBy(stage => stage.Key, StringComparer.Ordinal).Select(stage => $"{stage.Key}={stage.Value}"))}"));
    }

    private static string BuildPolicyGapSummary(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return "No policy-gap advisories yet.";
        }

        var advisories = File.ReadLines(logPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                try
                {
                    return JsonSerializer.Deserialize<RelayLogEvent>(line, HandoffJson.SerializerOptions);
                }
                catch
                {
                    return null;
                }
            })
            .Where(logEvent => logEvent is not null)
            .Select(logEvent => logEvent!)
            .Where(logEvent =>
                string.Equals(logEvent.EventType, "mcp.review_required", StringComparison.Ordinal) ||
                string.Equals(logEvent.EventType, "web.review_required", StringComparison.Ordinal))
            .ToArray();

        if (advisories.Length == 0)
        {
            return "No policy-gap advisories yet.";
        }

        return string.Join(
            Environment.NewLine,
            advisories
                .OrderBy(logEvent => logEvent.Timestamp)
                .Select(logEvent => $"{logEvent.Timestamp:HH:mm:ss} | {logEvent.EventType} | {logEvent.Role?.ToString() ?? "-"} | {Shorten(logEvent.Message, 140)}"));
    }

    private static string BuildCurrentSessionRiskSummary(
        RelaySessionState state,
        RelayPendingApproval? pendingApproval,
        bool autoApproveAllRequests)
    {
        var lines = new List<string>();

        if (pendingApproval is not null)
        {
            lines.Add($"Pending: {ToRiskLabel(pendingApproval.RiskLevel)} | {pendingApproval.Title} | {pendingApproval.Category}");
        }

        var criticalRules = state.SessionApprovalRules.Count(rule => string.Equals(rule.RiskLevel, "critical", StringComparison.OrdinalIgnoreCase));
        var highRules = state.SessionApprovalRules.Count(rule => string.Equals(rule.RiskLevel, "high", StringComparison.OrdinalIgnoreCase));
        var mediumRules = state.SessionApprovalRules.Count(rule => string.Equals(rule.RiskLevel, "medium", StringComparison.OrdinalIgnoreCase));

        if (criticalRules > 0)
        {
            lines.Add($"Critical saved session approvals: {criticalRules}");
        }

        if (highRules > 0)
        {
            lines.Add($"High-risk saved session approvals: {highRules}");
        }

        if (mediumRules > 0)
        {
            lines.Add($"Medium-risk saved session approvals: {mediumRules}");
        }

        if (state.PolicyGapAdvisoriesFired.Count > 0)
        {
            var categories = state.PolicyGapAdvisoriesFired
                .Select(advisory => advisory.Split(':').LastOrDefault() ?? advisory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase);
            lines.Add($"Policy gap categories: {string.Join(", ", categories)}");
        }

        if (autoApproveAllRequests)
        {
            lines.Add("High: dangerous auto-approve-all mode is enabled.");
        }

        if (lines.Count == 0)
        {
            return "No elevated session risk indicators.";
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildSessionRiskBadgeText(
        RelaySessionState state,
        RelayPendingApproval? pendingApproval,
        bool autoApproveAllRequests)
    {
        if (string.Equals(pendingApproval?.RiskLevel, "critical", StringComparison.OrdinalIgnoreCase))
        {
            return "critical";
        }

        if (autoApproveAllRequests)
        {
            return "high";
        }

        if (string.Equals(pendingApproval?.RiskLevel, "high", StringComparison.OrdinalIgnoreCase))
        {
            return "high";
        }

        if (state.SessionApprovalRules.Any(rule => string.Equals(rule.RiskLevel, "critical", StringComparison.OrdinalIgnoreCase)))
        {
            return "critical";
        }

        if (state.PolicyGapAdvisoriesFired.Count > 0 ||
            state.SessionApprovalRules.Any(rule => string.Equals(rule.RiskLevel, "high", StringComparison.OrdinalIgnoreCase)))
        {
            return "high";
        }

        if (string.Equals(pendingApproval?.RiskLevel, "medium", StringComparison.OrdinalIgnoreCase) ||
            state.SessionApprovalRules.Any(rule => string.Equals(rule.RiskLevel, "medium", StringComparison.OrdinalIgnoreCase)))
        {
            return "medium";
        }

        if (string.Equals(pendingApproval?.RiskLevel, "low", StringComparison.OrdinalIgnoreCase))
        {
            return "low";
        }

        return "none";
    }

    private static string ToRiskLabel(string? riskLevel) => riskLevel switch
    {
        "critical" => "Critical",
        "high" => "High",
        "medium" => "Medium",
        "low" => "Low",
        _ => "Unknown"
    };

    private static bool TrySplitToolCategoryEvent(string eventType, out string category, out string stage)
    {
        category = string.Empty;
        stage = string.Empty;

        if (string.IsNullOrWhiteSpace(eventType) ||
            string.Equals(eventType, "tool.invoked", StringComparison.Ordinal) ||
            string.Equals(eventType, "tool.completed", StringComparison.Ordinal) ||
            string.Equals(eventType, "tool.failed", StringComparison.Ordinal) ||
            string.Equals(eventType, "adapter.event", StringComparison.Ordinal) ||
            string.Equals(eventType, "policy.applied", StringComparison.Ordinal) ||
            eventType.StartsWith("approval.", StringComparison.Ordinal))
        {
            return false;
        }

        var lastDot = eventType.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= eventType.Length - 1)
        {
            return false;
        }

        category = eventType[..lastDot];
        stage = eventType[(lastDot + 1)..];
        return category is "git" or "git.add" or "git.commit" or "git.push" or "pr" or "shell" or "file.change" or "permissions" or "mcp" or "web" or "tool";
    }

    private static bool IsToolActivityEvent(RelayLogEvent logEvent)
    {
        if (string.IsNullOrWhiteSpace(logEvent.EventType))
        {
            return false;
        }

        if (string.Equals(logEvent.EventType, "tool.invoked", StringComparison.Ordinal) ||
            string.Equals(logEvent.EventType, "tool.completed", StringComparison.Ordinal) ||
            string.Equals(logEvent.EventType, "tool.failed", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(logEvent.EventType, "adapter.event", StringComparison.Ordinal) ||
            string.Equals(logEvent.EventType, "policy.applied", StringComparison.Ordinal) ||
            logEvent.EventType.StartsWith("approval.", StringComparison.Ordinal))
        {
            return false;
        }

        return logEvent.EventType.EndsWith(".requested", StringComparison.Ordinal) ||
               logEvent.EventType.EndsWith(".review_required", StringComparison.Ordinal) ||
               logEvent.EventType.EndsWith(".completed", StringComparison.Ordinal) ||
               logEvent.EventType.EndsWith(".failed", StringComparison.Ordinal);
    }

    private static bool IsGitOrPrEvent(RelayLogEvent logEvent)
    {
        if (string.IsNullOrWhiteSpace(logEvent.EventType))
        {
            return false;
        }

        return logEvent.EventType.StartsWith("git.", StringComparison.Ordinal) ||
               logEvent.EventType.StartsWith("pr.", StringComparison.Ordinal);
    }

    private static string BuildSmokeTestReport(
        string sessionId,
        bool succeeded,
        string summary,
        string logPath,
        RelaySessionState state)
    {
        var recentEvents = BuildRecentEventsSummary(logPath);
        var codexHandle = state.NativeSessionHandles.TryGetValue(AgentRole.Codex.ToString(), out var codexSessionHandle)
            ? codexSessionHandle
            : "(none)";
        var claudeHandle = state.NativeSessionHandles.TryGetValue(AgentRole.Claude.ToString(), out var claudeSessionHandle)
            ? claudeSessionHandle
            : "(none)";
        var lastHandoffSummary = state.LastHandoff is null
            ? "(none)"
            : $"{state.LastHandoff.Source}->{state.LastHandoff.Target} turn {state.LastHandoff.Turn}";

        return
            $"Session: {sessionId}{Environment.NewLine}" +
            $"Result: {(succeeded ? "PASS" : "FAIL")}{Environment.NewLine}" +
            $"Summary: {summary}{Environment.NewLine}" +
            $"Codex handle: {codexHandle}{Environment.NewLine}" +
            $"Claude handle: {claudeHandle}{Environment.NewLine}" +
            $"Last handoff: {lastHandoffSummary}{Environment.NewLine}" +
            $"Accepted relays: {state.AcceptedRelayKeys.Count}{Environment.NewLine}" +
            $"Recent: {ExtractMostRelevantEvent(recentEvents)}";
    }

    private static string ExtractMostRelevantEvent(string recentEvents)
    {
        if (string.IsNullOrWhiteSpace(recentEvents) || string.Equals(recentEvents, "No recent events.", StringComparison.Ordinal))
        {
            return "No recent events.";
        }

        var lines = recentEvents
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.LastOrDefault() ?? "No recent events.";
    }

    private async Task<RelayApprovalDecision> HandlePendingApprovalAsync(
        RelayPendingApproval pendingApproval,
        CancellationToken cancellationToken)
    {
        if (_broker is not null)
        {
            await _broker.EnqueueApprovalAsync(pendingApproval, cancellationToken);
        }

        if (IsAutoApproveAllRequestsEnabled())
        {
            if (_broker is not null)
            {
                await _broker.LogAutoApprovalModeAppliedAsync(pendingApproval, cancellationToken);
                await _broker.ResolvePendingApprovalAsync(pendingApproval, RelayApprovalDecision.ApproveForSession, cancellationToken);
            }

            StatusMessageTextBlock.Text = $"Auto-approved {pendingApproval.Title} because dangerous auto-approve mode is enabled.";
            RefreshUi();
            return RelayApprovalDecision.ApproveForSession;
        }

        if (_broker is not null &&
            _broker.TryResolveSessionApproval(pendingApproval, out var savedDecision, out var matchedRule))
        {
            await _broker.LogSessionApprovalRuleAppliedAsync(pendingApproval, matchedRule!, cancellationToken);
            await _broker.ResolvePendingApprovalAsync(pendingApproval, savedDecision, cancellationToken);
            StatusMessageTextBlock.Text = $"Applied saved session approval for {pendingApproval.Title}.";
            RefreshUi();
            return savedDecision;
        }

        TaskCompletionSource<RelayApprovalDecision>? decisionSource = null;

        await Dispatcher.InvokeAsync(() =>
        {
            _livePendingApproval = pendingApproval;
            _pendingApprovalDecisionSource = new TaskCompletionSource<RelayApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            decisionSource = _pendingApprovalDecisionSource;
            StatusMessageTextBlock.Text = $"Approval requested by {pendingApproval.Role}: {pendingApproval.Message}";
            RefreshUi();
        });

        try
        {
            using var registration = cancellationToken.Register(
                static state =>
                {
                    if (state is TaskCompletionSource<RelayApprovalDecision> source)
                    {
                        source.TrySetCanceled();
                    }
                },
                decisionSource);

            var decision = await decisionSource!.Task;
            if (_broker is not null)
            {
                if (decision == RelayApprovalDecision.ApproveForSession)
                {
                    await _broker.SaveSessionApprovalRuleAsync(pendingApproval, decision, cancellationToken);
                }

                await _broker.ResolvePendingApprovalAsync(pendingApproval, decision, cancellationToken);
            }

            return decision;
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _livePendingApproval = null;
                _pendingApprovalDecisionSource = null;
                RefreshUi();
            });
        }
    }

    private void ApproveOnceButton_Click(object sender, RoutedEventArgs e) => ResolvePendingApproval(RelayApprovalDecision.ApproveOnce);

    private void ApproveSessionButton_Click(object sender, RoutedEventArgs e) => ResolvePendingApproval(RelayApprovalDecision.ApproveForSession);

    private void DenyApprovalButton_Click(object sender, RoutedEventArgs e) => ResolvePendingApproval(RelayApprovalDecision.Deny);

    private void CancelApprovalButton_Click(object sender, RoutedEventArgs e) => ResolvePendingApproval(RelayApprovalDecision.Cancel);

    private async void ClearSessionApprovalsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("Clearing saved session approval rules...", async () =>
        {
            if (_broker is null)
            {
                return;
            }

            await _broker.ClearSessionApprovalRulesAsync(CancellationToken.None);
            StatusMessageTextBlock.Text = "Saved session approval rules cleared.";
        });
    }

    private void ResolvePendingApproval(RelayApprovalDecision decision)
    {
        if (_pendingApprovalDecisionSource is not null)
        {
            _pendingApprovalDecisionSource.TrySetResult(decision);
            return;
        }

        if (_broker?.State.PendingApproval is null)
        {
            return;
        }

        _ = RunOperationAsync("Resolving pending approval...", async () =>
        {
            await _broker.ResolveCurrentPendingApprovalAsync(decision, CancellationToken.None);
            StatusMessageTextBlock.Text = decision switch
            {
                RelayApprovalDecision.ApproveOnce => "Pending approval approved once.",
                RelayApprovalDecision.ApproveForSession => "Pending approval approved for session.",
                RelayApprovalDecision.Deny => "Pending approval denied.",
                _ => "Pending approval cancelled."
            };
        });
    }

    private void SetBusyState(bool isBusy, string? message = null)
    {
        _isBusy = isBusy;
        if (!string.IsNullOrWhiteSpace(message))
        {
            BusyMessageTextBlock.Text = message;
        }
        else if (!_isBusy)
        {
            BusyMessageTextBlock.Text = "Idle.";
        }

        ApplyVisualStates();
    }

    private async Task YieldToUiAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private async Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(_appDataDirectory, "diagnostics"));

        var exportFileName = $"relay-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.md";
        var exportPath = Path.Combine(_appDataDirectory, "diagnostics", exportFileName);
        var state = _broker?.State;
        var logPath = _broker?.CurrentLogPath ?? string.Empty;
        var eventLogText = File.Exists(logPath)
            ? await File.ReadAllTextAsync(logPath, cancellationToken)
            : "No event log written yet.";

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Relay Diagnostics");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Runtime: {GetRuntimeModeLabel()}");
        builder.AppendLine($"AutoApproveAllRequests: {IsAutoApproveAllRequestsEnabled()}");
        builder.AppendLine($"Session Id: {state?.SessionId ?? "(none)"}");
        builder.AppendLine($"Log Path: {logPath}");
        builder.AppendLine();
        builder.AppendLine("## Adapter Status");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(AdapterStatusTextBlock.Text ?? string.Empty);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Smoke Test Report");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(_latestSmokeTestReport);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## State Summary");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(StateSummaryTextBlock.Text ?? string.Empty);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Latest Accepted Handoff");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine(LatestHandoffTextBox.Text ?? "No handoff accepted yet.");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Latest Approval");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(LatestApprovalTextBox.Text ?? "No approval activity yet.");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Approval Queue");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(ApprovalQueueTextBox.Text ?? "No approvals queued yet.");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Latest Tool Activity");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(LatestToolActivityTextBox.Text ?? "No tool activity yet.");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Latest Git / PR Activity");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(LatestGitActivityTextBox.Text ?? "No git or PR activity yet.");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Tool Category Summary");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(ToolCategorySummaryTextBox.Text ?? "No categorized tool activity yet.");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Policy Gap Summary");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(PolicyGapSummaryTextBox.Text ?? "No policy-gap advisories yet.");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Current Session Risk Summary");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(CurrentSessionRiskSummaryTextBox.Text ?? "No elevated session risk indicators.");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Session Approval Rules");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(SessionApprovalRulesTextBox.Text ?? "No saved session approval rules.");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Recent Events");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(RecentEventsTextBox.Text ?? string.Empty);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Full Event Log");
        builder.AppendLine();
        builder.AppendLine("```json");
        builder.AppendLine(eventLogText);
        builder.AppendLine("```");

        await File.WriteAllTextAsync(exportPath, builder.ToString(), cancellationToken);
        return exportPath;
    }

    private void WriteAutomaticLogArtifacts()
    {
        try
        {
            var autoLogDirectory = Path.Combine(_appDataDirectory, "auto-logs");
            Directory.CreateDirectory(autoLogDirectory);

            var snapshotText = BuildAutomaticSnapshotText();
            File.WriteAllText(Path.Combine(autoLogDirectory, "current-status.txt"), snapshotText);
            var signalJson = BuildRelaySignalJson();
            var signalText = BuildRelaySignalText();
            File.WriteAllText(Path.Combine(autoLogDirectory, "relay-live-signal.json"), signalJson);
            File.WriteAllText(Path.Combine(autoLogDirectory, "relay-live-signal.txt"), signalText);

            if (_broker is not null && !string.IsNullOrWhiteSpace(_broker.State.SessionId))
            {
                var sessionFileName = $"session-{SanitizeFileName(_broker.State.SessionId)}-status.txt";
                File.WriteAllText(Path.Combine(autoLogDirectory, sessionFileName), snapshotText);
            }

            var latestHandoffText = LatestHandoffTextBox.Text ?? "No handoff accepted yet.";
            File.WriteAllText(Path.Combine(autoLogDirectory, "latest-handoff.json"), latestHandoffText);
            MirrorRelaySignalToManagedWorkspace(signalJson, signalText);
            EnsureRelaySignalWatcher();
        }
        catch
        {
            // Best-effort only. Automatic readable log export must never break the UI.
        }
    }

    private string BuildAutomaticSnapshotText()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"Generated: {DateTimeOffset.Now:O}");
        builder.AppendLine($"AppData: {_appDataDirectory}");
        builder.AppendLine($"AutoApproveAllRequests: {IsAutoApproveAllRequestsEnabled()}");
        builder.AppendLine();
        builder.AppendLine("## Adapter Status");
        builder.AppendLine(AdapterStatusTextBlock.Text ?? "Status not checked yet.");
        builder.AppendLine();
        builder.AppendLine("## State Summary");
        builder.AppendLine(StateSummaryTextBlock.Text ?? "Broker not initialized.");
        builder.AppendLine();
        builder.AppendLine("## Current Event Log Path");
        builder.AppendLine(CurrentLogPathTextBlock.Text ?? "(no log file yet)");
        builder.AppendLine();
        builder.AppendLine("## Recent Events");
        builder.AppendLine(RecentEventsTextBox.Text ?? "No recent events.");
        builder.AppendLine();
        builder.AppendLine("## Latest Accepted Handoff");
        builder.AppendLine(LatestHandoffTextBox.Text ?? "No handoff accepted yet.");
        builder.AppendLine();
        builder.AppendLine("## Latest Approval");
        builder.AppendLine(LatestApprovalTextBox.Text ?? "No approval activity yet.");
        builder.AppendLine();
        builder.AppendLine("## Approval Queue");
        builder.AppendLine(ApprovalQueueTextBox.Text ?? "No approvals queued yet.");
        builder.AppendLine();
        builder.AppendLine("## Latest Tool Activity");
        builder.AppendLine(LatestToolActivityTextBox.Text ?? "No tool activity yet.");
        builder.AppendLine();
        builder.AppendLine("## Latest Git / PR Activity");
        builder.AppendLine(LatestGitActivityTextBox.Text ?? "No git or PR activity yet.");
        builder.AppendLine();
        builder.AppendLine("## Tool Category Summary");
        builder.AppendLine(ToolCategorySummaryTextBox.Text ?? "No categorized tool activity yet.");
        builder.AppendLine();
        builder.AppendLine("## Policy Gap Summary");
        builder.AppendLine(PolicyGapSummaryTextBox.Text ?? "No policy-gap advisories yet.");
        builder.AppendLine();
        builder.AppendLine("## Current Session Risk Summary");
        builder.AppendLine(CurrentSessionRiskSummaryTextBox.Text ?? "No elevated session risk indicators.");
        builder.AppendLine();
        builder.AppendLine("## Session Approval Rules");
        builder.AppendLine(SessionApprovalRulesTextBox.Text ?? "No saved session approval rules.");
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private string BuildRelaySignalJson()
    {
        var now = DateTimeOffset.Now;
        var status = _broker?.State.Status.ToString() ?? "Idle";
        var activeRole = _broker?.State.ActiveAgent ?? "(none)";
        var currentTurn = _broker?.State.CurrentTurn ?? 0;
        var lastProgressAt = _broker?.State.UpdatedAt ?? now;
        var progressAge = now - lastProgressAt;
        var watchdogSeconds = _broker is null
            ? (int?)null
            : CalculateWatchdogRemainingSeconds(_broker.State, now, _broker.Options.PerTurnTimeout);
        var signal = new Dictionary<string, object?>
        {
            ["generated_at"] = now.ToString("O"),
            ["session_id"] = _broker?.State.SessionId ?? string.Empty,
            ["status"] = status,
            ["is_terminal"] = IsSignalTerminal(status),
            ["active_role"] = activeRole,
            ["current_turn"] = currentTurn,
            ["last_progress_at"] = lastProgressAt.ToString("O"),
            ["last_progress_age_seconds"] = Math.Max(0, (int)progressAge.TotalSeconds),
            ["watchdog_remaining_seconds"] = watchdogSeconds,
            ["pending_approval_count"] = _broker?.State.ApprovalQueue.Count ?? 0,
            ["pending_approval"] = FormatPendingApproval(_livePendingApproval ?? _broker?.State.PendingApproval),
            ["last_error"] = _broker?.State.LastError ?? string.Empty,
            ["signal_marker"] = BuildRelaySignalMarker(now),
            ["done_marker"] = BuildRelayDoneMarker(),
            ["event_log_path"] = _broker?.CurrentLogPath ?? string.Empty,
            ["source_pid"] = Environment.ProcessId,
            ["source_process_started_at"] = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O"),
            ["heartbeat_at"] = now.ToString("O"),
            ["heartbeat_max_age_seconds"] = 30
        };

        return JsonSerializer.Serialize(signal, DisplayJsonOptions);
    }

    private string BuildRelaySignalText()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(BuildRelaySignalMarker(DateTimeOffset.Now));
        builder.AppendLine(BuildRelayDoneMarker());
        return builder.ToString();
    }

    private string BuildRelaySignalMarker(DateTimeOffset now)
    {
        if (_broker is null)
        {
            return "[RELAY_SIGNAL] status=idle session=(none) turn=0 role=(none) progress_age=00:00 watchdog=disabled approvals=0";
        }

        var state = _broker.State;
        var watchdog = FormatTurnWatchdog(state, now, _broker.Options.PerTurnTimeout).Replace(' ', '_');
        return $"[RELAY_SIGNAL] status={state.Status} session={state.SessionId} turn={state.CurrentTurn} role={state.ActiveAgent} progress_age={FormatElapsed(now - state.UpdatedAt)} watchdog={watchdog} approvals={state.ApprovalQueue.Count}";
    }

    private string BuildRelayDoneMarker()
    {
        if (_broker is null)
        {
            return "[RELAY_DONE] false status=idle";
        }

        var status = _broker.State.Status.ToString();
        var done = IsSignalTerminal(status) ? "true" : "false";
        var reason = string.IsNullOrWhiteSpace(_broker.State.LastError) ? "none" : Shorten(_broker.State.LastError, 80).Replace(' ', '_');
        return $"[RELAY_DONE] {done} status={status} reason={reason}";
    }

    private void MirrorRelaySignalToManagedWorkspace(string signalJson, string signalText)
    {
        var autopilotGenerated = TryGetManagedWorkspaceGeneratedDirectory();
        if (string.IsNullOrWhiteSpace(autopilotGenerated))
        {
            return;
        }

        Directory.CreateDirectory(autopilotGenerated);
        File.WriteAllText(Path.Combine(autopilotGenerated, "relay-live-signal.json"), signalJson);
        File.WriteAllText(Path.Combine(autopilotGenerated, "relay-live-signal.txt"), signalText);
    }

    private void EnsureRelaySignalWatcher()
    {
        if (_broker is null || _broker.State.Status != RelaySessionStatus.Active)
        {
            _relaySignalWatcherKey = null;
            return;
        }

        var repoRoot = TryFindRelayRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return;
        }

        var watcherScriptPath = Path.Combine(repoRoot, "scripts", "card-game", "Watch-RelaySignalLiveness.ps1");
        if (!File.Exists(watcherScriptPath))
        {
            return;
        }

        var sessionKey = string.Join("|", new[]
        {
            _broker.State.SessionId ?? string.Empty,
            Environment.ProcessId.ToString(),
            Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O"),
        });

        if (string.Equals(_relaySignalWatcherKey, sessionKey, StringComparison.Ordinal))
        {
            return;
        }

        var primarySignalJsonPath = Path.Combine(_appDataDirectory, "auto-logs", "relay-live-signal.json");
        var mirroredGeneratedDirectory = TryGetManagedWorkspaceGeneratedDirectory();
        var mirroredSignalJsonPath = string.IsNullOrWhiteSpace(mirroredGeneratedDirectory)
            ? string.Empty
            : Path.Combine(mirroredGeneratedDirectory, "relay-live-signal.json");
        var managedCardGameRoot = string.IsNullOrWhiteSpace(ManagedCardGameRootTextBox.Text)
            ? @"D:\Unity\card game"
            : ManagedCardGameRootTextBox.Text.Trim();

        var arguments = string.Join(' ', new[]
        {
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            QuotePowerShellArgument(watcherScriptPath),
            "-PrimarySignalJsonPath",
            QuotePowerShellArgument(primarySignalJsonPath),
            "-MirroredSignalJsonPath",
            QuotePowerShellArgument(mirroredSignalJsonPath),
            "-SourcePid",
            Environment.ProcessId.ToString(),
            "-SourceProcessStartedAt",
            QuotePowerShellArgument(Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O")),
            "-CardGameRoot",
            QuotePowerShellArgument(managedCardGameRoot)
        });

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot,
        };

        Process.Start(startInfo);
        _relaySignalWatcherKey = sessionKey;
    }

    private void NormalizeStaleRelaySignalsOnStartup()
    {
        try
        {
            NormalizeSignalFileIfStale(Path.Combine(_appDataDirectory, "auto-logs", "relay-live-signal.json"));

            var managedRoot = string.IsNullOrWhiteSpace(ManagedCardGameRootTextBox.Text)
                ? @"D:\Unity\card game"
                : ManagedCardGameRootTextBox.Text.Trim();
            NormalizeSignalFileIfStale(Path.Combine(managedRoot, ".autopilot", "generated", "relay-live-signal.json"));
        }
        catch
        {
            // Best-effort only. Stale signal cleanup must never block app startup.
        }
    }

    private void NormalizeSignalFileIfStale(string signalPath)
    {
        if (!File.Exists(signalPath))
        {
            return;
        }

        var signalNode = JsonNode.Parse(File.ReadAllText(signalPath)) as JsonObject;
        if (signalNode is null)
        {
            return;
        }

        var status = signalNode["status"]?.GetValue<string>() ?? string.Empty;
        if (!string.Equals(status, RelaySessionStatus.Active.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var sourcePid = signalNode["source_pid"]?.GetValue<int>() ?? 0;
        var sourceProcessStartedAt = signalNode["source_process_started_at"]?.GetValue<string>() ?? string.Empty;
        if (IsRelayProcessRecordLive(sourcePid, sourceProcessStartedAt))
        {
            return;
        }

        WriteSignalOverrideArtifacts(
            signalPath,
            signalNode["session_id"]?.GetValue<string>() ?? string.Empty,
            "Stale",
            isTerminal: true,
            signalNode["active_role"]?.GetValue<string>() ?? "(none)",
            signalNode["current_turn"]?.GetValue<int>() ?? 0,
            signalNode["last_progress_at"]?.GetValue<string>() ?? DateTimeOffset.Now.ToString("O"),
            "relay_process_missing");
    }

    private void MarkLiveRelaySignalAsClosed()
    {
        try
        {
            if (_broker is null || _broker.State.Status != RelaySessionStatus.Active)
            {
                return;
            }

            var signalJsonPath = Path.Combine(_appDataDirectory, "auto-logs", "relay-live-signal.json");
            WriteSignalOverrideArtifacts(
                signalJsonPath,
                _broker.State.SessionId,
                RelaySessionStatus.Stopped.ToString(),
                isTerminal: true,
                _broker.State.ActiveAgent,
                _broker.State.CurrentTurn,
                _broker.State.UpdatedAt.ToString("O"),
                "desktop_closed");

            var managedGeneratedDirectory = TryGetManagedWorkspaceGeneratedDirectory();
            if (!string.IsNullOrWhiteSpace(managedGeneratedDirectory))
            {
                WriteSignalOverrideArtifacts(
                    Path.Combine(managedGeneratedDirectory, "relay-live-signal.json"),
                    _broker.State.SessionId,
                    RelaySessionStatus.Stopped.ToString(),
                    isTerminal: true,
                    _broker.State.ActiveAgent,
                    _broker.State.CurrentTurn,
                    _broker.State.UpdatedAt.ToString("O"),
                    "desktop_closed");
            }
        }
        catch
        {
            // Best-effort only. Closing should not fail because signal rewrite failed.
        }
    }

    private void WriteSignalOverrideArtifacts(
        string signalJsonPath,
        string sessionId,
        string status,
        bool isTerminal,
        string activeRole,
        int currentTurn,
        string lastProgressAt,
        string reason)
    {
        var generatedAt = DateTimeOffset.Now;
        var signalJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["generated_at"] = generatedAt.ToString("O"),
            ["session_id"] = sessionId,
            ["status"] = status,
            ["is_terminal"] = isTerminal,
            ["active_role"] = activeRole,
            ["current_turn"] = currentTurn,
            ["last_progress_at"] = lastProgressAt,
            ["last_progress_age_seconds"] = 0,
            ["watchdog_remaining_seconds"] = 0,
            ["pending_approval_count"] = 0,
            ["pending_approval"] = string.Empty,
            ["last_error"] = reason,
            ["signal_marker"] = $"[RELAY_SIGNAL] status={status.ToLowerInvariant()} session={(string.IsNullOrWhiteSpace(sessionId) ? "(none)" : sessionId)} turn={currentTurn} role={activeRole} progress_age=00:00 watchdog=disabled approvals=0",
            ["done_marker"] = $"[RELAY_DONE] {(isTerminal ? "true" : "false")} status={status.ToLowerInvariant()} reason={reason}",
            ["event_log_path"] = _broker?.CurrentLogPath ?? string.Empty,
            ["source_pid"] = Environment.ProcessId,
            ["source_process_started_at"] = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O"),
            ["heartbeat_at"] = generatedAt.ToString("O"),
            ["heartbeat_max_age_seconds"] = 30,
        }, DisplayJsonOptions);
        var signalText = string.Join(Environment.NewLine, new[]
        {
            $"[RELAY_SIGNAL] status={status.ToLowerInvariant()} session={(string.IsNullOrWhiteSpace(sessionId) ? "(none)" : sessionId)} turn={currentTurn} role={activeRole} progress_age=00:00 watchdog=disabled approvals=0",
            $"[RELAY_DONE] {(isTerminal ? "true" : "false")} status={status.ToLowerInvariant()} reason={reason}",
            string.Empty,
        });

        var signalDirectory = Path.GetDirectoryName(signalJsonPath);
        if (!string.IsNullOrWhiteSpace(signalDirectory))
        {
            Directory.CreateDirectory(signalDirectory);
        }

        File.WriteAllText(signalJsonPath, signalJson);
        File.WriteAllText(Path.ChangeExtension(signalJsonPath, ".txt"), signalText);
    }

    private static bool IsRelayProcessRecordLive(int pid, string processStartedAt)
    {
        if (pid <= 0 || string.IsNullOrWhiteSpace(processStartedAt))
        {
            return false;
        }

        try
        {
            var process = Process.GetProcessById(pid);
            return string.Equals(
                process.StartTime.ToUniversalTime().ToString("O"),
                processStartedAt,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private string? TryGetManagedWorkspaceGeneratedDirectory()
    {
        var workingDirectory = WorkingDirectoryTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return null;
        }

        var directory = new DirectoryInfo(workingDirectory);
        while (directory is not null)
        {
            var autopilotDirectory = Path.Combine(directory.FullName, ".autopilot");
            if (Directory.Exists(autopilotDirectory))
            {
                return Path.Combine(autopilotDirectory, "generated");
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsSignalTerminal(string status) =>
        string.Equals(status, RelaySessionStatus.Converged.ToString(), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, RelaySessionStatus.Failed.ToString(), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, RelaySessionStatus.Stopped.ToString(), StringComparison.OrdinalIgnoreCase);

    private static int? CalculateWatchdogRemainingSeconds(RelaySessionState state, DateTimeOffset now, TimeSpan perTurnTimeout)
    {
        if (state.Status != RelaySessionStatus.Active || perTurnTimeout <= TimeSpan.Zero)
        {
            return null;
        }

        var remaining = perTurnTimeout - (now - state.UpdatedAt);
        return (int)Math.Floor(remaining.TotalSeconds);
    }

    private async Task EnsureBrokerAsync(CancellationToken cancellationToken, bool recreate = false)
    {
        if (_broker is not null && !recreate)
        {
            return;
        }

        if (recreate)
        {
            await DisposeOwnedDisposablesAsync(cancellationToken);
        }

        var stateFileName = GetRuntimeMode() == RelayRuntimeMode.Interactive
            ? "state-interactive.json"
            : "state-noninteractive.json";
        var store = new JsonRelaySessionStore(Path.Combine(_appDataDirectory, stateFileName));
        var eventWriter = new JsonlEventLogWriter(Path.Combine(_appDataDirectory, "logs"));
        _brokerOptions = LoadBrokerOptions();
        var workingDirectory = string.IsNullOrWhiteSpace(WorkingDirectoryTextBox.Text)
            ? Environment.CurrentDirectory
            : WorkingDirectoryTextBox.Text.Trim();

        IEnumerable<IRelayAdapter> adapters;
        // DAD-v2 reset: Interactive adapters (Codex/Claude) were coupled to the
        // deleted CodexProtocol project. The Interactive branch now falls through
        // to CLI adapters until agent-agnostic interactive transport is rebuilt
        // post-G4. RelayRuntimeMode.Interactive is preserved for config compat.
        {
            adapters =
            [
                new CodexCliAdapter(workingDirectory, jobObjectOptions: _brokerOptions.JobObject),
                new ClaudeCliAdapter(workingDirectory, jobObjectOptions: _brokerOptions.JobObject),
            ];
            _broker = new RelayBroker(adapters, store, eventWriter, options: _brokerOptions);
        }
        await _broker.LoadAsync(cancellationToken);
        await RefreshAdapterStatusAsync();
    }

    private RelayBrokerOptions LoadBrokerOptions()
    {
        var optionsPath = Path.Combine(_appDataDirectory, "broker.json");
        if (!File.Exists(optionsPath))
        {
            _optionsLoadDiagnostic = "broker.json not found; using built-in defaults.";
            return new RelayBrokerOptions();
        }

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        try
        {
            var loaded = JsonSerializer.Deserialize<RelayBrokerOptions>(File.ReadAllText(optionsPath), serializerOptions)
                ?? new RelayBrokerOptions();
            var validated = RelayBrokerOptionsValidator.ClampAndReport(loaded, out var warnings);
            _optionsLoadDiagnostic = warnings.Count == 0
                ? "broker.json loaded."
                : "broker.json loaded with corrections: " + string.Join("; ", warnings);

            if (warnings.Count > 0)
            {
                MessageBox.Show(
                    _optionsLoadDiagnostic,
                    "Relay App Config",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return validated;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _optionsLoadDiagnostic = $"broker.json is invalid; using defaults. {ex.Message}";
            MessageBox.Show(
                _optionsLoadDiagnostic,
                "Relay App Config",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return new RelayBrokerOptions();
        }
    }

    private async Task<IReadOnlyDictionary<string, AdapterStatus>> GetAdapterStatusesWithTimeoutAsync(CancellationToken cancellationToken)
    {
        if (_broker is null)
        {
            return new Dictionary<string, AdapterStatus>(StringComparer.Ordinal);
        }

        if (_brokerOptions.PerTurnTimeout <= TimeSpan.Zero)
        {
            return await _broker.GetAdapterStatusesAsync(cancellationToken);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(_brokerOptions.PerTurnTimeout);

        try
        {
            return await _broker.GetAdapterStatusesAsync(linkedCts.Token);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == linkedCts.Token && !cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Adapter status check timed out after {_brokerOptions.PerTurnTimeout:mm\\:ss}. Review broker.json or adapter health.");
        }
    }

    private RelayRuntimeMode GetRuntimeMode() =>
        UseInteractiveAdaptersCheckBox.IsChecked.GetValueOrDefault()
            ? RelayRuntimeMode.Interactive
            : RelayRuntimeMode.NonInteractive;

    private string GetApprovalPathSummary(string role)
    {
        if (GetRuntimeMode() != RelayRuntimeMode.Interactive)
        {
            return "CLI-native only (no broker-routed approval)";
        }

        return role switch
        {
            AgentRole.Codex => "broker-routed (app-server on-request)",
            AgentRole.Claude => "audit-only (stream-json; no broker-routed approval)",
            _ => "unknown"
        };
    }

    private bool IsAutoApproveAllRequestsEnabled() =>
        AutoApproveAllRequestsCheckBox.IsChecked.GetValueOrDefault();

    private string GetRuntimeModeLabel() =>
        GetRuntimeMode() == RelayRuntimeMode.Interactive
            ? "INTERACTIVE"
            : "NON_INTERACTIVE";

    private async Task DisposeOwnedDisposablesAsync(CancellationToken cancellationToken)
    {
        foreach (var disposable in _ownedDisposables.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await disposable.DisposeAsync();
            }
            catch
            {
                // Best-effort cleanup for prototype adapters.
            }
        }

        _ownedDisposables.Clear();
    }

    private int ParseManagedTurns()
    {
        if (int.TryParse(ManagedTurnsTextBox.Text?.Trim(), out var turns) && turns >= 2)
        {
            return Math.Min(turns, 12);
        }

        return 2;
    }

    private int ParseManagedLoopSessions()
    {
        if (int.TryParse(ManagedLoopSessionsTextBox.Text?.Trim(), out var sessions) && sessions >= 1)
        {
            return Math.Min(sessions, 12);
        }

        return 3;
    }

    private async Task<ManagedCardGamePreparation> PrepareManagedCardGameSessionAsync(
        string cardGameRoot,
        string taskSlug,
        CancellationToken cancellationToken)
    {
        var repoRoot = TryFindRelayRepositoryRoot()
            ?? throw new InvalidOperationException("Could not locate the relay repository root from the desktop app.");
        var scriptPath = Path.Combine(repoRoot, "scripts", "card-game", "Start-CardGameRelay.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Managed card-game starter script was not found.", scriptPath);
        }

        var manifestPath = Path.Combine(repoRoot, "profiles", "card-game", "generated-admission.json");
        var promptPath = Path.Combine(repoRoot, "profiles", "card-game", "generated-session-prompt.md");

        var arguments = new List<string>
        {
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            QuotePowerShellArgument(scriptPath),
            "-CardGameRoot",
            QuotePowerShellArgument(cardGameRoot),
            "-PrepareOnly",
            "-ForceRelay"
        };

        if (!string.IsNullOrWhiteSpace(taskSlug))
        {
            arguments.Add("-TaskSlug");
            arguments.Add(QuotePowerShellArgument(taskSlug));
        }

        var preparationResult = await RunPowerShellProcessAsync(string.Join(' ', arguments), cancellationToken);
        if (preparationResult.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(preparationResult.ErrorText)
                ? "Managed card-game preparation failed."
                : preparationResult.ErrorText);
        }

        if (!File.Exists(manifestPath) || !File.Exists(promptPath))
        {
            throw new InvalidOperationException("Managed card-game preparation did not produce the expected manifest/prompt artifacts.");
        }

        using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        var sessionId = manifestDocument.RootElement.TryGetProperty("session_id", out var sessionIdElement)
            ? sessionIdElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Prepared card-game manifest does not contain a session id.");
        }

        _currentAdmissionManifestPath = manifestPath;
        SaveUiSettings();

        return new ManagedCardGamePreparation(
            sessionId.Trim(),
            await File.ReadAllTextAsync(promptPath, cancellationToken),
            manifestPath);
    }

    private async Task<ManagedAutopilotPreparation> PrepareManagedAutopilotSessionAsync(
        string cardGameRoot,
        CancellationToken cancellationToken)
    {
        var repoRoot = TryFindRelayRepositoryRoot()
            ?? throw new InvalidOperationException("Could not locate the relay repository root from the desktop app.");
        var scriptPath = Path.Combine(repoRoot, "scripts", "card-game", "Invoke-CardGameAutopilotLoop.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Managed autopilot script was not found.", scriptPath);
        }

        var arguments = string.Join(' ', new[]
        {
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            QuotePowerShellArgument(scriptPath),
            "-CardGameRoot",
            QuotePowerShellArgument(cardGameRoot),
            "-PrepareOnly",
            "-MaxSessions",
            "1"
        });

        var preparationResult = await RunPowerShellProcessAsync(arguments, cancellationToken);
        if (preparationResult.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(preparationResult.ErrorText)
                ? "Managed autopilot preparation failed."
                : preparationResult.ErrorText);
        }

        var managerSignal = await LoadManagerSignalAsync(cardGameRoot, cancellationToken);
        var suggestedAction = managerSignal?.SuggestedDesktopAction ?? "prepare_autopilot";
        var statusMessage = managerSignal is null
            ? "Managed autopilot preparation completed, but no manager signal was produced."
            : $"{managerSignal.OverallStatus}: {managerSignal.Reason}. Suggested action: {managerSignal.SuggestedDesktopAction}.";

        return new ManagedAutopilotPreparation(
            string.Equals(suggestedAction, "run_relay_session", StringComparison.OrdinalIgnoreCase),
            statusMessage);
    }

    private async Task<ManagedControllerResult> ExecuteManagedControllerCycleAsync(
        string cardGameRoot,
        int turns,
        CancellationToken cancellationToken)
    {
        for (var guard = 0; guard < 6; guard++)
        {
            var managerSignal = await LoadManagerSignalAsync(cardGameRoot, cancellationToken);
            if (managerSignal is null)
            {
                return new ManagedControllerResult(false, "Managed status is not available.");
            }

            switch (managerSignal.SuggestedDesktopAction)
            {
                case "prepare_autopilot":
                case "prepare_fresh_session":
                    await PrepareManagedAutopilotSessionAsync(cardGameRoot, cancellationToken);
                    await RefreshManagedStatusAsync(cancellationToken);
                    continue;

                case "run_relay_session":
                {
                    var prepared = await LoadPreparedManagedSessionAsync(cancellationToken);
                    WorkingDirectoryTextBox.Text = cardGameRoot;
                    SessionIdTextBox.Text = prepared.SessionId;
                    InitialPromptTextBox.Text = prepared.Prompt;
                    _currentAdmissionManifestPath = prepared.ManifestPath;
                    SaveUiSettings();

                    await EnsureBrokerAsync(cancellationToken, recreate: true);
                    await EnsureAdaptersReadyForSessionAsync();

                    await _broker!.StartSessionAsync(
                        prepared.SessionId,
                        AgentRole.Codex,
                        prepared.Prompt,
                        TryLoadSessionBootstrap(),
                        cancellationToken);

                    await RunTurnBatchCoreAsync(
                        turns,
                        successPauseReason: turns == 2
                            ? "Paused intentionally after one successful relay cycle."
                            : $"Paused intentionally after {turns} managed relay turns.",
                        successStatusMessage: turns == 2
                            ? "Managed controller completed one cycle and paused intentionally."
                            : $"Managed controller completed {turns} turns and paused intentionally.");

                    var completionResult = await CompleteManagedRelaySessionAsync(
                        cardGameRoot,
                        prepared.ManifestPath,
                        cancellationToken);
                    if (completionResult.ExitCode != 0)
                    {
                        var message = string.IsNullOrWhiteSpace(completionResult.ErrorText)
                            ? "managed completion failed"
                            : completionResult.ErrorText;
                        return new ManagedControllerResult(false, message);
                    }

                    await RefreshManagedStatusAsync(cancellationToken);
                    return new ManagedControllerResult(true, $"Managed controller executed session {prepared.SessionId}.");
                }

                case "complete_terminal_session":
                {
                    var completionResult = await CompleteManagedRelaySessionAsync(
                        cardGameRoot,
                        ResolveManagedManifestPath(cardGameRoot),
                        cancellationToken);
                    if (completionResult.ExitCode != 0)
                    {
                        var message = string.IsNullOrWhiteSpace(completionResult.ErrorText)
                            ? "managed completion failed"
                            : completionResult.ErrorText;
                        return new ManagedControllerResult(false, message);
                    }

                    await RefreshManagedStatusAsync(cancellationToken);
                    continue;
                }

                case "consume_route_artifact":
                    return new ManagedControllerResult(false, "Execution route selected a non-relay path. Consume the route artifact instead of starting DAD.");

                case "wait_for_signal":
                    return new ManagedControllerResult(false, "A relay session is already active. Wait for the compact signal instead of starting another action.");

                case "wait_for_operator":
                    return new ManagedControllerResult(false, "HALT is active. Operator action is required before the desktop controller can continue.");

                case "fix_blocker":
                    return new ManagedControllerResult(false, "Backlog or decision state is blocked. Fix the blocker before continuing the desktop controller.");

                default:
                    return new ManagedControllerResult(false, $"Unsupported managed action: {managerSignal.SuggestedDesktopAction}");
            }
        }

        return new ManagedControllerResult(false, "Managed controller reached its internal guard limit. Refresh the manager signal and retry.");
    }

    private async Task<ManagedCardGamePreparation> LoadPreparedManagedSessionAsync(CancellationToken cancellationToken)
    {
        var repoRoot = TryFindRelayRepositoryRoot()
            ?? throw new InvalidOperationException("Could not locate the relay repository root from the desktop app.");
        var manifestPath = Path.Combine(repoRoot, "profiles", "card-game", "generated-admission.json");
        var promptPath = Path.Combine(repoRoot, "profiles", "card-game", "generated-session-prompt.md");
        if (!File.Exists(manifestPath) || !File.Exists(promptPath))
        {
            throw new InvalidOperationException("Prepared card-game artifacts are missing. Run managed preparation first.");
        }

        using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        var sessionId = manifestDocument.RootElement.TryGetProperty("session_id", out var sessionIdElement)
            ? sessionIdElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Prepared card-game manifest does not contain a session id.");
        }

        _currentAdmissionManifestPath = manifestPath;
        SaveUiSettings();

        return new ManagedCardGamePreparation(
            sessionId.Trim(),
            await File.ReadAllTextAsync(promptPath, cancellationToken),
            manifestPath);
    }

    private async Task RefreshManagedStatusAsync(CancellationToken cancellationToken)
    {
        var cardGameRoot = string.IsNullOrWhiteSpace(ManagedCardGameRootTextBox.Text)
            ? @"D:\Unity\card game"
            : ManagedCardGameRootTextBox.Text.Trim();
        var managerSignal = await LoadManagerSignalAsync(cardGameRoot, cancellationToken);
        var resolvedTaskSlug = !string.IsNullOrWhiteSpace(managerSignal?.ResolvedTaskSlug)
            ? managerSignal.ResolvedTaskSlug
            : ManagedTaskSlugTextBox.Text?.Trim() ?? string.Empty;

        ManagedStatusTextBox.Text = managerSignal is null
            ? "Managed status not available."
            : $"Overall: {managerSignal.OverallStatus}{Environment.NewLine}" +
              $"Reason: {managerSignal.Reason}{Environment.NewLine}" +
              $"Next action: {managerSignal.NextAction}{Environment.NewLine}" +
              $"Task slug: {resolvedTaskSlug}{Environment.NewLine}" +
              $"Session: {managerSignal.SessionId}{Environment.NewLine}" +
              $"Relay status: {managerSignal.RelayStatus}{Environment.NewLine}" +
              $"Suggested action: {managerSignal.SuggestedDesktopAction}{Environment.NewLine}" +
              $"Wait should end: {managerSignal.WaitShouldEnd}{Environment.NewLine}" +
              $"Attention required: {managerSignal.AttentionRequired}{Environment.NewLine}" +
              $"Marker: {managerSignal.ManagerSignalMarker}{Environment.NewLine}" +
              $"Done: {managerSignal.ManagerDoneMarker}";
        EasyStatusTextBox.Text = managerSignal is null
            ? "Easy status is not available."
            : $"What is happening: {BuildEasyOverallLabel(managerSignal.OverallStatus)}{Environment.NewLine}" +
              $"What the app is doing next: {BuildEasyActionLabel(managerSignal.SuggestedDesktopAction)}{Environment.NewLine}" +
              $"Task: {resolvedTaskSlug}{Environment.NewLine}" +
              $"Need a human now: {(managerSignal.AttentionRequired ? "yes" : "no")}{Environment.NewLine}" +
              $"Can stop waiting now: {(managerSignal.WaitShouldEnd ? "yes" : "no")}";
    }

    private static string BuildEasyOverallLabel(string overallStatus) => overallStatus switch
    {
        "relay_active" => "A relay session is running.",
        "relay_ready" => "A prepared relay session is ready to run.",
        "prepare_next" => "The app is preparing the next slice.",
        "completion_pending" => "The session finished and needs write-back.",
        "route_only" => "This slice should not run through DAD.",
        "relay_dead" => "The previous relay session died and must be replaced.",
        "blocked" => "The backlog or decision state is blocked.",
        "halted" => "The project is halted on purpose.",
        _ => "The app is waiting for the next safe step."
    };

    private static string BuildEasyActionLabel(string suggestedDesktopAction) => suggestedDesktopAction switch
    {
        "prepare_autopilot" => "Prepare the next safe slice.",
        "prepare_fresh_session" => "Discard the dead session and prepare a fresh one.",
        "run_relay_session" => "Run the prepared relay session now.",
        "complete_terminal_session" => "Write the finished session back into autopilot state.",
        "consume_route_artifact" => "Use the route artifact instead of starting DAD.",
        "wait_for_signal" => "Do nothing and wait for the compact signal to change.",
        "wait_for_operator" => "Stop and wait for a human decision.",
        "fix_blocker" => "Stop and fix the blocker before continuing.",
        _ => "Wait for the next compact status update."
    };

    private async Task<ManagerSignalSummary?> LoadManagerSignalAsync(string cardGameRoot, CancellationToken cancellationToken)
    {
        var repoRoot = TryFindRelayRepositoryRoot()
            ?? throw new InvalidOperationException("Could not locate the relay repository root from the desktop app.");
        var scriptPath = Path.Combine(repoRoot, "scripts", "card-game", "Get-CardGameManagerSignal.ps1");
        if (!File.Exists(scriptPath))
        {
            return null;
        }

        var arguments = string.Join(' ', new[]
        {
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            QuotePowerShellArgument(scriptPath),
            "-CardGameRoot",
            QuotePowerShellArgument(cardGameRoot)
        });

        var result = await RunPowerShellProcessAsync(arguments, cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.OutputText))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ManagerSignalSummary>(result.OutputText, HandoffJson.SerializerOptions);
    }

    private async Task<ProcessRunResult> CompleteManagedRelaySessionAsync(
        string cardGameRoot,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var repoRoot = TryFindRelayRepositoryRoot()
            ?? throw new InvalidOperationException("Could not locate the relay repository root from the desktop app.");
        var scriptPath = Path.Combine(repoRoot, "scripts", "card-game", "Complete-CardGameRelaySession.ps1");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Managed completion script was not found.", scriptPath);
        }

        var arguments = string.Join(' ', new[]
        {
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            QuotePowerShellArgument(scriptPath),
            "-CardGameRoot",
            QuotePowerShellArgument(cardGameRoot),
            "-ManifestPath",
            QuotePowerShellArgument(manifestPath)
        });

        return await RunPowerShellProcessAsync(arguments, cancellationToken);
    }

    private string ResolveManagedManifestPath(string cardGameRoot)
    {
        if (!string.IsNullOrWhiteSpace(_currentAdmissionManifestPath))
        {
            return _currentAdmissionManifestPath;
        }

        var repoRoot = TryFindRelayRepositoryRoot()
            ?? throw new InvalidOperationException("Could not locate the relay repository root from the desktop app.");
        return Path.Combine(repoRoot, "profiles", "card-game", "generated-admission.json");
    }

    private async Task<ProcessRunResult> RunPowerShellProcessAsync(string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = TryFindRelayRepositoryRoot() ?? Environment.CurrentDirectory,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessRunResult(
            process.ExitCode,
            (await standardOutputTask).Trim(),
            (await standardErrorTask).Trim());
    }

    private string? TryFindRelayRepositoryRoot()
    {
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (baseDirectory is not null)
        {
            var candidate = Path.Combine(baseDirectory.FullName, "scripts", "card-game", "Start-CardGameRelay.ps1");
            if (File.Exists(candidate))
            {
                return baseDirectory.FullName;
            }

            baseDirectory = baseDirectory.Parent;
        }

        return null;
    }

    private static string QuotePowerShellArgument(string value) =>
        $"\"{value.Replace("\"", "`\"")}\"";

    private void LoadUiSettings()
    {
        _suppressUiSettingCallbacks = true;
        try
        {
            var settingsPath = GetUiSettingsPath();
            if (File.Exists(settingsPath))
            {
                var settings = JsonSerializer.Deserialize<RelayUiSettings>(
                    File.ReadAllText(settingsPath),
                    HandoffJson.SerializerOptions) ?? new RelayUiSettings();
                WorkingDirectoryTextBox.Text = string.IsNullOrWhiteSpace(settings.WorkingDirectory)
                    ? Environment.CurrentDirectory
                    : settings.WorkingDirectory;
                SessionIdTextBox.Text = settings.SessionId ?? string.Empty;
                InitialPromptTextBox.Text = settings.InitialPrompt ?? string.Empty;
                ManagedCardGameRootTextBox.Text = string.IsNullOrWhiteSpace(settings.ManagedCardGameRoot)
                    ? @"D:\Unity\card game"
                    : settings.ManagedCardGameRoot;
                ManagedTaskSlugTextBox.Text = string.IsNullOrWhiteSpace(settings.ManagedTaskSlug)
                    ? "companion-depth-first-slice"
                    : settings.ManagedTaskSlug;
                ManagedTurnsTextBox.Text = settings.ManagedTurns <= 1 ? "2" : settings.ManagedTurns.ToString();
                ManagedLoopSessionsTextBox.Text = settings.ManagedLoopSessions <= 0 ? "3" : settings.ManagedLoopSessions.ToString();
                _currentAdmissionManifestPath = settings.AdmissionManifestPath;
                UseInteractiveAdaptersCheckBox.IsChecked = settings.UseInteractiveAdapters;
                AutoApproveAllRequestsCheckBox.IsChecked = settings.AutoApproveAllRequests;
            }
            else
            {
                WorkingDirectoryTextBox.Text = Environment.CurrentDirectory;
                ManagedCardGameRootTextBox.Text = @"D:\Unity\card game";
                ManagedTaskSlugTextBox.Text = "companion-depth-first-slice";
                ManagedTurnsTextBox.Text = "2";
                ManagedLoopSessionsTextBox.Text = "3";
            }
        }
        catch
        {
            WorkingDirectoryTextBox.Text = Environment.CurrentDirectory;
            ManagedCardGameRootTextBox.Text = @"D:\Unity\card game";
            ManagedTaskSlugTextBox.Text = "companion-depth-first-slice";
            ManagedTurnsTextBox.Text = "2";
            ManagedLoopSessionsTextBox.Text = "3";
        }
        finally
        {
            _suppressUiSettingCallbacks = false;
        }
    }

    private void SaveUiSettings()
    {
        try
        {
            Directory.CreateDirectory(_appDataDirectory);
            var settings = new RelayUiSettings
            {
                WorkingDirectory = WorkingDirectoryTextBox.Text?.Trim(),
                SessionId = SessionIdTextBox.Text?.Trim(),
                InitialPrompt = InitialPromptTextBox.Text,
                AdmissionManifestPath = _currentAdmissionManifestPath,
                ManagedCardGameRoot = ManagedCardGameRootTextBox.Text?.Trim(),
                ManagedTaskSlug = ManagedTaskSlugTextBox.Text?.Trim(),
                ManagedTurns = ParseManagedTurns(),
                ManagedLoopSessions = ParseManagedLoopSessions(),
                UseInteractiveAdapters = UseInteractiveAdaptersCheckBox.IsChecked.GetValueOrDefault(),
                AutoApproveAllRequests = AutoApproveAllRequestsCheckBox.IsChecked.GetValueOrDefault()
            };
            File.WriteAllText(
                GetUiSettingsPath(),
                JsonSerializer.Serialize(settings, HandoffJson.SerializerOptions));
        }
        catch
        {
            // Best-effort only. UI settings persistence must never break the app.
        }
    }

    private string GetUiSettingsPath() => Path.Combine(_appDataDirectory, "ui-settings.json");

    private string? TryReadCurrentAdmissionManifestPath()
    {
        if (!string.IsNullOrWhiteSpace(_currentAdmissionManifestPath))
        {
            return _currentAdmissionManifestPath;
        }

        try
        {
            var settingsPath = GetUiSettingsPath();
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<RelayUiSettings>(
                File.ReadAllText(settingsPath),
                HandoffJson.SerializerOptions);
            _currentAdmissionManifestPath = settings?.AdmissionManifestPath;
            return _currentAdmissionManifestPath;
        }
        catch
        {
            return null;
        }
    }

    private RelaySessionBootstrap? TryLoadSessionBootstrap()
    {
        try
        {
            var manifestPath = TryReadCurrentAdmissionManifestPath();
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            var task = root.TryGetProperty("task", out var taskElement) ? taskElement : default;
            var guidance = root.TryGetProperty("guidance", out var guidanceElement) ? guidanceElement : default;
            var decisionsNode = root.TryGetProperty("decisions", out var decisionsElement) ? decisionsElement : default;
            var decisions = new List<string>();

            if (decisionsNode.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in decisionsNode.EnumerateObject())
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        decisions.Add($"{property.Name}: {value}");
                    }
                }
            }

            var constraints = new List<string>();
            if (guidance.ValueKind == JsonValueKind.Object)
            {
                if (guidance.TryGetProperty("recommended_read_path", out var readPathElement) &&
                    readPathElement.ValueKind == JsonValueKind.Array)
                {
                    var readPath = string.Join(" -> ", readPathElement.EnumerateArray()
                        .Select(static item => item.GetString())
                        .Where(static item => !string.IsNullOrWhiteSpace(item)));
                    if (!string.IsNullOrWhiteSpace(readPath))
                    {
                        constraints.Add($"recommended_read_path: {readPath}");
                    }
                }

                if (guidance.TryGetProperty("verification_expectation", out var verificationElement))
                {
                    var verification = verificationElement.GetString();
                    if (!string.IsNullOrWhiteSpace(verification))
                    {
                        constraints.Add($"verification_expectation: {verification}");
                    }
                }

                if (guidance.TryGetProperty("token_policy", out var tokenPolicyElement))
                {
                    var tokenPolicy = tokenPolicyElement.GetString();
                    if (!string.IsNullOrWhiteSpace(tokenPolicy))
                    {
                        constraints.Add($"token_policy: {tokenPolicy}");
                    }
                }
            }

            return new RelaySessionBootstrap
            {
                Mode = "hybrid",
                Scope = task.TryGetProperty("priority", out var priorityElement) &&
                        string.Equals(priorityElement.GetString(), "P1", StringComparison.OrdinalIgnoreCase)
                    ? "medium"
                    : "small",
                OriginBacklogId = task.TryGetProperty("slug", out var slugElement) ? slugElement.GetString() ?? string.Empty : string.Empty,
                TaskBucket = task.TryGetProperty("bucket", out var bucketElement) ? bucketElement.GetString() ?? string.Empty : string.Empty,
                TaskSummary = task.TryGetProperty("summary", out var summaryElement) ? summaryElement.GetString() ?? string.Empty : string.Empty,
                ContractStatus = "accepted",
                Decisions = decisions,
                Constraints = constraints,
            };
        }
        catch
        {
            return null;
        }
    }

    private sealed record ManagedCardGamePreparation(string SessionId, string Prompt, string ManifestPath);

    private sealed record ManagedAutopilotPreparation(bool ShouldStartRelay, string StatusMessage);

    private sealed record ManagedControllerResult(bool ShouldContinue, string Message);

    private sealed record ManagerSignalSummary
    {
        [JsonPropertyName("overall_status")]
        public string OverallStatus { get; init; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; init; } = string.Empty;

        [JsonPropertyName("next_action")]
        public string NextAction { get; init; } = string.Empty;

        [JsonPropertyName("resolved_task_slug")]
        public string ResolvedTaskSlug { get; init; } = string.Empty;

        [JsonPropertyName("session_id")]
        public string SessionId { get; init; } = string.Empty;

        [JsonPropertyName("relay_status")]
        public string RelayStatus { get; init; } = string.Empty;

        [JsonPropertyName("suggested_desktop_action")]
        public string SuggestedDesktopAction { get; init; } = string.Empty;

        [JsonPropertyName("wait_should_end")]
        public bool WaitShouldEnd { get; init; }

        [JsonPropertyName("attention_required")]
        public bool AttentionRequired { get; init; }

        [JsonPropertyName("manager_signal_marker")]
        public string ManagerSignalMarker { get; init; } = string.Empty;

        [JsonPropertyName("manager_done_marker")]
        public string ManagerDoneMarker { get; init; } = string.Empty;
    }

    private sealed record ProcessRunResult(int ExitCode, string OutputText, string ErrorText);

    private static string Shorten(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        return text.Length <= maxLength ? text : $"{text[..maxLength]}...";
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }
}
