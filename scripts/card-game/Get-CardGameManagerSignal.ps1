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
$governanceStatusPath = Join-Path $repoRoot 'profiles\card-game\generated-governance-status.json'
$promptSurfacePath = Join-Path $repoRoot 'profiles\card-game\generated-prompt-surface-status.json'
$anomalyStatusPath = Join-Path $repoRoot 'profiles\card-game\generated-anomaly-status.json'
$securityPosturePath = Join-Path $repoRoot 'profiles\card-game\generated-security-posture.json'

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
$governanceArgs = @(
  '-ExecutionPolicy', 'Bypass',
  '-File', (Join-Path $scriptRoot 'Get-CardGameGovernanceStatus.ps1'),
  '-CardGameRoot', $CardGameRoot,
  '-OutputJsonPath', $governanceStatusPath
)
if ($loopStatus -and $loopStatus.session_id) {
  $governanceArgs += @('-SessionId', [string]$loopStatus.session_id)
}
try {
  & powershell @governanceArgs | Out-Null
} catch {
}
$governanceStatus = Read-JsonFile -Path $governanceStatusPath
$promptSurface = Read-JsonFile -Path $promptSurfacePath
$anomalyStatus = $null
try {
  & powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Get-CardGameAnomalyStatus.ps1') `
    -CardGameRoot $CardGameRoot `
    -ManifestPath (Join-Path $repoRoot 'profiles\card-game\generated-admission.json') `
    -OutputJsonPath $anomalyStatusPath | Out-Null
} catch {
}
$anomalyStatus = Read-JsonFile -Path $anomalyStatusPath
$securityPosture = $null
try {
  & powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Get-CardGameSecurityPosture.ps1') `
    -CardGameRoot $CardGameRoot `
    -OutputJsonPath $securityPosturePath | Out-Null
} catch {
}
$securityPosture = Read-JsonFile -Path $securityPosturePath
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
$loopSessionId = if ($loopStatus) { [string]$loopStatus.session_id } else { '' }
$relaySessionId = if ($relaySignal) { [string]$relaySignal.session_id } else { '' }
$relayLoopMismatch = $relaySignal -and $loopStatus -and $relaySessionId -and $loopSessionId -and ($relaySessionId -ne $loopSessionId)

if ($relaySignal -and [string]$relaySignal.derived_status -eq 'HungWatchdog') {
  $overallStatus = 'relay_hung'
  $reason = 'relay_watchdog_expired'
  $suggestedDesktopAction = 'prepare_fresh_session'
  $waitShouldEnd = $true
  $attentionRequired = $true
}
elseif ($relayLoopMismatch) {
  $overallStatus = 'relay_session_mismatch'
  $reason = 'active_signal_session_differs_from_prepared_session'
  $suggestedDesktopAction = 'prepare_fresh_session'
  $waitShouldEnd = $true
  $attentionRequired = $true
}
elseif ($relaySignal -and [string]$relaySignal.status -eq 'Active' -and $relaySignal.relay_process_running) {
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
elseif ($relaySignal -and [string]$relaySignal.status -eq 'AwaitingApproval') {
  $overallStatus = 'approval_required'
  $reason = 'relay_waiting_for_approval'
  $suggestedDesktopAction = 'review_pending_approval'
  $waitShouldEnd = $true
  $attentionRequired = $true
}
elseif ($relaySignal -and
        [string]$relaySignal.status -eq 'Paused' -and
        [string]$relaySignal.last_error -eq 'Paused intentionally after one successful relay cycle.') {
  $overallStatus = 'relay_cycle_complete'
  $reason = 'one_successful_cycle_completed'
  $suggestedDesktopAction = 'wait_for_operator'
  $waitShouldEnd = $true
  $success = $true
}
elseif ($relaySignal -and
        [string]$relaySignal.status -eq 'Paused' -and
        -not [string]::IsNullOrWhiteSpace([string]$relaySignal.last_error)) {
  $overallStatus = 'relay_paused_error'
  $reason = 'relay_paused_with_error'
  $suggestedDesktopAction = 'fix_blocker'
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

if ($governanceStatus -and [string]$governanceStatus.status -eq 'blocked') {
  $overallStatus = 'governance_blocked'
  $reason = [string]$governanceStatus.reason
  $suggestedDesktopAction = 'fix_blocker'
  $waitShouldEnd = $true
  $attentionRequired = $true
}

$sessionId = if ($relaySignal -and $relaySignal.session_id) { [string]$relaySignal.session_id } elseif ($loopStatus) { [string]$loopStatus.session_id } else { '' }
$preparedSessionId = if ($loopStatus) { [string]$loopStatus.session_id } else { '' }
$governanceSessionId = if ($governanceStatus) { [string]$governanceStatus.session_id } else { '' }
if ($overallStatus -eq 'governance_blocked') {
  if ($governanceSessionId) {
    $sessionId = $governanceSessionId
  } elseif ($preparedSessionId) {
    $sessionId = $preparedSessionId
  }
}
$taskSlug = if ($loopStatus) { [string]$loopStatus.resolved_task_slug } else { '' }
$nextAction = switch ($overallStatus) {
  'approval_required' { 'blocked' }
  'relay_paused_error' { 'blocked' }
  'relay_session_mismatch' { 'blocked' }
  'relay_hung' { 'blocked' }
  'relay_dead' { 'prepare' }
  'relay_cycle_complete' { 'run' }
  default { if ($loopStatus) { [string]$loopStatus.next_action } else { '' } }
}
$relayStatus = if ($relaySignal) { [string]$relaySignal.status } else { 'none' }
$relayMarker = if ($relaySignal) { [string]$relaySignal.signal_marker } else { '[RELAY_SIGNAL] status=missing session=(none) turn=0 role=(none) progress_age=unknown watchdog=unknown approvals=0' }
$managerSignalMarker = "[MANAGER_SIGNAL] overall=$overallStatus next=$nextAction session=$(if ($sessionId) { $sessionId } else { '(none)' }) task=$(if ($taskSlug) { $taskSlug } else { '(none)' }) action=$suggestedDesktopAction attention=$($attentionRequired.ToString().ToLowerInvariant())"
$managerDoneMarker = "[MANAGER_DONE] $($waitShouldEnd.ToString().ToLowerInvariant()) success=$($success.ToString().ToLowerInvariant()) reason=$reason"
$governanceMarker = if ($governanceStatus) { [string]$governanceStatus.summary_marker } else { '' }
$retryBudgetActive = $governanceStatus -and [bool]$governanceStatus.unity_verification_retry_budget_active
$retryBudgetExhausted = $retryBudgetActive -and
  ([int]$governanceStatus.unity_verification_retry_limit -gt 0) -and
  ([int]$governanceStatus.unity_verification_retries_left -le 0)
$retryBudgetMarker = if ($retryBudgetExhausted) {
  '[RETRY_BUDGET] exhausted unity_verification'
} elseif ($retryBudgetActive -and [int]$governanceStatus.unity_verification_retry_limit -gt 0) {
  "[RETRY_BUDGET] left=$([int]$governanceStatus.unity_verification_retries_left) limit=$([int]$governanceStatus.unity_verification_retry_limit)"
} else {
  ''
}

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
  relay_session_mismatch = [bool]$relayLoopMismatch
  relay_loop_session_id = $loopSessionId
  relay_signal_session_id = $relaySessionId
  suggested_desktop_action = $suggestedDesktopAction
  wait_should_end = $waitShouldEnd
  success = $success
  attention_required = $attentionRequired
  governance_status = if ($governanceStatus) { [string]$governanceStatus.status } else { '' }
  governance_reason = if ($governanceStatus) { [string]$governanceStatus.reason } else { '' }
  governance_marker = if ($governanceStatus) { [string]$governanceStatus.summary_marker } else { '' }
  agent_identity_status = if ($governanceStatus) { [string]$governanceStatus.agent_identity_status } else { '' }
  agent_identity_path = if ($governanceStatus) { [string]$governanceStatus.agent_identity_path } else { '' }
  agent_identity_marker = if ($governanceStatus) { [string]$governanceStatus.agent_identity_marker } else { '' }
  active_agent_identity_ids = if ($governanceStatus) { @($governanceStatus.active_agent_identity_ids) } else { @() }
  missing_agent_identity_ids = if ($governanceStatus) { @($governanceStatus.missing_agent_identity_ids) } else { @() }
  tool_registry_status = if ($governanceStatus) { [string]$governanceStatus.tool_registry_status } else { '' }
  tool_registry_path = if ($governanceStatus) { [string]$governanceStatus.tool_registry_path } else { '' }
  tool_registry_marker = if ($governanceStatus) { [string]$governanceStatus.tool_registry_marker } else { '' }
  active_tool_ids = if ($governanceStatus) { @($governanceStatus.active_tool_ids) } else { @() }
  missing_tool_ids = if ($governanceStatus) { @($governanceStatus.missing_tool_ids) } else { @() }
  policy_registry_status = if ($governanceStatus) { [string]$governanceStatus.policy_registry_status } else { '' }
  policy_registry_path = if ($governanceStatus) { [string]$governanceStatus.policy_registry_path } else { '' }
  policy_registry_marker = if ($governanceStatus) { [string]$governanceStatus.policy_registry_marker } else { '' }
  active_policy_ids = if ($governanceStatus) { @($governanceStatus.active_policy_ids) } else { @() }
  missing_policy_ids = if ($governanceStatus) { @($governanceStatus.missing_policy_ids) } else { @() }
  prompt_surface_status = if ($promptSurface) { [string]$promptSurface.status } else { '' }
  prompt_surface_path = $promptSurfacePath
  prompt_surface_marker = if ($promptSurface) { [string]$promptSurface.summary_marker } else { '' }
  prompt_surface_issues = if ($promptSurface) { @($promptSurface.issues) } else { @() }
  prompt_surface_recommendation = if ($promptSurface) { [string]$promptSurface.recommendation } else { '' }
  anomaly_status = if ($anomalyStatus) { [string]$anomalyStatus.status } else { '' }
  anomaly_path = $anomalyStatusPath
  anomaly_marker = if ($anomalyStatus) { [string]$anomalyStatus.summary_marker } else { '' }
  anomaly_flags = if ($anomalyStatus) { @($anomalyStatus.flags) } else { @() }
  security_posture_risk = if ($securityPosture) { [string]$securityPosture.risk } else { '' }
  security_posture_reason = if ($securityPosture) { [string]$securityPosture.reason } else { '' }
  security_posture_path = $securityPosturePath
  security_posture_marker = if ($securityPosture) { [string]$securityPosture.summary_marker } else { '' }
  blocker_artifact_path = if ($governanceStatus) { [string]$governanceStatus.blocker_artifact_path } else { '' }
  blocker_hint = if ($governanceStatus) { [string]$governanceStatus.blocker_hint } else { '' }
  blocker_detail = if ($governanceStatus) { [string]$governanceStatus.blocker_detail } else { '' }
  recommended_action = if ($governanceStatus) { [string]$governanceStatus.recommended_action } else { '' }
  recommended_action_id = if ($governanceStatus) { [string]$governanceStatus.recommended_action_id } else { '' }
  recommended_action_label = if ($governanceStatus) { [string]$governanceStatus.recommended_action_label } else { '' }
  remediation_status_path = if ($governanceStatus) { [string]$governanceStatus.remediation_status_path } else { '' }
  remediation_report = if ($governanceStatus) { [string]$governanceStatus.remediation_report } else { '' }
  unity_verification_retry_count = if ($governanceStatus) { [int]$governanceStatus.unity_verification_retry_count } else { 0 }
  unity_verification_retry_limit = if ($governanceStatus) { [int]$governanceStatus.unity_verification_retry_limit } else { 0 }
  unity_verification_retries_left = if ($governanceStatus) { [int]$governanceStatus.unity_verification_retries_left } else { 0 }
  unity_verification_retry_budget_active = [bool]$retryBudgetActive
  retry_budget_exhausted = [bool]$retryBudgetExhausted
  retry_budget_marker = $retryBudgetMarker
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
  $governanceMarker
  if ($governanceStatus) { [string]$governanceStatus.agent_identity_marker } else { '' }
  if ($governanceStatus) { [string]$governanceStatus.tool_registry_marker } else { '' }
  if ($governanceStatus) { [string]$governanceStatus.policy_registry_marker } else { '' }
  if ($promptSurface) { [string]$promptSurface.summary_marker } else { '' }
  if ($anomalyStatus) { [string]$anomalyStatus.summary_marker } else { '' }
  if ($securityPosture) { [string]$securityPosture.summary_marker } else { '' }
  $retryBudgetMarker
  $relayMarker
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Set-Content -LiteralPath $OutputTextPath -Encoding UTF8

Write-Output ($summary | ConvertTo-Json -Depth 6)
