param(
  [string]$ManifestPath = '',
  [string]$LoopStatusPath = '',
  [string]$RegistryPath = '',
  [string]$OutputJsonPath = '',
  [string]$OutputTextPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $ManifestPath) {
  $ManifestPath = Join-Path $repoRoot 'profiles\card-game\generated-admission.json'
}
if (-not $LoopStatusPath) {
  $LoopStatusPath = Join-Path $repoRoot 'profiles\card-game\generated-loop-status.json'
}
if (-not $RegistryPath) {
  $RegistryPath = Join-Path $repoRoot 'profiles\card-game\policy-registry.json'
}
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-policy-registry-status.json'
}
if (-not $OutputTextPath) {
  $OutputTextPath = Join-Path $repoRoot 'profiles\card-game\generated-policy-registry-status.txt'
}

function Read-JsonFile {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
    return $null
  }

  try {
    return Get-Content -Raw -LiteralPath $Path -Encoding UTF8 | ConvertFrom-Json
  } catch {
    return $null
  }
}

function New-PlainObject {
  param([hashtable]$Properties)

  $object = New-Object PSObject
  foreach ($key in $Properties.Keys) {
    $object | Add-Member -NotePropertyName $key -NotePropertyValue $Properties[$key]
  }

  return $object
}

$manifest = Read-JsonFile -Path $ManifestPath
$loopStatus = Read-JsonFile -Path $LoopStatusPath
$registry = Read-JsonFile -Path $RegistryPath

$bucket = if ($manifest -and $manifest.task -and $manifest.task.bucket) { [string]$manifest.task.bucket } else { '' }
$executionMode = if ($manifest -and $manifest.guidance -and $manifest.guidance.execution_mode -and $manifest.guidance.execution_mode.mode) {
  [string]$manifest.guidance.execution_mode.mode
} elseif ($loopStatus -and $loopStatus.execution_mode) {
  [string]$loopStatus.execution_mode
} else {
  ''
}
$sessionId = if ($manifest -and $manifest.session_id) { [string]$manifest.session_id } elseif ($loopStatus -and $loopStatus.session_id) { [string]$loopStatus.session_id } else { '' }

$activePolicyIds = New-Object System.Collections.Generic.List[string]
$activePolicyIds.Add('cardgame-compact-artifacts-only') | Out-Null
$activePolicyIds.Add('cardgame-no-full-log-tail') | Out-Null
if ($bucket -eq 'qa-editor') {
  $activePolicyIds.Add('cardgame-no-web-for-unity-local') | Out-Null
}
if ($bucket -in @('battle-runtime', 'map-runtime', 'qa-editor', 'ui-runtime')) {
  $activePolicyIds.Add('cardgame-require-unity-mcp-proof') | Out-Null
}

$activePolicyIds = @($activePolicyIds | Select-Object -Unique)
$policyIndex = @{}
foreach ($policy in @($registry.policies)) {
  if ($policy.id) {
    $policyIndex[[string]$policy.id] = $policy
  }
}

$missingPolicyIds = New-Object System.Collections.Generic.List[string]
$resolvedPolicies = New-Object System.Collections.Generic.List[object]
foreach ($policyId in $activePolicyIds) {
  if (-not $policyIndex.ContainsKey($policyId)) {
    $missingPolicyIds.Add($policyId) | Out-Null
    continue
  }

  $policy = $policyIndex[$policyId]
  $resolvedPolicies.Add((New-PlainObject -Properties @{
    id = [string]$policy.id
    description = [string]$policy.description
    applies_to_buckets = @($policy.applies_to_buckets | Where-Object { $null -ne $_ })
    applies_to_execution_modes = @($policy.applies_to_execution_modes | Where-Object { $null -ne $_ })
    enforcement_plane = [string]$policy.enforcement_plane
  })) | Out-Null
}

$status = if (-not $registry) {
  'missing'
} elseif ($missingPolicyIds.Count -gt 0 -or $resolvedPolicies.Count -eq 0) {
  'missing'
} else {
  'ok'
}

$marker = if ($status -eq 'ok') {
  "[POLICY_REGISTRY] status=ok active=$($activePolicyIds.Count) mode=$executionMode bucket=$bucket"
} elseif ($missingPolicyIds.Count -gt 0) {
  "[POLICY_REGISTRY] status=missing missing=$($missingPolicyIds -join ',')"
} else {
  '[POLICY_REGISTRY] status=missing registry_unavailable'
}

$summary = New-PlainObject -Properties @{
  generated_at = (Get-Date).ToString('o')
  manifest_path = $ManifestPath
  loop_status_path = $LoopStatusPath
  registry_path = $RegistryPath
  session_id = $sessionId
  bucket = $bucket
  execution_mode = $executionMode
  status = $status
  registry_present = [bool]($registry -ne $null)
  active_policy_ids = @($activePolicyIds)
  missing_policy_ids = @($missingPolicyIds.ToArray())
  policies = @($resolvedPolicies.ToArray())
  summary_marker = $marker
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$textDir = Split-Path -Parent $OutputTextPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($textDir) { New-Item -ItemType Directory -Force -Path $textDir | Out-Null }

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
@(
  $summary.summary_marker
  if ($summary.active_policy_ids.Count -gt 0) { '[POLICY_IDS] ' + ($summary.active_policy_ids -join ', ') } else { '' }
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Set-Content -LiteralPath $OutputTextPath -Encoding UTF8

Write-Output ($summary | ConvertTo-Json -Depth 8)
