param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$ManifestPath = '',
  [string]$OutputJsonPath = '',
  [string]$OutputMarkdownPath = ''
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
$autopilotRoot = Join-Path $CardGameRoot '.autopilot'
$dialogueRoot = Join-Path $CardGameRoot 'Document\dialogue'
$haltPath = Join-Path $autopilotRoot 'HALT'
$operatorDecisionsPath = Join-Path $autopilotRoot 'OPERATOR-DECISIONS.md'
$dadDecisionsPath = Join-Path $dialogueRoot 'DECISIONS.md'
$backlogHealthJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-backlog-health.json'
$completionMarkerPath = Join-Path $repoRoot 'profiles\card-game\generated-last-completed-session.json'
$executionRouteMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-execution-route.md'
$directPromptPath = Join-Path $repoRoot 'profiles\card-game\generated-direct-codex-prompt.md'
$runbookPath = Join-Path $repoRoot 'profiles\card-game\generated-runbook.md'
$opsDashboardPath = Join-Path $repoRoot 'profiles\card-game\generated-ops-dashboard.md'
$relaySignalPath = Join-Path $CardGameRoot '.autopilot\generated\relay-live-signal.json'

if (-not $ManifestPath) {
  $ManifestPath = Join-Path $repoRoot 'profiles\card-game\generated-admission.json'
}

if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-loop-status.json'
}

if (-not $OutputMarkdownPath) {
  $OutputMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-loop-status.md'
}

function Get-DecisionValue {
  param(
    [string]$Path,
    [string]$DecisionName
  )

  if (-not (Test-Path -LiteralPath $Path)) {
    return ''
  }

  $lines = Get-Content -LiteralPath $Path -Encoding UTF8
  $escaped = [regex]::Escape($DecisionName)
  foreach ($line in $lines) {
    if ($line -match "DECISION:\s*$escaped\s+(.+)$") {
      return $Matches[1].Trim().Trim('`')
    }
  }

  return ''
}

function Read-JsonFile {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path)) {
    return $null
  }

  if (-not (Test-Path -LiteralPath $Path)) {
    return $null
  }

  return Get-Content -Raw -LiteralPath $Path -Encoding UTF8 | ConvertFrom-Json
}

function Get-SessionStatePath {
  param(
    [string]$DialogueRoot,
    [string]$SessionId
  )

  if (-not $SessionId) {
    return ''
  }

  $candidate = Join-Path $DialogueRoot "sessions\$SessionId\state.json"
  if (Test-Path -LiteralPath $candidate) {
    return $candidate
  }

  return ''
}

$operatorFocus = Get-DecisionValue -Path $operatorDecisionsPath -DecisionName 'focus'
$evolutionDecision = Get-DecisionValue -Path $operatorDecisionsPath -DecisionName 'evolution'
$dadApproval = Get-DecisionValue -Path $dadDecisionsPath -DecisionName 'approval'
$nextSession = Get-DecisionValue -Path $dadDecisionsPath -DecisionName 'next-session'
$backlogHealth = Read-JsonFile -Path $backlogHealthJsonPath
$manifest = Read-JsonFile -Path $ManifestPath
$completionMarker = Read-JsonFile -Path $completionMarkerPath
$sessionId = if ($manifest) { [string]$manifest.session_id } else { '' }
$sessionStatePath = Get-SessionStatePath -DialogueRoot $dialogueRoot -SessionId $sessionId
$sessionState = Read-JsonFile -Path $sessionStatePath
$relaySignal = Read-JsonFile -Path $relaySignalPath
$alreadyIntegrated = $completionMarker -and $sessionId -and ([string]$completionMarker.session_id -eq $sessionId)
$executionMode = if ($manifest -and $manifest.guidance.execution_mode) { [string]($manifest.guidance.execution_mode.mode) } else { '' }
$executionModeReason = if ($manifest -and $manifest.guidance.execution_mode) { [string]($manifest.guidance.execution_mode.reason) } else { '' }

$nextAction = 'prepare'
$reasons = New-Object System.Collections.Generic.List[string]
$resolvedTaskSlug = ''

if (Test-Path -LiteralPath $haltPath) {
  $nextAction = 'halt'
  $reasons.Add('HALT file is present.')
}
elseif ($sessionState -and
        @('converged','abandoned','stopped','failed') -contains ([string]$sessionState.session_status) -and
        -not $alreadyIntegrated) {
  $nextAction = 'complete'
  $reasons.Add("Session $sessionId is terminal: $($sessionState.session_status).")
}
elseif ($sessionState -and
        @('converged','abandoned','stopped','failed') -contains ([string]$sessionState.session_status) -and
        $alreadyIntegrated) {
  $nextAction = 'prepare'
  $reasons.Add("Session $sessionId was already integrated at $($completionMarker.completed_at).")
}
elseif ($sessionState -and [string]$sessionState.session_status -eq 'active') {
  $nextAction = 'run'
  $reasons.Add("Session $sessionId is active.")
}
elseif ($relaySignal -and [string]$relaySignal.status -eq 'Active') {
  $nextAction = 'run'
  $reasons.Add("Relay live signal reports active session $($relaySignal.session_id) with role $($relaySignal.active_role) at turn $($relaySignal.current_turn).")
}
elseif ($relaySignal -and [string]$relaySignal.status -eq 'Stale') {
  $nextAction = 'prepare'
  $reasons.Add("Relay live signal was normalized to stale for session $($relaySignal.session_id); prepare a fresh session instead of waiting.")
}
elseif ($manifest -and $sessionId -and $executionMode -and $executionMode -ne 'relay-dad') {
  $nextAction = 'route'
  $reasons.Add("Session $sessionId is prepared but routed to $executionMode instead of desktop relay.")
}
elseif ($manifest -and $sessionId) {
  $nextAction = 'run'
  $reasons.Add("Session $sessionId is prepared and awaiting relay execution.")
}

