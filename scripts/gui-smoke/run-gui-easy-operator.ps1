#!/usr/bin/env pwsh

param(
    [string]$CardGameRoot = 'D:\Unity\card game',
    [string]$TaskSlug = 'companion-depth-first-slice',
    [string]$ScreenshotDir = '',
    [int]$TimeoutSeconds = 300,
    [switch]$InjectRelayDeathOnce
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $ScreenshotDir) {
    $ScreenshotDir = Join-Path $repoRoot 'scripts\gui-smoke\out-easy-operator'
}

New-Item -ItemType Directory -Force -Path $ScreenshotDir | Out-Null
$exe = Join-Path $repoRoot 'CodexClaudeRelay.Desktop\bin\Debug\net10.0-windows\CodexClaudeRelay.Desktop.exe'
$managerSignalPath = Join-Path $CardGameRoot '.autopilot\generated\relay-manager-signal.json'
$liveSignalPath = Join-Path $CardGameRoot '.autopilot\generated\relay-live-signal.json'

function Save-Screen([string]$tag) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $path = Join-Path $ScreenshotDir ("{0:yyyyMMdd-HHmmss}-{1}.png" -f (Get-Date), $tag)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "[shot] $path"
}

function Find-Window([string]$title, [int]$timeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $title)
        $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($w) { return $w }
        Start-Sleep -Milliseconds 500
    }
    throw "Window '$title' not found within $timeoutSec s"
}

function Find-ByAutomationId($parent, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $el = $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $el) { throw "AutomationId '$id' not found" }
    return $el
}

function Set-Text($el, [string]$text) {
    $vp = $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $vp.SetValue($text)
}

function Get-Text($el) {
    $vp = $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    return $vp.Current.Value
}

function Wait-Enabled($el, [int]$timeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($el.Current.IsEnabled) { return }
        Start-Sleep -Milliseconds 500
    }
    throw "Element did not become enabled within $timeoutSec s"
}

function Click-Button($el) {
    Wait-Enabled $el 120
    $ip = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $ip.Invoke()
}

function Wait-TextChange($parent, [string]$automationId, [string]$initialText, [int]$timeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    $el = Find-ByAutomationId $parent $automationId
    while ((Get-Date) -lt $deadline) {
        $current = Get-Text $el
        if (-not [string]::Equals($current, $initialText, [System.StringComparison]::Ordinal)) {
            return $current
        }

        Start-Sleep -Milliseconds 500
    }

    return Get-Text $el
}

