#!/usr/bin/env pwsh
# tools/uia-gui-smoke.ps1
# Drives the WPF Desktop UI via Windows UI Automation (no MCP needed):
#   1. Launch CodexClaudeRelay.Desktop.exe
#   2. Find the "Relay App MVP" window
#   3. Set WorkingDirectory + check Auto-Approve
#   4. Click "Smoke Test 2" (deterministic 2-turn smoke prompt)
#   5. Wait for completion (poll busy overlay)
#   6. Read state summary + smoke report and dump to console
#   7. Take screenshots before/after for evidence
# Exit code 0 on smoke success, 1 on failure.

param(
    [string]$WorkingDir = 'D:\dad-test-project',
    [string]$ScreenshotDir = '',
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $ScreenshotDir) {
    $ScreenshotDir = Join-Path $repoRoot 'scripts\gui-smoke\out-smoke'
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
    Wait-Enabled $el 60
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

Write-Host "=== UIA GUI smoke @ $(Get-Date -Format o) ==="
Write-Host "exe:        $exe"
Write-Host "workingDir: $WorkingDir"
Write-Host "shots:      $ScreenshotDir"
Write-Host ""

if (-not (Test-Path $exe)) { throw "exe not built: $exe" }

# Kill any pre-existing instance
Get-Process CodexClaudeRelay.Desktop -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "[1/7] launching..."
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4

Write-Host "[2/7] finding window..."
$win = Find-Window 'Relay App MVP' 30
Save-Screen 'after-launch'

Write-Host "[3/7] setting WorkingDirectory + AutoApprove..."
$wdBox  = Find-ByAutomationId $win 'WorkingDirectoryTextBox'
Set-Text $wdBox $WorkingDir

$approveCb = Find-ByAutomationId $win 'AutoApproveAllRequestsCheckBox'
Toggle-On $approveCb
Start-Sleep -Milliseconds 500
Save-Screen 'after-config'

Write-Host "[4/7] clicking Check Adapters..."
$checkBtn = Find-ByAutomationId $win 'CheckAdaptersButton'
Click-Button $checkBtn
Save-Screen 'after-check-adapters'
$adapter = Wait-AdapterStatusReady $win 120
Write-Host "--- adapter status ---"
Write-Host $adapter
Write-Host "----------------------"

Write-Host "[5/7] clicking Smoke Test 2..."
$smokeBtn = Find-ByAutomationId $win 'SmokeTestButton'
Click-Button $smokeBtn

Write-Host "[6/7] waiting for completion (timeout=${TimeoutSeconds}s)..."
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$reportEl = Find-ByAutomationId $win 'SmokeTestReportTextBlock'
$lastReport = ''
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 4
    $r = Get-Text $reportEl
    if ($r -ne $lastReport) {
        $oneLine = ($r -replace '\s+', ' ').Trim()
        if ($oneLine.Length -gt 140) { $oneLine = $oneLine.Substring(0, 140) + '...' }
        Write-Host "  [poll] $oneLine"
        $lastReport = $r
    }
    if ($r -match 'PASS|FAIL|success|failed|completed|error|closed') {
        Write-Host "  [poll] terminator detected"
        Start-Sleep -Seconds 2
        break
    }
}
Save-Screen 'after-smoke'

Write-Host "[7/7] reading results..."
$state  = Get-Text (Find-ByAutomationId $win 'StateSummaryTextBlock')
$report = Get-Text (Find-ByAutomationId $win 'SmokeTestReportTextBlock')
Write-Host ""
Write-Host "=== StateSummary ==="
Write-Host $state
Write-Host ""
Write-Host "=== SmokeTestReport ==="
Write-Host $report
Write-Host ""

$success = $report -match 'PASS|success|completed'
if ($success) {
    Write-Host "=== GUI SMOKE: SUCCESS ==="
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    exit 0
} else {
    Write-Host "=== GUI SMOKE: FAILED (no PASS/success token in report) ==="
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    exit 1
}