if ($operatorFocus -and $operatorFocus -ne 'none') {
  $resolvedTaskSlug = $operatorFocus
  $reasons.Add("Operator focus overrides backlog: $operatorFocus")
} elseif ($nextSession -and $nextSession -ne 'pending') {
  $resolvedTaskSlug = $nextSession
  $reasons.Add("DAD next-session override: $nextSession")
} elseif ($manifest -and $manifest.task.slug) {
  $resolvedTaskSlug = [string]$manifest.task.slug
} elseif ($backlogHealth -and $backlogHealth.top_item) {
  $resolvedTaskSlug = [string]$backlogHealth.top_item.slug
}

if (($nextAction -eq 'prepare' -or $nextAction -eq 'run') -and
    $backlogHealth -and
    -not $backlogHealth.auto_promotion_safe -and
    -not $resolvedTaskSlug) {
  $nextAction = 'blocked'
  $reasons.Add('Backlog auto-promotion is unsafe and no override task slug is available.')
}

$status = [ordered]@{
  generated_at = (Get-Date).ToString('o')
  card_game_root = $CardGameRoot
  halt_present = (Test-Path -LiteralPath $haltPath)
  next_action = $nextAction
  resolved_task_slug = $resolvedTaskSlug
  execution_mode = $executionMode
  execution_mode_reason = $executionModeReason
  execution_route_path = if (Test-Path -LiteralPath $executionRouteMarkdownPath) { $executionRouteMarkdownPath } else { '' }
  direct_prompt_path = if (Test-Path -LiteralPath $directPromptPath) { $directPromptPath } else { '' }
  runbook_path = if (Test-Path -LiteralPath $runbookPath) { $runbookPath } else { '' }
  ops_dashboard_path = if (Test-Path -LiteralPath $opsDashboardPath) { $opsDashboardPath } else { '' }
  operator_focus = if ($operatorFocus) { $operatorFocus } else { 'none' }
  evolution_decision = if ($evolutionDecision) { $evolutionDecision } else { 'pending' }
  dad_approval = if ($dadApproval) { $dadApproval } else { 'pending' }
  dad_next_session = if ($nextSession) { $nextSession } else { 'pending' }
  session_id = $sessionId
  session_status = if ($sessionState) { [string]$sessionState.session_status } else { '' }
  relay_live_signal_path = if (Test-Path -LiteralPath $relaySignalPath) { $relaySignalPath } else { '' }
  relay_live_signal_status = if ($relaySignal) { [string]$relaySignal.status } else { '' }
  relay_live_signal_session_id = if ($relaySignal) { [string]$relaySignal.session_id } else { '' }
  relay_live_signal_marker = if ($relaySignal) { [string]$relaySignal.signal_marker } else { '' }
  session_already_integrated = [bool]$alreadyIntegrated
  backlog_auto_promotion_safe = if ($backlogHealth) { [bool]$backlogHealth.auto_promotion_safe } else { $false }
  backlog_recommendation = if ($backlogHealth) { [string]$backlogHealth.recommendation } else { 'missing backlog health report' }
  reasons = @($reasons)
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$mdDir = Split-Path -Parent $OutputMarkdownPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($mdDir) { New-Item -ItemType Directory -Force -Path $mdDir | Out-Null }

$status | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Card Game Loop Status')
$lines.Add('')
$lines.Add('Generated at: ' + $status.generated_at)
$lines.Add('Next action: ' + $status.next_action)
$lines.Add('Resolved task slug: ' + $status.resolved_task_slug)
if ($status.execution_mode) {
  $lines.Add('Execution mode: ' + $status.execution_mode)
  $lines.Add('Execution mode reason: ' + $status.execution_mode_reason)
  if ($status.execution_route_path) {
    $lines.Add('Execution route: ' + $status.execution_route_path)
  }
  if ($status.direct_prompt_path) {
    $lines.Add('Direct prompt: ' + $status.direct_prompt_path)
  }
  if ($status.runbook_path) {
    $lines.Add('Runbook: ' + $status.runbook_path)
  }
  if ($status.ops_dashboard_path) {
    $lines.Add('Ops dashboard: ' + $status.ops_dashboard_path)
  }
}
$lines.Add('Session id: ' + $status.session_id)
$lines.Add('Session status: ' + $status.session_status)
if ($status.relay_live_signal_path) {
  $lines.Add('Relay live signal: ' + $status.relay_live_signal_path)
  $lines.Add('Relay live signal status: ' + $status.relay_live_signal_status)
  $lines.Add('Relay live signal marker: ' + $status.relay_live_signal_marker)
}
$lines.Add('Backlog auto-promotion safe: ' + $status.backlog_auto_promotion_safe)
$lines.Add('Backlog recommendation: ' + $status.backlog_recommendation)
$lines.Add('')
$lines.Add('## Reasons')
$lines.Add('')
foreach ($reason in $status.reasons) {
  $lines.Add('- ' + $reason)
}

[string]::Join("`r`n", $lines) | Set-Content -LiteralPath $OutputMarkdownPath -Encoding UTF8

Write-Host "Wrote loop status JSON: $OutputJsonPath"
Write-Host "Wrote loop status Markdown: $OutputMarkdownPath"
