param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [int]$TimeoutSeconds = 1800,
  [int]$PollSeconds = 10,
  [int]$StaleActiveSeconds = 30,
  [int]$WatchdogExpiryGraceSeconds = 30,
  [string[]]$TerminalStatuses = @('Paused', 'Completed', 'Failed', 'Error', 'Stopped', 'Stale')
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$lastMarker = ''

while ((Get-Date) -lt $deadline) {
  try {
    $signalJson = & powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Get-CardGameRelaySignal.ps1') -CardGameRoot $CardGameRoot
    $signal = $signalJson | ConvertFrom-Json
  } catch {
    Start-Sleep -Seconds $PollSeconds
    continue
  }

  if ($signal.signal_marker -and $signal.signal_marker -ne $lastMarker) {
    Write-Host $signal.signal_marker
    $lastMarker = [string]$signal.signal_marker
  }

  if (([string]$signal.derived_status -eq 'StaleActive' -and [int]$signal.last_progress_age_seconds -ge $StaleActiveSeconds) -or
      [string]$signal.derived_status -eq 'HungWatchdog' -or
      [string]$signal.status -eq 'Stale') {
    if ([string]$signal.derived_status -eq 'HungWatchdog') {
      Write-Host "[RELAY_DONE] true status=hung reason=watchdog_expired"
    } else {
      Write-Host "[RELAY_DONE] true status=stale reason=relay_process_missing"
    }
    exit 1
  }

  if ($signal.is_terminal -or $TerminalStatuses -contains [string]$signal.status) {
    Write-Host $signal.done_marker
    exit 0
  }

  Start-Sleep -Seconds $PollSeconds
}

Write-Host "[RELAY_DONE] false status=timeout reason=watcher_timeout"
exit 1
