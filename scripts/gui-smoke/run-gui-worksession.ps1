#!/usr/bin/env pwsh
# examples/gui-smoke/run-gui-worksession.ps1
# UIA-driven WPF runner for REAL work sessions (not smoke):
#   1. Launch Desktop UI
#   2. Set WorkingDir + SessionId + custom InitialPrompt + AutoApprove
#   3. Check Adapters
#   4. Start Session
#   5. Auto Run 4 turns
#   6. Poll StateSummary for Completed/Paused terminator
#   7. Dump final state + report

param(
    [Parameter(Mandatory = $true)][string]$WorkingDir,
    [Parameter(Mandatory = $true)][string]$InitialPrompt,
    [string]$SessionId = "work-$(Get-Date -Format yyyyMMdd-HHmmss)",
    [string]$ScreenshotDir = 'D:\codex-claude-relay\scripts\gui-smoke\out-worksession',
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

New-Item -ItemType Directory -Force -Path $ScreenshotDir | Out-Null
$exe = 'D:\codex-claude-relay\CodexClaudeRelay.Desktop\bin\Debug\net10.0-windows\CodexClaudeRelay.Desktop.exe'

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

Write-Host "=== UIA GUI worksession @ $(Get-Date -Format o) ==="
Write-Host "workingDir:    $WorkingDir"
Write-Host "sessionId:     $SessionId"
Write-Host "initialPrompt: $($InitialPrompt.Substring(0, [Math]::Min(120, $InitialPrompt.Length)))..."
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
Start-Sleep -Seconds 8
$adapter = Get-Text (Find-ByAutomationId $win 'AdapterStatusTextBlock')
Write-Host "--- adapter status ---`n$adapter`n----------------------"
Save-Screen 'after-check-adapters'

Write-Host "[4/6] Start Session..."
Click-Button (Find-ByAutomationId $win 'StartSessionButton')
Start-Sleep -Seconds 6
Save-Screen 'after-start'

Write-Host "[5/6] Auto Run 4..."
Click-Button (Find-ByAutomationId $win 'AutoRunButton')

Write-Host "[6/6] polling state (timeout=${TimeoutSeconds}s)..."
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$stateEl = Find-ByAutomationId $win 'StateSummaryTextBlock'
$lastTurn = -1
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 6
    $state = Get-Text $stateEl
    $turnMatch = [regex]::Match($state, 'Current turn:\s*(\d+)')
    $statusMatch = [regex]::Match($state, 'Status:\s*(\w+)')
    $turn = if ($turnMatch.Success) { [int]$turnMatch.Groups[1].Value } else { -1 }
    $status = if ($statusMatch.Success) { $statusMatch.Groups[1].Value } else { '?' }
    if ($turn -ne $lastTurn) {
        Write-Host "  [poll] turn=$turn status=$status"
        $lastTurn = $turn
    }
    if ($status -match 'Paused|Completed|Failed|Error|Stopped') {
        Write-Host "  [poll] terminal status: $status"
        Start-Sleep -Seconds 2
        break
    }
}
Save-Screen 'after-autorun'

Write-Host ""
Write-Host "=== StateSummary ==="
Get-Text (Find-ByAutomationId $win 'StateSummaryTextBlock') | Write-Host
Write-Host ""
Write-Host "=== AdapterStatus (final) ==="
Get-Text (Find-ByAutomationId $win 'AdapterStatusTextBlock') | Write-Host

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Host ""
Write-Host "=== WORKSESSION: finished ==="
