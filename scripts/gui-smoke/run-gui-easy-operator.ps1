#!/usr/bin/env pwsh

param(
    [string]$CardGameRoot = 'D:\Unity\card game',
    [string]$TaskSlug = 'companion-depth-first-slice',
    [string]$ScreenshotDir = '',
    [int]$TimeoutSeconds = 300
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
while ((Get-Date) -lt $deadline) {
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

    if ((Find-ByAutomationId $win 'EasyStartButton').Current.IsEnabled) {
        Write-Host "  [poll] easy start finished"
        break
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