function Read-ManagerSignal() {
    if (-not (Test-Path -LiteralPath $managerSignalPath)) {
        return $null
    }

    try {
        return Get-Content -Raw -LiteralPath $managerSignalPath -Encoding UTF8 | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Write-InjectedRelayDeath($signal) {
    if ($null -eq $signal) {
        return
    }

    $sessionId = [string]$signal.session_id
    if (-not $sessionId) {
        return
    }

    $now = (Get-Date).ToString('o')
    $taskSlug = if ($signal.resolved_task_slug) { [string]$signal.resolved_task_slug } else { $TaskSlug }
    $relayMarker = "[RELAY_SIGNAL] status=stale session=$sessionId turn=2 role=claude-code progress_age=00:00 watchdog=disabled approvals=0"
    $relayDoneMarker = "[RELAY_DONE] true status=stale reason=relay_process_missing"
    $managerMarker = "[MANAGER_SIGNAL] overall=relay_dead next=prepare session=$sessionId task=$taskSlug action=prepare_fresh_session attention=true"
    $managerDoneMarker = "[MANAGER_DONE] true success=false reason=relay_process_missing"

    $liveSignal = [ordered]@{
        generated_at = $now
        session_id = $sessionId
        status = 'Stale'
        is_terminal = $true
        active_role = 'claude-code'
        current_turn = 2
        last_progress_at = $now
        last_progress_age_seconds = 0
        watchdog_remaining_seconds = 0
        pending_approval_count = 0
        pending_approval = ''
        last_error = 'relay_process_missing'
        signal_marker = $relayMarker
        done_marker = $relayDoneMarker
        event_log_path = ''
        source_pid = 0
        source_process_started_at = $now
        heartbeat_at = $now
        heartbeat_max_age_seconds = 30
    }

    $managerSignal = [ordered]@{
        generated_at = $now
        card_game_root = $CardGameRoot
        overall_status = 'relay_dead'
        reason = 'relay_process_missing'
        next_action = 'prepare'
        resolved_task_slug = $taskSlug
        session_id = $sessionId
        relay_status = 'Stale'
        relay_process_running = $false
        suggested_desktop_action = 'prepare_fresh_session'
        wait_should_end = $true
        success = $false
        attention_required = $true
        relay_signal_marker = $relayMarker
        relay_done_marker = $relayDoneMarker
        manager_signal_marker = $managerMarker
        manager_done_marker = $managerDoneMarker
    }

    $liveSignal | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $liveSignalPath -Encoding UTF8
    $managerSignal | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $managerSignalPath -Encoding UTF8
    Write-Host "  [inject] simulated relay death for session $sessionId"
}

Write-Host "=== UIA easy operator @ $(Get-Date -Format o) ==="
Write-Host "cardGameRoot: $CardGameRoot"
Write-Host "taskSlug:     $TaskSlug"
Write-Host "shots:        $ScreenshotDir"

if (-not (Test-Path $exe)) { throw "exe not built: $exe" }

Get-Process CodexClaudeRelay.Desktop -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
$win = Find-Window 'Relay App MVP' 30
Save-Screen 'after-launch'

Set-Text (Find-ByAutomationId $win 'ManagedCardGameRootTextBox') $CardGameRoot
Set-Text (Find-ByAutomationId $win 'ManagedTaskSlugTextBox') $TaskSlug
Set-Text (Find-ByAutomationId $win 'ManagedTurnsTextBox') '2'
Save-Screen 'after-config'

$initialEasyStatus = Get-Text (Find-ByAutomationId $win 'EasyStatusTextBox')
$initialManagedStatus = Get-Text (Find-ByAutomationId $win 'ManagedStatusTextBox')

Click-Button (Find-ByAutomationId $win 'EasyStartButton')

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$lastMarker = ''
$injectedDeath = $false
while ((Get-Date) -lt $deadline) {
    if ($proc.HasExited) {
        Write-Host "  [poll] desktop process exited early"
        break
    }

    Start-Sleep -Seconds 3
    $signal = Read-ManagerSignal
    if ($null -eq $signal) {
        if ((Find-ByAutomationId $win 'EasyStartButton').Current.IsEnabled) {
            break
        }
        continue
    }

    $marker = $signal.manager_signal_marker
    if ($marker -ne $lastMarker) {
        Write-Host "  [poll] $marker"
        $lastMarker = $marker
    }

    if ($InjectRelayDeathOnce -and -not $injectedDeath -and [string]$signal.overall_status -eq 'relay_active') {
        Write-InjectedRelayDeath $signal
        $injectedDeath = $true
        continue
    }

    $statusMessage = Get-Text (Find-ByAutomationId $win 'StatusMessageTextBlock')
    if ($InjectRelayDeathOnce -and $statusMessage -like 'Easy Start is retrying automatically*') {
        Write-Host "  [poll] automatic retry detected"
        break
    }

    if ((Find-ByAutomationId $win 'EasyStartButton').Current.IsEnabled) {
        Write-Host "  [poll] easy start finished"
        break
    }
}

if ((Get-Date) -ge $deadline) {
    $signal = Read-ManagerSignal
    if ($null -ne $signal) {
        Write-Host "  [poll] timeout while manager status was $($signal.overall_status) / $($signal.suggested_desktop_action)"
    } else {
        Write-Host "  [poll] timeout while manager status was unavailable"
    }
}

Save-Screen 'after-easy-start'

Write-Host ""
Write-Host "=== EasyStatus ==="
$easyStatus = Wait-TextChange $win 'EasyStatusTextBox' $initialEasyStatus 20
$easyStatus | Write-Host
Write-Host ""
Write-Host "=== ManagedStatus ==="
$managedStatus = Wait-TextChange $win 'ManagedStatusTextBox' $initialManagedStatus 20
$managedStatus | Write-Host
Write-Host ""
Write-Host "=== StatusMessage ==="
$statusMessage = Get-Text (Find-ByAutomationId $win 'StatusMessageTextBlock')
$statusMessage | Write-Host

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
