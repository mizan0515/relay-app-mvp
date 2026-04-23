param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$ManifestPath = '',
  [string]$SessionId = '',
  [string]$OutputJsonPath = '',
  [string]$OutputTextPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $ManifestPath) {
  $ManifestPath = Join-Path $repoRoot 'profiles\card-game\generated-admission.json'
}
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-governance-status.json'
}
if (-not $OutputTextPath) {
  $OutputTextPath = Join-Path $repoRoot 'profiles\card-game\generated-governance-status.txt'
}
$requiredEvidenceOutputPath = Join-Path $repoRoot 'profiles\card-game\generated-required-evidence-status.json'
$skillResolverOutputPath = Join-Path $repoRoot 'profiles\card-game\generated-skill-resolver.json'
$toolPolicyOutputPath = Join-Path $repoRoot 'profiles\card-game\generated-tool-policy-status.json'
$agentIdentityOutputPath = Join-Path $repoRoot 'profiles\card-game\generated-agent-identity-status.json'
$toolRegistryOutputPath = Join-Path $repoRoot 'profiles\card-game\generated-tool-registry-status.json'
$policyRegistryOutputPath = Join-Path $repoRoot 'profiles\card-game\generated-policy-registry-status.json'
$remediationStatusPath = Join-Path $CardGameRoot '.autopilot\generated\relay-remediation-status.json'
$unityVerificationRetryLimit = 2

$requiredEvidence = $null
$skillResolver = $null
$toolPolicy = $null
$agentIdentity = $null
$toolRegistry = $null
$policyRegistry = $null
$remediationStatus = $null
$unityVerificationRetryCount = 0

try {
  $requiredEvidenceArgs = @(
    '-ExecutionPolicy', 'Bypass',
    '-File', (Join-Path $PSScriptRoot 'Get-CardGameRequiredEvidenceStatus.ps1'),
    '-CardGameRoot', $CardGameRoot,
    '-ManifestPath', $ManifestPath,
    '-OutputPath', $requiredEvidenceOutputPath
  )
  if (-not [string]::IsNullOrWhiteSpace($SessionId)) {
    $requiredEvidenceArgs += @('-SessionId', $SessionId)
  }
  $requiredEvidence = & powershell @requiredEvidenceArgs | ConvertFrom-Json
} catch {
  $requiredEvidence = $null
}

try {
  $skillResolver = & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Get-CardGameSkillResolverStatus.ps1') `
    -ManifestPath $ManifestPath `
    -OutputJsonPath $skillResolverOutputPath | ConvertFrom-Json
} catch {
  $skillResolver = $null
}

try {
  $agentIdentity = & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Get-CardGameAgentIdentityStatus.ps1') `
    -ManifestPath $ManifestPath `
    -LoopStatusPath (Join-Path $repoRoot 'profiles\card-game\generated-loop-status.json') `
    -OutputJsonPath $agentIdentityOutputPath | ConvertFrom-Json
} catch {
  $agentIdentity = $null
}

try {
  $toolRegistry = & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Get-CardGameToolRegistryStatus.ps1') `
    -ManifestPath $ManifestPath `
    -LoopStatusPath (Join-Path $repoRoot 'profiles\card-game\generated-loop-status.json') `
    -OutputJsonPath $toolRegistryOutputPath | ConvertFrom-Json
} catch {
  $toolRegistry = $null
}

try {
  $policyRegistry = & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Get-CardGamePolicyRegistryStatus.ps1') `
    -ManifestPath $ManifestPath `
    -LoopStatusPath (Join-Path $repoRoot 'profiles\card-game\generated-loop-status.json') `
    -OutputJsonPath $policyRegistryOutputPath | ConvertFrom-Json
} catch {
  $policyRegistry = $null
}

try {
  $toolPolicyArgs = @(
    '-ExecutionPolicy', 'Bypass',
    '-File', (Join-Path $PSScriptRoot 'Get-CardGameToolPolicyStatus.ps1'),
    '-CardGameRoot', $CardGameRoot,
    '-ManifestPath', $ManifestPath,
    '-OutputPath', $toolPolicyOutputPath
  )
  if (-not [string]::IsNullOrWhiteSpace($SessionId)) {
    $toolPolicyArgs += @('-SessionId', $SessionId)
  }
  $toolPolicy = & powershell @toolPolicyArgs | ConvertFrom-Json
} catch {
  $toolPolicy = $null
}

