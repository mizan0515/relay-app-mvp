#!/usr/bin/env pwsh
# examples/gui-smoke/run-gui-worksession.ps1
# UIA-driven WPF runner for REAL work sessions (not smoke):
#   1. Launch Desktop UI
#   2. Set WorkingDir + SessionId + custom InitialPrompt + AutoApprove
#   3. Check Adapters
#   4. Start Session
#   5. Run one relay cycle by default (2 turns)
#   6. Poll StateSummary for Completed/Paused terminator
#   7. Dump final state + report

param(
    [Parameter(Mandatory = $true)][string]$WorkingDir,
    [Parameter(Mandatory = $true)][string]$InitialPrompt,
    [string]$SessionId = "work-$(Get-Date -Format yyyyMMdd-HHmmss)",
    [string]$ScreenshotDir = '',
    [int]$Turns = 2,
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$appDataDir = Join-Path $env:LOCALAPPDATA 'CodexClaudeRelayMvp'
$signalJsonPath = Join-Path $appDataDir 'auto-logs\relay-live-signal.json'
if (-not $ScreenshotDir) {
    $ScreenshotDir = Join-Path $repoRoot 'scripts\gui-smoke\out-worksession'
}
New-Item -ItemType Directory -Force -Path $ScreenshotDir | Out-Null
$exe = Join-Path $repoRoot 'CodexClaudeRelay.Desktop\bin\Debug\net10.0-windows\CodexClaudeRelay.Desktop.exe'

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
function Toggle-On($el) {
    $tp = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($tp.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $tp.Toggle() }
}
function Wait-AdapterStatusReady($win, [int]$timeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    $statusEl = Find-ByAutomationId $win 'AdapterStatusTextBlock'
    while ((Get-Date) -lt $deadline) {
        $status = Get-Text $statusEl
        if (
            -not [string]::IsNullOrWhiteSpace($status) -and
            $status -notmatch 'Status not checked yet\.' -and
            $status -notmatch '^Checking adapter health\.\.\.$'
        ) {
            return $status
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Adapter status did not finish refreshing within $timeoutSec s"
}
function Invoke-TurnPlan($win, [int]$turns) {
    if ($turns -le 0) {
        throw 'Turns must be greater than zero.'
    }

    if ($turns -eq 2) {
        $cycleButton = Find-ByAutomationId $win 'RunCycleButton'
        Click-Button $cycleButton
        return
    }

    if ($turns -eq 4) {
        $autoRunButton = Find-ByAutomationId $win 'AutoRunButton'
        Click-Button $autoRunButton
        return
    }

    for ($i = 0; $i -lt $turns; $i++) {
        Click-Button (Find-ByAutomationId $win 'AdvanceButton')
    }
}
function Read-RelaySignal() {
    if (-not (Test-Path -LiteralPath $signalJsonPath)) {
        return $null
    }

    try {
        return Get-Content -Raw -LiteralPath $signalJsonPath -Encoding UTF8 | ConvertFrom-Json
    } catch {
        return $null
    }
}
function Format-RelaySignal($signal) {
    if ($null -eq $signal) {
        return '[RELAY_SIGNAL] unavailable'
    }

    $watchdog = if ($null -ne $signal.watchdog_remaining_seconds) { "$($signal.watchdog_remaining_seconds)s" } else { 'inactive' }
    return "[RELAY_SIGNAL] status=$($signal.status) session=$($signal.session_id) turn=$($signal.current_turn) role=$($signal.active_role) progress_age=$($signal.last_progress_age_seconds)s watchdog=$watchdog approvals=$($signal.pending_approval_count)"
}

Write-Host "=== UIA GUI worksession @ $(Get-Date -Format o) ==="
Write-Host "workingDir:    $WorkingDir"
Write-Host "sessionId:     $SessionId"
Write-Host "initialPrompt: $($InitialPrompt.Substring(0, [Math]::Min(120, $InitialPrompt.Length)))..."
Write-Host "turns:         $Turns"
Write-Host "shots:         $ScreenshotDir"

if (-not (Test-Path $exe)) { throw "exe not built: $exe" }

Get-Process CodexClaudeRelay.Desktop -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "[1/6] launching..."
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
$win = Find-Window 'Relay App MVP' 30
Save-Screen 'after-launch'

Write-Host "[2/6] configuring..."
Set-Text (Find-ByAutomationId $win 'WorkingDirectoryTextBox') $WorkingDir
Set-Text (Find-ByAutomationId $win 'SessionIdTextBox') $SessionId
Set-Text (Find-ByAutomationId $win 'InitialPromptTextBox') $InitialPrompt
Toggle-On (Find-ByAutomationId $win 'AutoApproveAllRequestsCheckBox')
Start-Sleep -Milliseconds 500
Save-Screen 'after-config'

Write-Host "[3/6] Check Adapters..."
Click-Button (Find-ByAutomationId $win 'CheckAdaptersButton')
$adapter = Wait-AdapterStatusReady $win 120
Write-Host "--- adapter status ---`n$adapter`n----------------------"
Save-Screen 'after-check-adapters'

Write-Host "[4/6] Start Session..."
Click-Button (Find-ByAutomationId $win 'StartSessionButton')
Start-Sleep -Seconds 6
Save-Screen 'after-start'

Write-Host "[5/6] running $Turns relay turn(s)..."
Invoke-TurnPlan $win $Turns

Write-Host "[6/6] polling state (timeout=${TimeoutSeconds}s)..."
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$lastSignalMarker = ''
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 6
    $signal = Read-RelaySignal
    if ($null -eq $signal) {
        continue
    }

    $signalMarker = Format-RelaySignal $signal
    if ($signalMarker -ne $lastSignalMarker) {
        Write-Host "  [poll] $signalMarker"
        $lastSignalMarker = $signalMarker
    }

    if ($signal.status -match 'Paused|Completed|Failed|Error|Stopped' -or $signal.is_terminal -eq $true) {
        Write-Host "  [poll] terminal status: $($signal.status)"
        Start-Sleep -Seconds 2
        break
    }
}
Save-Screen 'after-autorun'

Write-Host ""
Write-Host "=== RelaySignal ==="
$finalSignal = Read-RelaySignal
if ($null -ne $finalSignal) {
    $finalSignal | ConvertTo-Json -Depth 6 | Write-Host
} else {
    Write-Host '{"signal":"unavailable"}'
}
Write-Host ""
Write-Host "=== AdapterStatus (final) ==="
Get-Text (Find-ByAutomationId $win 'AdapterStatusTextBlock') | Write-Host

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Host ""
Write-Host "=== WORKSESSION: finished ==="
