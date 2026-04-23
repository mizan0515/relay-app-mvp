param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$LoopStatusPath = '',
  [string]$ContextSurfacePath = '',
  [string]$ExecutionRoutePath = '',
  [string]$SkillResolverPath = '',
  [string]$HeuristicsPath = '',
  [string]$OutputJsonPath = '',
  [string]$OutputMarkdownPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$statePath = Join-Path $CardGameRoot '.autopilot\STATE.md'
$backlogPath = Join-Path $CardGameRoot '.autopilot\BACKLOG.md'

if (-not $LoopStatusPath) {
  $LoopStatusPath = Join-Path $repoRoot 'profiles\card-game\generated-loop-status.json'
}
if (-not $ContextSurfacePath) {
  $ContextSurfacePath = Join-Path $repoRoot 'profiles\card-game\generated-context-surface.json'
}
if (-not $ExecutionRoutePath) {
  $ExecutionRoutePath = Join-Path $repoRoot 'profiles\card-game\generated-execution-route.json'
}
if (-not $SkillResolverPath) {
  $SkillResolverPath = Join-Path $repoRoot 'profiles\card-game\generated-skill-resolver.json'
}
$skillBundlePath = Join-Path $repoRoot 'profiles\card-game\generated-skill-bundle.md'
$governancePath = Join-Path $repoRoot 'profiles\card-game\generated-governance-status.json'
$agentIdentityPath = Join-Path $repoRoot 'profiles\card-game\generated-agent-identity-status.json'
$toolRegistryPath = Join-Path $repoRoot 'profiles\card-game\generated-tool-registry-status.json'
$policyRegistryPath = Join-Path $repoRoot 'profiles\card-game\generated-policy-registry-status.json'
$promptSurfacePath = Join-Path $repoRoot 'profiles\card-game\generated-prompt-surface-status.json'
$anomalyStatusPath = Join-Path $repoRoot 'profiles\card-game\generated-anomaly-status.json'
$securityPosturePath = Join-Path $repoRoot 'profiles\card-game\generated-security-posture.json'
$remediationStatusPath = Join-Path $CardGameRoot '.autopilot\generated\relay-remediation-status.json'
if (-not $HeuristicsPath) {
  $HeuristicsPath = Join-Path $repoRoot 'docs\card-game-integration\learning-memory\heuristics.json'
}
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-ops-dashboard.json'
}
if (-not $OutputMarkdownPath) {
  $OutputMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-ops-dashboard.md'
}

function Get-StateValue {
  param(
    [string[]]$Lines,
    [string]$Key
  )

  $escaped = [regex]::Escape($Key)
  foreach ($line in $Lines) {
    if ($line -match "^${escaped}:\s*(.+)$") {
      return $Matches[1].Trim().Trim('"')
    }
  }

  return ''
}

function Get-TopBacklogItem {
  param([string[]]$Lines)

  foreach ($line in $Lines) {
    if ($line -match '^\- \[(P\d+)\] \*\*([^\*]+)\*\* -- (.+)$') {
      return [pscustomobject]@{
        priority = $Matches[1]
        slug = $Matches[2].Trim()
        summary = $Matches[3].Trim()
      }
    }
  }

  return $null
}