if (Test-Path -LiteralPath $remediationStatusPath) {
  try {
    $remediationStatus = Get-Content -Raw -LiteralPath $remediationStatusPath -Encoding UTF8 | ConvertFrom-Json
    if ($remediationStatus -and $remediationStatus.unity_verification_retry_count -ne $null) {
      $unityVerificationRetryCount = [int]$remediationStatus.unity_verification_retry_count
    }
  } catch {
    $remediationStatus = $null
  }
}

$status = 'ok'
$attentionRequired = $false
$reason = 'all_gates_clear'

if ($skillResolver -and -not $skillResolver.all_required_skills_present) {
  $status = 'blocked'
  $attentionRequired = $true
  $reason = 'missing_required_skills'
}
elseif ($agentIdentity -and [string]$agentIdentity.status -ne 'ok') {
  $status = 'blocked'
  $attentionRequired = $true
  $reason = 'missing_agent_identity'
}
elseif ($toolRegistry -and [string]$toolRegistry.status -ne 'ok') {
  $status = 'blocked'
  $attentionRequired = $true
  $reason = 'unregistered_tool'
}
elseif ($policyRegistry -and [string]$policyRegistry.status -ne 'ok') {
  $status = 'blocked'
  $attentionRequired = $true
  $reason = 'missing_policy_registration'
}
elseif ($requiredEvidence -and -not $requiredEvidence.all_required_evidence_present) {
  $status = 'blocked'
  $attentionRequired = $true
  $reason = 'missing_required_evidence'
}
elseif ($toolPolicy -and [string]$toolPolicy.status -eq 'violation') {
  $status = 'blocked'
  $attentionRequired = $true
  $reason = 'forbidden_tool_violation'
}
elseif ($toolPolicy -and [string]$toolPolicy.status -eq 'partial') {
  $status = 'partial'
  $reason = 'policy_partially_observed'
}
elseif ($requiredEvidence -and [string]$requiredEvidence.status -eq 'unsupported') {
  $status = 'partial'
  $reason = 'evidence_partially_observed'
}

