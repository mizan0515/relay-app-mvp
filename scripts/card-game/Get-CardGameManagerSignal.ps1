param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$OutputJsonPath = '',
  [string]$OutputTextPath = ''
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
$generatedRoot = Join-Path $CardGameRoot '.autopilot\generated'
$loopStatusPath = Join-Path $repoRoot 'profiles\card-game\generated-loop-status.json'

if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $generatedRoot 'relay-manager-signal.json'
}

if (-not $OutputTextPath) {
  $OutputTextPath = Join-Path $generatedRoot 'relay-manager-signal.txt'
}

function Read-JsonFile {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
    return $null
  }

  return Get-Content -Raw -LiteralPath $Path -Encoding UTF8 | ConvertFrom-Json
}

if (-not (Test-Path -LiteralPath $loopStatusPath)) {
  & powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Get-CardGameLoopStatus.ps1') `
    -CardGameRoot $CardGameRoot `
    -OutputJsonPath $loopStatusPath | Out-Null
}

$loopStatus = Read-JsonFile -Path $loopStatusPath
$relaySignal = $null
try {
  $relaySignal = & powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Get-CardGameRelaySignal.ps1') -CardGameRoot $CardGameRoot | ConvertFrom-Json
} catch {
  $relaySignal = $null
}

$overallStatus = 'idle'
$reason = 'no_active_relay'
$suggestedDesktopAction = 'prepare_autopilot'
$waitShouldEnd = $true
$success = $false
$attentionRequired = $false

if ($relaySignal -and [string]$relaySignal.status -eq 'Active' -and $relaySignal.relay_process_running) {
  $overallStatus = 'relay_active'
  $reason = 'relay_running'
  $suggestedDesktopAction = 'wait_for_signal'
  $waitShouldEnd = $false
}
elseif ($relaySignal -and [string]$relaySignal.status -eq 'Stale') {
  $overallStatus = 'relay_dead'
  $reason = 'relay_process_missing'
  $suggestedDesktopAction = 'prepare_fresh_session'
  $waitShouldEnd = $true
  $attentionRequired = $true
}
elseif ($loopStatus) {
  switch ([string]$loopStatus.next_action) {
    'run' {
      $overallStatus = 'relay_ready'
      $reason = 'prepared_session_waiting'
      $suggestedDesktopAction = 'run_relay_session'
    }
    'prepare' {
      $overallStatus = 'prepare_next'
      $reason = 'autopilot_ready_to_prepare'
      $suggestedDesktopAction = 'prepare_autopilot'
    }
    'route' {
      $overallStatus = 'route_only'
      $reason = 'execution_mode_selected_non_relay_route'
      $suggestedDesktopAction = 'consume_route_artifact'
      $success = $true
    }
    'complete' {
      $overallStatus = 'completion_pending'
      $reason = 'terminal_session_needs_writeback'
      $suggestedDesktopAction = 'complete_terminal_session'
    }
    'halt' {
      $overallStatus = 'halted'
      $reason = 'halt_file_present'
      $suggestedDesktopAction = 'wait_for_operator'
      $attentionRequired = $true
    }
    'blocked' {
      $overallStatus = 'blocked'
      $reason = 'backlog_or_decision_blocked'
      $suggestedDesktopAction = 'fix_blocker'
      $attentionRequired = $true
    }
  }
}

$sessionId = if ($relaySignal -and $relaySignal.session_id) { [string]$relaySignal.session_id } elseif ($loopStatus) { [string]$loopStatus.session_id } else { '' }
$taskSlug = if ($loopStatus) { [string]$loopStatus.resolved_task_slug } else { '' }
$nextAction = if ($loopStatus) { [string]$loopStatus.next_action } else { '' }
$relayStatus = if ($relaySignal) { [string]$relaySignal.status } else { 'none' }
$relayMarker = if ($relaySignal) { [string]$relaySignal.signal_marker } else { '[RELAY_SIGNAL] status=missing session=(none) turn=0 role=(none) progress_age=unknown watchdog=unknown approvals=0' }
$managerSignalMarker = "[MANAGER_SIGNAL] overall=$overallStatus next=$nextAction session=$(if ($sessionId) { $sessionId } else { '(none)' }) task=$(if ($taskSlug) { $taskSlug } else { '(none)' }) action=$suggestedDesktopAction attention=$($attentionRequired.ToString().ToLowerInvariant())"
$managerDoneMarker = "[MANAGER_DONE] $($waitShouldEnd.ToString().ToLowerInvariant()) success=$($success.ToString().ToLowerInvariant()) reason=$reason"

$summary = [ordered]@{
  generated_at = (Get-Date).ToString('o')
  card_game_root = $CardGameRoot
  overall_status = $overallStatus
  reason = $reason
  next_action = $nextAction
  resolved_task_slug = $taskSlug
  session_id = $sessionId
  relay_status = $relayStatus
  relay_process_running = if ($relaySignal) { [bool]$relaySignal.relay_process_running } else { $false }
  suggested_desktop_action = $suggestedDesktopAction
  wait_should_end = $waitShouldEnd
  success = $success
  attention_required = $attentionRequired
  relay_signal_marker = $relayMarker
  relay_done_marker = if ($relaySignal) { [string]$relaySignal.done_marker } else { '[RELAY_DONE] false status=missing reason=no_signal' }
  manager_signal_marker = $managerSignalMarker
  manager_done_marker = $managerDoneMarker
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$textDir = Split-Path -Parent $OutputTextPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($textDir) { New-Item -ItemType Directory -Force -Path $textDir | Out-Null }

$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
@(
  $managerSignalMarker
  $managerDoneMarker
  $relayMarker
) | Set-Content -LiteralPath $OutputTextPath -Encoding UTF8

Write-Output ($summary | ConvertTo-Json -Depth 6)
