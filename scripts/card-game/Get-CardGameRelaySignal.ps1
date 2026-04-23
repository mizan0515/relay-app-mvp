param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

$appDataDir = Join-Path $env:LOCALAPPDATA 'CodexClaudeRelayMvp'
$workspaceSignalPath = Join-Path $CardGameRoot '.autopilot\generated\relay-live-signal.json'
$appDataSignalPath = Join-Path $appDataDir 'auto-logs\relay-live-signal.json'
$resolvedPath = if (Test-Path -LiteralPath $workspaceSignalPath) { $workspaceSignalPath } else { $appDataSignalPath }

function Test-RelaySignalProcessLive {
  param(
    [object]$Signal
  )

  $sourcePid = 0
  if ($Signal.PSObject.Properties.Name -contains 'source_pid' -and $Signal.source_pid) {
    $sourcePid = [int]$Signal.source_pid
  }

  $sourceStartedAt = ''
  if ($Signal.PSObject.Properties.Name -contains 'source_process_started_at' -and $Signal.source_process_started_at) {
    $sourceStartedAt = [string]$Signal.source_process_started_at
  }

  if ($sourcePid -le 0 -or [string]::IsNullOrWhiteSpace($sourceStartedAt)) {
    return $false
  }

  try {
    $process = Get-Process -Id $sourcePid -ErrorAction Stop
    return $process.StartTime.ToUniversalTime().ToString('o') -eq $sourceStartedAt
  } catch {
    return $false
  }
}

function Write-StaleRelaySignal {
  param(
    [string]$SignalPath,
    [object]$Signal
  )

  $generatedAt = (Get-Date).ToUniversalTime().ToString('o')
  $sessionId = if ($Signal.session_id) { [string]$Signal.session_id } else { '' }
  $activeRole = if ($Signal.active_role) { [string]$Signal.active_role } else { '(none)' }
  $currentTurn = if ($Signal.current_turn) { [int]$Signal.current_turn } else { 0 }
  $lastProgressAt = if ($Signal.last_progress_at) { [string]$Signal.last_progress_at } else { $generatedAt }
  $reason = 'relay_process_missing'
  $normalized = [ordered]@{
    generated_at = $generatedAt
    session_id = $sessionId
    status = 'Stale'
    is_terminal = $true
    active_role = $activeRole
    current_turn = $currentTurn
    last_progress_at = $lastProgressAt
    last_progress_age_seconds = 0
    watchdog_remaining_seconds = 0
    pending_approval_count = 0
    pending_approval = ''
    last_error = $reason
    signal_marker = "[RELAY_SIGNAL] status=stale session=$(if ($sessionId) { $sessionId } else { '(none)' }) turn=$currentTurn role=$activeRole progress_age=00:00 watchdog=disabled approvals=0"
    done_marker = "[RELAY_DONE] true status=stale reason=$reason"
    event_log_path = if ($Signal.event_log_path) { [string]$Signal.event_log_path } else { '' }
    source_pid = if ($Signal.source_pid) { [int]$Signal.source_pid } else { 0 }
    source_process_started_at = if ($Signal.source_process_started_at) { [string]$Signal.source_process_started_at } else { '' }
    heartbeat_at = $generatedAt
    heartbeat_max_age_seconds = 30
  }

  $normalized | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $SignalPath -Encoding UTF8
  $textPath = [System.IO.Path]::ChangeExtension($SignalPath, '.txt')
  @(
    $normalized.signal_marker
    $normalized.done_marker
  ) | Set-Content -LiteralPath $textPath -Encoding UTF8

  return $normalized
}

if (-not (Test-Path -LiteralPath $resolvedPath)) {
  throw "Relay signal not found. Checked: $workspaceSignalPath and $appDataSignalPath"
}

$signal = Get-Content -Raw -LiteralPath $resolvedPath -Encoding UTF8 | ConvertFrom-Json
$signalProcessLive = Test-RelaySignalProcessLive -Signal $signal
$status = [string]$signal.status
if ($status -eq 'Active' -and -not $signalProcessLive) {
  $signal = Write-StaleRelaySignal -SignalPath $resolvedPath -Signal $signal
  $status = [string]$signal.status
}
$now = [datetimeoffset](Get-Date)
$lastProgressAt = if ($signal.last_progress_at) { [datetimeoffset]$signal.last_progress_at } else { $now }
$lastProgressAgeSeconds = [Math]::Max(0, [int](($now - $lastProgressAt).TotalSeconds))
$relayProcessRunning = [bool]$signalProcessLive
$derivedStatus = $status
if (-not $relayProcessRunning -and $derivedStatus -eq 'Active' -and $lastProgressAgeSeconds -ge 30) {
  $derivedStatus = 'StaleActive'
}

$summary = [ordered]@{
  signal_path = $resolvedPath
  session_id = [string]$signal.session_id
  status = $status
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
