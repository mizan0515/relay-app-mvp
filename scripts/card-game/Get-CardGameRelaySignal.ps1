param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

$appDataDir = Join-Path $env:LOCALAPPDATA 'CodexClaudeRelayMvp'
$workspaceSignalPath = Join-Path $CardGameRoot '.autopilot\generated\relay-live-signal.json'
$appDataSignalPath = Join-Path $appDataDir 'auto-logs\relay-live-signal.json'
$resolvedPath = if (Test-Path -LiteralPath $workspaceSignalPath) { $workspaceSignalPath } else { $appDataSignalPath }

if (-not (Test-Path -LiteralPath $resolvedPath)) {
  throw "Relay signal not found. Checked: $workspaceSignalPath and $appDataSignalPath"
}

$signal = Get-Content -Raw -LiteralPath $resolvedPath -Encoding UTF8 | ConvertFrom-Json
$now = [datetimeoffset](Get-Date)
$lastProgressAt = if ($signal.last_progress_at) { [datetimeoffset]$signal.last_progress_at } else { $now }
$lastProgressAgeSeconds = [Math]::Max(0, [int](($now - $lastProgressAt).TotalSeconds))
$relayProcessRunning = [bool](Get-Process CodexClaudeRelay.Desktop -ErrorAction SilentlyContinue)
$derivedStatus = [string]$signal.status
if (-not $relayProcessRunning -and $derivedStatus -eq 'Active' -and $lastProgressAgeSeconds -ge 30) {
  $derivedStatus = 'StaleActive'
}

$summary = [ordered]@{
  signal_path = $resolvedPath
  session_id = [string]$signal.session_id
  status = [string]$signal.status
  derived_status = $derivedStatus
  is_terminal = [bool]$signal.is_terminal
  relay_process_running = $relayProcessRunning
  active_role = [string]$signal.active_role
  current_turn = [int]$signal.current_turn
  last_progress_at = [string]$signal.last_progress_at
  last_progress_age_seconds = $lastProgressAgeSeconds
  watchdog_remaining_seconds = $signal.watchdog_remaining_seconds
  pending_approval_count = [int]$signal.pending_approval_count
  pending_approval = [string]$signal.pending_approval
  last_error = [string]$signal.last_error
  signal_marker = [string]$signal.signal_marker
  done_marker = [string]$signal.done_marker
}

$json = $summary | ConvertTo-Json -Depth 6
if ($OutputPath) {
  $outputDir = Split-Path -Parent $OutputPath
  if ($outputDir) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
  }
  Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
}

Write-Output $json
