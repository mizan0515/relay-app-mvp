param(
  [Parameter(Mandatory = $true)]
  [string]$PrimarySignalJsonPath,
  [string]$MirroredSignalJsonPath = '',
  [Parameter(Mandatory = $true)]
  [int]$SourcePid,
  [Parameter(Mandatory = $true)]
  [string]$SourceProcessStartedAt,
  [string]$CardGameRoot = 'D:\Unity\card game',
  [int]$PollSeconds = 5,
  [int]$MaxWatchSeconds = 7200
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$managerSignalScriptPath = Join-Path $scriptRoot 'Get-CardGameManagerSignal.ps1'
$watchStartedAt = Get-Date

function Test-SourceProcessLive {
  param(
    [int]$ProcessIdToCheck,
    [string]$StartedAt
  )

  if ($ProcessIdToCheck -le 0 -or [string]::IsNullOrWhiteSpace($StartedAt)) {
    return $false
  }

  try {
    $process = Get-Process -Id $ProcessIdToCheck -ErrorAction Stop
    return $process.StartTime.ToUniversalTime().ToString('o') -eq $StartedAt
  } catch {
    return $false
  }
}

function Read-SignalObject {
  param([string]$SignalPath)

  if ([string]::IsNullOrWhiteSpace($SignalPath) -or -not (Test-Path -LiteralPath $SignalPath)) {
    return $null
  }

  try {
    return Get-Content -Raw -LiteralPath $SignalPath -Encoding UTF8 | ConvertFrom-Json
  } catch {
    return $null
  }
}

function Write-StaleSignal {
  param(
    [string]$SignalPath,
    [object]$Signal
  )

  if ([string]::IsNullOrWhiteSpace($SignalPath)) {
    return
  }

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
    source_pid = $SourcePid
    source_process_started_at = $SourceProcessStartedAt
    heartbeat_at = $generatedAt
    heartbeat_max_age_seconds = 30
  }

  $directory = Split-Path -Parent $SignalPath
  if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
  }

  $normalized | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $SignalPath -Encoding UTF8
  @(
    $normalized.signal_marker
    $normalized.done_marker
  ) | Set-Content -LiteralPath ([System.IO.Path]::ChangeExtension($SignalPath, '.txt')) -Encoding UTF8
}

while (((Get-Date) - $watchStartedAt).TotalSeconds -lt $MaxWatchSeconds) {
  $signal = Read-SignalObject -SignalPath $PrimarySignalJsonPath
  if ($null -eq $signal) {
    break
  }

  $status = [string]$signal.status
  $signalPid = if ($signal.PSObject.Properties.Name -contains 'source_pid' -and $signal.source_pid) { [int]$signal.source_pid } else { 0 }
  $signalStartedAt = if ($signal.PSObject.Properties.Name -contains 'source_process_started_at' -and $signal.source_process_started_at) { [string]$signal.source_process_started_at } else { '' }

  if ($status -ne 'Active') {
    break
  }

  if ($signalPid -ne $SourcePid -or $signalStartedAt -ne $SourceProcessStartedAt) {
    break
  }

  if (-not (Test-SourceProcessLive -ProcessIdToCheck $SourcePid -StartedAt $SourceProcessStartedAt)) {
    Write-StaleSignal -SignalPath $PrimarySignalJsonPath -Signal $signal

    if ($MirroredSignalJsonPath) {
      $mirroredSignal = Read-SignalObject -SignalPath $MirroredSignalJsonPath
      if ($null -eq $mirroredSignal) {
        $mirroredSignal = $signal
      }

      Write-StaleSignal -SignalPath $MirroredSignalJsonPath -Signal $mirroredSignal
    }

    if (Test-Path -LiteralPath $managerSignalScriptPath) {
      try {
        & powershell -ExecutionPolicy Bypass -File $managerSignalScriptPath -CardGameRoot $CardGameRoot | Out-Null
      } catch {
      }
    }

    break
  }

  Start-Sleep -Seconds $PollSeconds
}