$blockerArtifactPath = ''
$blockerHint = ''
$blockerDetail = ''
$recommendedAction = ''
$recommendedActionId = ''
$recommendedActionLabel = ''
$retryBudgetRemaining = [Math]::Max(0, $unityVerificationRetryLimit - $unityVerificationRetryCount)
$retryBudgetActive = $false
switch ($reason) {
  'missing_required_skills' {
    $blockerArtifactPath = $skillResolverOutputPath
    $blockerHint = 'Open the skill resolver artifact and restore the missing required skill path.'
    $blockerDetail = ((@($skillResolver.missing_skills) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ', ')
    $recommendedAction = 'Restore the missing skill file or fix the required skill path before running this slice again.'
    $recommendedActionId = 'open_skill_resolver'
    $recommendedActionLabel = 'Open Skill Resolver'
  }
  'missing_agent_identity' {
    $blockerArtifactPath = $agentIdentityOutputPath
    $blockerHint = 'Open the agent-identity artifact and restore the missing or unregistered role identity before trusting this slice.'
    $blockerDetail = ((@($agentIdentity.missing_identity_ids) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ', ')
    $recommendedAction = 'Register the missing agent identity or fix the role-to-identity mapping before continuing this slice.'
    $recommendedActionId = 'open_agent_identity'
    $recommendedActionLabel = 'Open Agent Identity'
  }
  'unregistered_tool' {
    $blockerArtifactPath = $toolRegistryOutputPath
    $blockerHint = 'Open the tool-registry artifact and restore the missing or unregistered approved tool before running this slice.'
    $blockerDetail = ((@($toolRegistry.missing_tool_ids) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ', ')
    $recommendedAction = 'Register the missing approved tool or fix the tool-to-bucket mapping before continuing this slice.'
    $recommendedActionId = 'open_tool_registry'
    $recommendedActionLabel = 'Open Tool Registry'
  }
  'missing_policy_registration' {
    $blockerArtifactPath = $policyRegistryOutputPath
    $blockerHint = 'Open the policy-registry artifact and restore the missing or unregistered active policy before running this slice.'
    $blockerDetail = ((@($policyRegistry.missing_policy_ids) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ', ')
    $recommendedAction = 'Register the missing central policy or fix the policy-to-bucket mapping before continuing this slice.'
    $recommendedActionId = 'open_policy_registry'
    $recommendedActionLabel = 'Open Policy Registry'
  }
  'missing_required_evidence' {
    $blockerArtifactPath = $requiredEvidenceOutputPath
    $blockerHint = 'Open the required-evidence artifact and collect the missing proof before continuing.'
    $blockerDetail = ((@($requiredEvidence.missing_evidence) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ', ')
    $recommendedAction = switch ($blockerDetail) {
      'unity_mcp_observed' { 'Run the Unity verification path again and make sure the relay peer actually uses Unity MCP before completion.' }
      'compact_manager_signal' { 'Regenerate the compact manager signal before trying to continue this slice.' }
      'compact_relay_signal' { 'Regenerate the compact relay signal before trying to continue this slice.' }
      default { 'Collect the missing required evidence before continuing this slice.' }
    }
    switch ($blockerDetail) {
      'unity_mcp_observed' {
        $recentRemediation = if ($remediationStatus) { [string]$remediationStatus.latest_remediation_report } else { '' }
        $retryBudgetActive = $true
        if ($unityVerificationRetryCount -ge 2) {
          $recommendedActionId = 'wait_for_operator'
          $recommendedActionLabel = 'Wait For Human'
          $recommendedAction = 'Unity verification retry has already failed multiple times. Stop and let a human inspect the compact evidence before retrying again.'
        } elseif ($recentRemediation -and $recentRemediation -match 'Retry Unity Verification') {
          $recommendedActionId = 'open_required_evidence'
          $recommendedActionLabel = 'Open Required Evidence'
          $recommendedAction = 'A Unity verification retry was already attempted recently. Read the compact evidence file before retrying again.'
        } else {
          $recommendedActionId = 'retry_unity_verification'
          $recommendedActionLabel = if ($retryBudgetRemaining -gt 0) {
            "Retry Unity Verification ($retryBudgetRemaining left)"
          } else {
            'Retry Unity Verification'
          }
        }
      }
      'compact_manager_signal' {
        $recommendedActionId = 'refresh_manager_signal'
        $recommendedActionLabel = 'Refresh Manager Signal'
      }
      'compact_relay_signal' {
        $recommendedActionId = 'refresh_relay_signal'
        $recommendedActionLabel = 'Refresh Relay Signal'
      }
      default {
        $recommendedActionId = 'open_required_evidence'
        $recommendedActionLabel = 'Open Required Evidence'
      }
    }
  }
  'forbidden_tool_violation' {
    $blockerArtifactPath = $toolPolicyOutputPath
    $blockerHint = 'Open the tool-policy artifact and remove the forbidden tool usage from this slice.'
    $blockerDetail = ((@($toolPolicy.violations) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ', ')
    $recommendedAction = 'Rerun the slice without the forbidden tool usage and keep the session on the allowed tool path only.'
    $recommendedActionId = 'open_tool_policy'
    $recommendedActionLabel = 'Open Tool Policy'
  }
  'policy_partially_observed' {
    $blockerArtifactPath = $toolPolicyOutputPath
    $blockerHint = 'Open the tool-policy artifact to see which policy checks are still only partially observed.'
    $blockerDetail = ((@($toolPolicy.unsupported_forbidden_tools) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ', ')
    $recommendedAction = 'Use the compact policy artifact to review the unsupported checks before trusting this slice.'
    $recommendedActionId = 'open_tool_policy'
    $recommendedActionLabel = 'Open Tool Policy'
  }
  'evidence_partially_observed' {
    $blockerArtifactPath = $requiredEvidenceOutputPath
    $blockerHint = 'Open the required-evidence artifact to see which proof checks are still unsupported.'
    $blockerDetail = ((@($requiredEvidence.unsupported_evidence) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ', ')
    $recommendedAction = 'Review the unsupported evidence contract and avoid marking this slice complete until a deterministic proof exists.'
    $recommendedActionId = 'open_required_evidence'
    $recommendedActionLabel = 'Open Required Evidence'
  }
}

$summary = [pscustomobject]@{
  generated_at = (Get-Date).ToString('o')
  card_game_root = $CardGameRoot
  manifest_path = $ManifestPath
  session_id = if ($SessionId) { $SessionId } elseif ($requiredEvidence) { [string]$requiredEvidence.session_id } else { '' }
  status = $status
  reason = $reason
  attention_required = $attentionRequired
  skill_resolver_status = if ($skillResolver) { [string]$skillResolver.status } else { '' }
  skill_resolver_marker = if ($skillResolver) { [string]$skillResolver.summary_marker } else { '' }
  agent_identity_status = if ($agentIdentity) { [string]$agentIdentity.status } else { '' }
  agent_identity_path = $agentIdentityOutputPath
  agent_identity_marker = if ($agentIdentity) { [string]$agentIdentity.summary_marker } else { '' }
  active_agent_identity_ids = if ($agentIdentity) { @($agentIdentity.active_identity_ids) } else { @() }
  missing_agent_identity_ids = if ($agentIdentity) { @($agentIdentity.missing_identity_ids) } else { @() }
  tool_registry_status = if ($toolRegistry) { [string]$toolRegistry.status } else { '' }
  tool_registry_path = $toolRegistryOutputPath
  tool_registry_marker = if ($toolRegistry) { [string]$toolRegistry.summary_marker } else { '' }
  active_tool_ids = if ($toolRegistry) { @($toolRegistry.active_tool_ids) } else { @() }
  missing_tool_ids = if ($toolRegistry) { @($toolRegistry.missing_tool_ids) } else { @() }
  policy_registry_status = if ($policyRegistry) { [string]$policyRegistry.status } else { '' }
  policy_registry_path = $policyRegistryOutputPath
  policy_registry_marker = if ($policyRegistry) { [string]$policyRegistry.summary_marker } else { '' }
  active_policy_ids = if ($policyRegistry) { @($policyRegistry.active_policy_ids) } else { @() }
  missing_policy_ids = if ($policyRegistry) { @($policyRegistry.missing_policy_ids) } else { @() }
  required_evidence_status = if ($requiredEvidence) { [string]$requiredEvidence.status } else { '' }
  required_evidence_path = $requiredEvidenceOutputPath
  required_evidence_marker = if ($requiredEvidence) { [string]$requiredEvidence.summary_marker } else { '' }
  tool_policy_status = if ($toolPolicy) { [string]$toolPolicy.status } else { '' }
  tool_policy_path = $toolPolicyOutputPath
  tool_policy_marker = if ($toolPolicy) { [string]$toolPolicy.summary_marker } else { '' }
  skill_resolver_path = $skillResolverOutputPath
  missing_required_skills = if ($skillResolver) { @($skillResolver.missing_skills) } else { @() }
  missing_required_evidence = if ($requiredEvidence) { @($requiredEvidence.missing_evidence) } else { @() }
  forbidden_tool_violations = if ($toolPolicy) { @($toolPolicy.violations) } else { @() }
  blocker_artifact_path = $blockerArtifactPath
  blocker_hint = $blockerHint
  blocker_detail = $blockerDetail
  recommended_action = $recommendedAction
  recommended_action_id = $recommendedActionId
  recommended_action_label = $recommendedActionLabel
  remediation_status_path = if ($remediationStatus) { $remediationStatusPath } else { '' }
  remediation_report = if ($remediationStatus) { [string]$remediationStatus.latest_remediation_report } else { '' }
  unity_verification_retry_count = $unityVerificationRetryCount
  unity_verification_retry_limit = $unityVerificationRetryLimit
  unity_verification_retries_left = $retryBudgetRemaining
  unity_verification_retry_budget_active = $retryBudgetActive
  summary_marker = "[GOVERNANCE] status=$status reason=$reason attention=$($attentionRequired.ToString().ToLowerInvariant())"
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$textDir = Split-Path -Parent $OutputTextPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($textDir) { New-Item -ItemType Directory -Force -Path $textDir | Out-Null }

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
@(
  $summary.summary_marker
  $summary.skill_resolver_marker
  $summary.agent_identity_marker
  $summary.tool_registry_marker
  $summary.policy_registry_marker
  $summary.required_evidence_marker
  $summary.tool_policy_marker
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Set-Content -LiteralPath $OutputTextPath -Encoding UTF8

Write-Output ($summary | ConvertTo-Json -Depth 8)
