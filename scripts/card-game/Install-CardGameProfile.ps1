param(
  [string]$WorkingDirectory = 'D:\Unity\card game',
  [switch]$Force
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
$profileRoot = Join-Path $repoRoot 'profiles\card-game'
$appDataDir = Join-Path $env:LOCALAPPDATA 'CodexClaudeRelayMvp'

if (-not (Test-Path $WorkingDirectory)) {
  throw "Working directory not found: $WorkingDirectory"
}

New-Item -ItemType Directory -Force -Path $appDataDir | Out-Null

$brokerSource = Join-Path $profileRoot 'broker.cardgame.json'
$brokerTarget = Join-Path $appDataDir 'broker.json'
if ($Force -or -not (Test-Path $brokerTarget)) {
  Copy-Item -LiteralPath $brokerSource -Destination $brokerTarget -Force
}

$promptPrefix = Get-Content -Raw -LiteralPath (Join-Path $profileRoot 'prompt-prefix.md')
$uiSettings = [ordered]@{
  WorkingDirectory = $WorkingDirectory
  SessionId = ''
  InitialPrompt = $promptPrefix.Trim()
  UseInteractiveAdapters = $false
  AutoApproveAllRequests = $false
}

$uiSettingsTarget = Join-Path $appDataDir 'ui-settings.json'
$uiSettingsJson = $uiSettings | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($uiSettingsTarget, $uiSettingsJson, [System.Text.Encoding]::UTF8)

Write-Host "Installed card-game relay profile."
Write-Host "AppData: $appDataDir"
Write-Host "WorkingDirectory: $WorkingDirectory"
Write-Host "broker.json: $brokerTarget"
Write-Host "ui-settings.json: $uiSettingsTarget"
