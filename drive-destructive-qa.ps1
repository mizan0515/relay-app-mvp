$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$sessionId = 'auto-approve-push-qa-20260417-143000'
$logPath = Join-Path $env:LOCALAPPDATA "RelayAppMvp\logs\$sessionId.jsonl"

function Find-AppWindow {
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Relay App MVP')
        $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($null -ne $win) { return $win }
        Start-Sleep -Milliseconds 500
    }
    throw 'Relay App MVP window not found'
}

function Get-ButtonByName([System.Windows.Automation.AutomationElement]$root, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-Button([System.Windows.Automation.AutomationElement]$btn) {
    $pattern = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Wait-ButtonEnabled([System.Windows.Automation.AutomationElement]$root, [string]$name, [int]$timeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $btn = Get-ButtonByName $root $name
        if ($null -ne $btn -and $btn.Current.IsEnabled) { return $btn }
        Start-Sleep -Milliseconds 500
    }
    throw "Button '$name' did not become enabled within $timeoutSec s"
}

Write-Host "Waiting for window..."
$win = Find-AppWindow
Write-Host "Window found. Clicking Start Session..."
$start = Wait-ButtonEnabled $win 'Start Session' 30
Invoke-Button $start
Start-Sleep -Seconds 5

Write-Host "Waiting for Advance Once to be enabled..."
$advance = Wait-ButtonEnabled $win 'Advance Once' 60
Write-Host "Clicking Advance Once..."
Invoke-Button $advance

Write-Host "Waiting up to 5 minutes for turn to complete (watching log)..."
$deadline = (Get-Date).AddMinutes(5)
while ((Get-Date) -lt $deadline) {
    if (Test-Path $logPath) {
        $content = Get-Content $logPath -Raw -ErrorAction SilentlyContinue
        if ($content -match '"type":"handoff\.accepted"' -or $content -match '"type":"turn\.completed"' -or $content -match '"type":"turn\.failed"') {
            Write-Host "Turn finished."
            break
        }
    }
    Start-Sleep -Seconds 5
}

Write-Host "Done. Log at: $logPath"