$stateLines = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Encoding UTF8 } else { @() }
$backlogLines = if (Test-Path -LiteralPath $backlogPath) { Get-Content -LiteralPath $backlogPath -Encoding UTF8 } else { @() }
$loopStatus = if (Test-Path -LiteralPath $LoopStatusPath) { Get-Content -Raw -LiteralPath $LoopStatusPath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$contextSurface = if (Test-Path -LiteralPath $ContextSurfacePath) { Get-Content -Raw -LiteralPath $ContextSurfacePath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$executionRoute = if (Test-Path -LiteralPath $ExecutionRoutePath) { Get-Content -Raw -LiteralPath $ExecutionRoutePath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$skillResolver = if (Test-Path -LiteralPath $SkillResolverPath) { Get-Content -Raw -LiteralPath $SkillResolverPath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$governance = if (Test-Path -LiteralPath $governancePath) { Get-Content -Raw -LiteralPath $governancePath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$agentIdentity = if (Test-Path -LiteralPath $agentIdentityPath) { Get-Content -Raw -LiteralPath $agentIdentityPath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$toolRegistry = if (Test-Path -LiteralPath $toolRegistryPath) { Get-Content -Raw -LiteralPath $toolRegistryPath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$policyRegistry = if (Test-Path -LiteralPath $policyRegistryPath) { Get-Content -Raw -LiteralPath $policyRegistryPath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$promptSurface = if (Test-Path -LiteralPath $promptSurfacePath) { Get-Content -Raw -LiteralPath $promptSurfacePath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$anomalyStatus = if (Test-Path -LiteralPath $anomalyStatusPath) { Get-Content -Raw -LiteralPath $anomalyStatusPath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$securityPosture = if (Test-Path -LiteralPath $securityPosturePath) { Get-Content -Raw -LiteralPath $securityPosturePath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$remediation = if (Test-Path -LiteralPath $remediationStatusPath) { Get-Content -Raw -LiteralPath $remediationStatusPath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$heuristics = if (Test-Path -LiteralPath $HeuristicsPath) { Get-Content -Raw -LiteralPath $HeuristicsPath -Encoding UTF8 | ConvertFrom-Json } else { $null }

$topBacklog = Get-TopBacklogItem -Lines $backlogLines
$topBucketHeuristic = $null
if ($heuristics -and $loopStatus -and $loopStatus.execution_mode) {
  $topBucketHeuristic = @($heuristics.buckets | Where-Object { $_.preferred_execution_mode -eq $loopStatus.execution_mode } | Select-Object -First 1)
}

$dashboard = [ordered]@{
  generated_at = (Get-Date).ToString('o')
  state = [ordered]@{
    iteration = Get-StateValue -Lines $stateLines -Key 'iteration'
    status = Get-StateValue -Lines $stateLines -Key 'status'
    mvp_gates = Get-StateValue -Lines $stateLines -Key 'mvp_gates'
    active_task = Get-StateValue -Lines $stateLines -Key 'active_task'
  }
  backlog = [ordered]@{
    top_priority = if ($topBacklog) { [string]$topBacklog.priority } else { '' }
    top_slug = if ($topBacklog) { [string]$topBacklog.slug } else { '' }
    top_summary = if ($topBacklog) { [string]$topBacklog.summary } else { '' }
  }
  loop = if ($loopStatus) { $loopStatus } else { $null }
  context = if ($contextSurface) {
    [ordered]@{
      asmdef_status = [string]$contextSurface.asmdef_status
      highest_risk_bucket = [string]$contextSurface.highest_risk_bucket
      highest_risk_recommendation = [string]$contextSurface.highest_risk_recommendation
    }
  } else {
    $null
  }
  structure = if ($executionRoute) {
    [ordered]@{
      representative_target = [string]$executionRoute.representative_target
      asmdef_readiness_suggestion = [string]$executionRoute.asmdef_readiness_suggestion
    }
  } else {
    $null
  }
  skill_resolver = if ($skillResolver) {
    [ordered]@{
      status = [string]$skillResolver.status
      missing_skills = @($skillResolver.missing_skills)
      marker = [string]$skillResolver.summary_marker
    }
  } else {
    $null
  }
  skill_bundle = [ordered]@{
    path = if (Test-Path -LiteralPath $skillBundlePath) { $skillBundlePath } else { '' }
    present = (Test-Path -LiteralPath $skillBundlePath)
  }
  agent_identity = if ($agentIdentity) {
    [ordered]@{
      status = [string]$agentIdentity.status
      marker = [string]$agentIdentity.summary_marker
      active_ids = @($agentIdentity.active_identity_ids)
      missing_ids = @($agentIdentity.missing_identity_ids)
      path = $agentIdentityPath
    }
  } else {
    $null
  }
  tool_registry = if ($toolRegistry) {
    [ordered]@{
      status = [string]$toolRegistry.status
      marker = [string]$toolRegistry.summary_marker
      active_ids = @($toolRegistry.active_tool_ids)
      missing_ids = @($toolRegistry.missing_tool_ids)
      path = $toolRegistryPath
    }
  } else {
    $null
  }
  policy_registry = if ($policyRegistry) {
    [ordered]@{
      status = [string]$policyRegistry.status
      marker = [string]$policyRegistry.summary_marker
      active_ids = @($policyRegistry.active_policy_ids)
      missing_ids = @($policyRegistry.missing_policy_ids)
      path = $policyRegistryPath
    }
  } else {
    $null
  }
  prompt_surface = if ($promptSurface) {
    [ordered]@{
      status = [string]$promptSurface.status
      marker = [string]$promptSurface.summary_marker
      issues = @($promptSurface.issues)
      recommendation = [string]$promptSurface.recommendation
      path = $promptSurfacePath
    }
  } else {
    $null
  }
  anomaly = if ($anomalyStatus) {
    [ordered]@{
      status = [string]$anomalyStatus.status
      marker = [string]$anomalyStatus.summary_marker
      flags = @($anomalyStatus.flags)
      path = $anomalyStatusPath
    }
  } else {
    $null
  }
  security_posture = if ($securityPosture) {
    [ordered]@{
      risk = [string]$securityPosture.risk
      reason = [string]$securityPosture.reason
      marker = [string]$securityPosture.summary_marker
      flags = @($securityPosture.anomaly_flags)
      path = $securityPosturePath
    }
  } else {
    $null
  }
  governance = if ($governance) {
    [ordered]@{
      status = [string]$governance.status
      reason = [string]$governance.reason
      marker = [string]$governance.summary_marker
      blocker_artifact_path = [string]$governance.blocker_artifact_path
      blocker_hint = [string]$governance.blocker_hint
      blocker_detail = [string]$governance.blocker_detail
      recommended_action = [string]$governance.recommended_action
      recommended_action_id = [string]$governance.recommended_action_id
      recommended_action_label = [string]$governance.recommended_action_label
      remediation_status_path = [string]$governance.remediation_status_path
      remediation_report = [string]$governance.remediation_report
      unity_verification_retry_count = $governance.unity_verification_retry_count
      unity_verification_retry_limit = $governance.unity_verification_retry_limit
      unity_verification_retries_left = $governance.unity_verification_retries_left
    }
  } else {
    $null
  }
  remediation = if ($remediation) {
    [ordered]@{
      report = [string]$remediation.latest_remediation_report
      retry_count = $remediation.unity_verification_retry_count
      retry_limit = $remediation.unity_verification_retry_limit
      retries_left = $remediation.unity_verification_retries_left
      retry_budget_marker = [string]$remediation.retry_budget_marker
      marker = if ($remediation.latest_remediation_report) { '[REMEDIATION] ' + [string]$remediation.latest_remediation_report } else { '' }
      path = $remediationStatusPath
    }
  } else {
    [ordered]@{
      report = ''
      retry_count = 0
      retry_limit = 0
      retries_left = 0
      retry_budget_marker = ''
      marker = '[REMEDIATION] no remediation yet'
      path = $remediationStatusPath
    }
  }
  heuristics = if ($heuristics) {
    [ordered]@{
      total_route_records = $heuristics.total_route_records
      top_mode_bucket = if ($topBucketHeuristic.Count -gt 0) { [string]$topBucketHeuristic[0].bucket } else { '' }
      top_mode_route_samples = if ($topBucketHeuristic.Count -gt 0) { $topBucketHeuristic[0].route_samples } else { 0 }
      top_mode_policy = if ($topBucketHeuristic.Count -gt 0) { [string]$topBucketHeuristic[0].recommended_policy } else { '' }
    }
  } else {
    $null
  }
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$mdDir = Split-Path -Parent $OutputMarkdownPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($mdDir) { New-Item -ItemType Directory -Force -Path $mdDir | Out-Null }

$dashboard | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Card Game Ops Dashboard')
$lines.Add('')
$lines.Add('Generated at: ' + $dashboard.generated_at)
$lines.Add('Iteration: ' + $dashboard.state.iteration)
$lines.Add('Status: ' + $dashboard.state.status)
$lines.Add('MVP gates: ' + $dashboard.state.mvp_gates)
$lines.Add('Active task: ' + $dashboard.state.active_task)
$lines.Add('')
$lines.Add('## Backlog')
$lines.Add('')
$lines.Add('- Top priority: ' + $dashboard.backlog.top_priority)
$lines.Add('- Top slug: ' + $dashboard.backlog.top_slug)
$lines.Add('- Top summary: ' + $dashboard.backlog.top_summary)
$lines.Add('')
$lines.Add('## Loop')
$lines.Add('')
if ($dashboard.loop) {
  $lines.Add('- Next action: ' + $dashboard.loop.next_action)
  $lines.Add('- Execution mode: ' + $dashboard.loop.execution_mode)
  $lines.Add('- Execution mode reason: ' + $dashboard.loop.execution_mode_reason)
  if ($dashboard.loop.tool_policy_status) { $lines.Add('- Tool policy status: ' + $dashboard.loop.tool_policy_status) }
  if ($dashboard.loop.tool_policy_marker) { $lines.Add('- Tool policy marker: ' + $dashboard.loop.tool_policy_marker) }
  if ($dashboard.loop.execution_route_path) { $lines.Add('- Execution route: ' + $dashboard.loop.execution_route_path) }
  if ($dashboard.loop.direct_prompt_path) { $lines.Add('- Direct prompt: ' + $dashboard.loop.direct_prompt_path) }
  if ($dashboard.loop.runbook_path) { $lines.Add('- Runbook: ' + $dashboard.loop.runbook_path) }
}
$lines.Add('')
$lines.Add('## Context')
$lines.Add('')
if ($dashboard.context) {
  $lines.Add('- asmdef status: ' + $dashboard.context.asmdef_status)
  $lines.Add('- Highest risk bucket: ' + $dashboard.context.highest_risk_bucket)
  $lines.Add('- Highest risk recommendation: ' + $dashboard.context.highest_risk_recommendation)
}
$lines.Add('')
$lines.Add('## Structure')
$lines.Add('')
if ($dashboard.structure) {
  $lines.Add('- Representative target: ' + $dashboard.structure.representative_target)
  if ($dashboard.structure.asmdef_readiness_suggestion) {
    foreach ($line in ($dashboard.structure.asmdef_readiness_suggestion -split "`r?`n")) {
      $lines.Add($line)
    }
  }
}
$lines.Add('')
$lines.Add('## Skill Resolver')
$lines.Add('')
if ($dashboard.skill_resolver) {
  $lines.Add('- Status: ' + $dashboard.skill_resolver.status)
  $lines.Add('- Marker: ' + $dashboard.skill_resolver.marker)
  foreach ($skill in @($dashboard.skill_resolver.missing_skills)) {
    $lines.Add('- Missing skill: ' + [string]$skill)
  }
}
if ($dashboard.skill_bundle.present) {
  $lines.Add('- Bundle: ' + $dashboard.skill_bundle.path)
}
$lines.Add('')
$lines.Add('## Agent Identity')
$lines.Add('')
if ($dashboard.agent_identity) {
  $lines.Add('- Status: ' + $dashboard.agent_identity.status)
  $lines.Add('- Marker: ' + $dashboard.agent_identity.marker)
  if ($dashboard.agent_identity.path) { $lines.Add('- Artifact: ' + $dashboard.agent_identity.path) }
  foreach ($identityId in @($dashboard.agent_identity.active_ids)) {
    $lines.Add('- Active identity: ' + [string]$identityId)
  }
  foreach ($identityId in @($dashboard.agent_identity.missing_ids)) {
    $lines.Add('- Missing identity: ' + [string]$identityId)
  }
}
$lines.Add('')
if ($dashboard.tool_registry) {
  $lines.Add('## Tool Registry')
  $lines.Add('')
  $lines.Add('- Status: ' + $dashboard.tool_registry.status)
  $lines.Add('- Marker: ' + $dashboard.tool_registry.marker)
  if ($dashboard.tool_registry.path) { $lines.Add('- Artifact: ' + $dashboard.tool_registry.path) }
  foreach ($toolId in @($dashboard.tool_registry.active_ids)) {
    $lines.Add('- Active tool: ' + [string]$toolId)
  }
  foreach ($toolId in @($dashboard.tool_registry.missing_ids)) {
    $lines.Add('- Missing tool: ' + [string]$toolId)
  }
  $lines.Add('')
}
$lines.Add('')
if ($dashboard.policy_registry) {
  $lines.Add('## Policy Registry')
  $lines.Add('')
  $lines.Add('- Status: ' + $dashboard.policy_registry.status)
  $lines.Add('- Marker: ' + $dashboard.policy_registry.marker)
  if ($dashboard.policy_registry.path) { $lines.Add('- Artifact: ' + $dashboard.policy_registry.path) }
  foreach ($policyId in @($dashboard.policy_registry.active_ids)) {
    $lines.Add('- Active policy: ' + [string]$policyId)
  }
  foreach ($policyId in @($dashboard.policy_registry.missing_ids)) {
    $lines.Add('- Missing policy: ' + [string]$policyId)
  }
  $lines.Add('')
}
if ($dashboard.prompt_surface) {
  $lines.Add('## Prompt Surface')
  $lines.Add('')
  $lines.Add('- Status: ' + $dashboard.prompt_surface.status)
  $lines.Add('- Marker: ' + $dashboard.prompt_surface.marker)
  if ($dashboard.prompt_surface.path) { $lines.Add('- Artifact: ' + $dashboard.prompt_surface.path) }
  foreach ($issue in @($dashboard.prompt_surface.issues) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) {
    $lines.Add('- Issue: ' + [string]$issue)
  }
  if ($dashboard.prompt_surface.recommendation) { $lines.Add('- Recommendation: ' + $dashboard.prompt_surface.recommendation) }
  $lines.Add('')
}
if ($dashboard.prompt_surface) {
  $lines.Add('## Prompt Surface')
  $lines.Add('')
  $lines.Add('- Status: ' + $dashboard.prompt_surface.status)
  $lines.Add('- Marker: ' + $dashboard.prompt_surface.marker)
  if ($dashboard.prompt_surface.path) { $lines.Add('- Artifact: ' + $dashboard.prompt_surface.path) }
  foreach ($issue in @($dashboard.prompt_surface.issues) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) {
    $lines.Add('- Issue: ' + [string]$issue)
  }
  if ($dashboard.prompt_surface.recommendation) { $lines.Add('- Recommendation: ' + $dashboard.prompt_surface.recommendation) }
  $lines.Add('')
}
if ($dashboard.anomaly) {
  $lines.Add('## Anomaly Detection')
  $lines.Add('')
  $lines.Add('- Status: ' + $dashboard.anomaly.status)
  $lines.Add('- Marker: ' + $dashboard.anomaly.marker)
  if ($dashboard.anomaly.path) { $lines.Add('- Artifact: ' + $dashboard.anomaly.path) }
  foreach ($flag in @($dashboard.anomaly.flags) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) {
    $lines.Add('- Flag: ' + [string]$flag)
  }
  $lines.Add('')
}
if ($dashboard.security_posture) {
  $lines.Add('## Security Posture')
  $lines.Add('')
  $lines.Add('- Risk: ' + $dashboard.security_posture.risk)
  $lines.Add('- Reason: ' + $dashboard.security_posture.reason)
  $lines.Add('- Marker: ' + $dashboard.security_posture.marker)
  if ($dashboard.security_posture.path) { $lines.Add('- Artifact: ' + $dashboard.security_posture.path) }
  foreach ($flag in @($dashboard.security_posture.flags) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }) {
    $lines.Add('- Correlated flag: ' + [string]$flag)
  }
  $lines.Add('')
}
$lines.Add('## Governance')
$lines.Add('')
if ($dashboard.governance) {
  $lines.Add('- Status: ' + $dashboard.governance.status)
  $lines.Add('- Reason: ' + $dashboard.governance.reason)
  $lines.Add('- Marker: ' + $dashboard.governance.marker)
  if ($dashboard.governance.blocker_artifact_path) { $lines.Add('- Open next: ' + $dashboard.governance.blocker_artifact_path) }
  if ($dashboard.governance.blocker_hint) { $lines.Add('- Hint: ' + $dashboard.governance.blocker_hint) }
  if ($dashboard.governance.blocker_detail) { $lines.Add('- Missing key(s): ' + $dashboard.governance.blocker_detail) }
  if ($dashboard.governance.recommended_action) { $lines.Add('- Do this next: ' + $dashboard.governance.recommended_action) }
  if ($dashboard.governance.recommended_action_label) { $lines.Add('- Desktop button: ' + $dashboard.governance.recommended_action_label) }
  if ($dashboard.governance.remediation_report) { $lines.Add('- Last remediation report: ' + $dashboard.governance.remediation_report) }
  if ($dashboard.governance.remediation_status_path) { $lines.Add('- Remediation artifact: ' + $dashboard.governance.remediation_status_path) }
  if ($dashboard.governance.unity_verification_retry_count -gt 0) { $lines.Add('- Unity verification retries: ' + $dashboard.governance.unity_verification_retry_count) }
  if ($dashboard.governance.unity_verification_retry_limit -gt 0) { $lines.Add('- Unity retry budget: ' + $dashboard.governance.unity_verification_retries_left + ' left of ' + $dashboard.governance.unity_verification_retry_limit) }
}
$lines.Add('')
$lines.Add('## Remediation')
$lines.Add('')
if ($dashboard.remediation.marker) { $lines.Add('- Marker: ' + $dashboard.remediation.marker) }
if ($dashboard.remediation.retry_budget_marker) { $lines.Add('- Retry budget marker: ' + $dashboard.remediation.retry_budget_marker) }
if ($dashboard.remediation.path) { $lines.Add('- Artifact: ' + $dashboard.remediation.path) }
$lines.Add('')
$lines.Add('## Learning')
$lines.Add('')
if ($dashboard.heuristics) {
  $lines.Add('- Total route records: ' + $dashboard.heuristics.total_route_records)
  $lines.Add('- Mode-aligned bucket: ' + $dashboard.heuristics.top_mode_bucket)
  $lines.Add('- Mode-aligned route samples: ' + $dashboard.heuristics.top_mode_route_samples)
  $lines.Add('- Mode-aligned policy: ' + $dashboard.heuristics.top_mode_policy)
}

[string]::Join("`r`n", $lines) | Set-Content -LiteralPath $OutputMarkdownPath -Encoding UTF8

Write-Host "Wrote ops dashboard JSON: $OutputJsonPath"
Write-Host "Wrote ops dashboard Markdown: $OutputMarkdownPath"
