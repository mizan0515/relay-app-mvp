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
  $RegistryPath = Join-Path $repoRoot 'profiles\card-game\agent-identities.json'
}
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-agent-identity-status.json'
}
if (-not $OutputTextPath) {
  $OutputTextPath = Join-Path $repoRoot 'profiles\card-game\generated-agent-identity-status.txt'
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

$activeIdentityIds = New-Object System.Collections.Generic.List[string]
$activeIdentityIds.Add('cardgame-autopilot-manager')

switch ($executionMode) {
  'relay-dad' {
    $activeIdentityIds.Add('cardgame-relay-codex-peer')
    $activeIdentityIds.Add('cardgame-relay-claude-peer')
  }
  'direct-codex' {
    $activeIdentityIds.Add('cardgame-route-direct-codex')
  }
  'docs-lite' {
    $activeIdentityIds.Add('cardgame-route-direct-codex')
  }
}

if ($bucket -in @('battle-runtime', 'map-runtime', 'qa-editor', 'ui-runtime')) {
  $activeIdentityIds.Add('cardgame-unity-mcp-bridge')
}

$activeIdentityIds = @($activeIdentityIds | Select-Object -Unique)
$identityIndex = @{}
foreach ($identity in @($registry.identities)) {
  if ($identity.id) {
    $identityIndex[[string]$identity.id] = $identity
  }
}

$missingIdentityIds = New-Object System.Collections.Generic.List[string]
$resolvedIdentities = New-Object System.Collections.Generic.List[object]
foreach ($identityId in $activeIdentityIds) {
  if (-not $identityIndex.ContainsKey($identityId)) {
    $missingIdentityIds.Add($identityId)
    continue
  }

  $identity = $identityIndex[$identityId]
  $resolvedIdentities.Add((New-PlainObject -Properties @{
    id = [string]$identity.id
    kind = [string]$identity.kind
    description = [string]$identity.description
    revocation_scope = [string]$identity.revocation_scope
    allowed_buckets = @($identity.allowed_buckets | Where-Object { $null -ne $_ })
    allowed_execution_modes = @($identity.allowed_execution_modes | Where-Object { $null -ne $_ })
    allowed_tool_classes = @($identity.allowed_tool_classes | Where-Object { $null -ne $_ })
    allowed_mcp_servers = @($identity.allowed_mcp_servers | Where-Object { $null -ne $_ })
    allowed_mcp_tools = @($identity.allowed_mcp_tools | Where-Object { $null -ne $_ })
    allowed_compact_artifacts = @($identity.allowed_compact_artifacts | Where-Object { $null -ne $_ })
  }))
}

$status = if (-not $registry) {
  'missing'
} elseif ($missingIdentityIds.Count -gt 0 -or $resolvedIdentities.Count -eq 0) {
  'missing'
} else {
  'ok'
}

$marker = if ($status -eq 'ok') {
  "[AGENT_IDENTITY] status=ok active=$($activeIdentityIds.Count) mode=$executionMode bucket=$bucket"
} elseif ($missingIdentityIds.Count -gt 0) {
  "[AGENT_IDENTITY] status=missing missing=$($missingIdentityIds -join ',')"
} else {
  '[AGENT_IDENTITY] status=missing registry_unavailable'
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
  active_identity_ids = @($activeIdentityIds)
  missing_identity_ids = @($missingIdentityIds.ToArray())
  identities = @($resolvedIdentities.ToArray())
  summary_marker = $marker
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$textDir = Split-Path -Parent $OutputTextPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($textDir) { New-Item -ItemType Directory -Force -Path $textDir | Out-Null }

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
@(
  $summary.summary_marker
  if ($summary.active_identity_ids.Count -gt 0) { '[AGENT_IDS] ' + ($summary.active_identity_ids -join ', ') } else { '' }
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Set-Content -LiteralPath $OutputTextPath -Encoding UTF8

Write-Output ($summary | ConvertTo-Json -Depth 8)
