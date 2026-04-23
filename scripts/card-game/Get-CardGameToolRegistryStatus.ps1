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
  $RegistryPath = Join-Path $repoRoot 'profiles\card-game\tool-registry.json'
}
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-tool-registry-status.json'
}
if (-not $OutputTextPath) {
  $OutputTextPath = Join-Path $repoRoot 'profiles\card-game\generated-tool-registry-status.txt'
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

$activeToolIds = New-Object System.Collections.Generic.List[string]
$activeToolIds.Add('cardgame-compact-artifact-surface') | Out-Null
$activeToolIds.Add('cardgame-powershell-runbooks') | Out-Null

switch ($executionMode) {
  'relay-dad' {
    $activeToolIds.Add('cardgame-relay-codex-cli') | Out-Null
    $activeToolIds.Add('cardgame-relay-claude-cli') | Out-Null
  }
  'direct-codex' {
    $activeToolIds.Add('cardgame-direct-codex-local') | Out-Null
  }
  'docs-lite' {
    $activeToolIds.Add('cardgame-direct-codex-local') | Out-Null
  }
}

if ($bucket -in @('battle-runtime', 'map-runtime', 'qa-editor', 'ui-runtime')) {
  $activeToolIds.Add('cardgame-unity-mcp-approved') | Out-Null
}

$activeToolIds = @($activeToolIds | Select-Object -Unique)
$toolIndex = @{}
foreach ($tool in @($registry.tools)) {
  if ($tool.id) {
    $toolIndex[[string]$tool.id] = $tool
  }
}

$missingToolIds = New-Object System.Collections.Generic.List[string]
$resolvedTools = New-Object System.Collections.Generic.List[object]
foreach ($toolId in $activeToolIds) {
  if (-not $toolIndex.ContainsKey($toolId)) {
    $missingToolIds.Add($toolId) | Out-Null
    continue
  }

  $tool = $toolIndex[$toolId]
  $resolvedTools.Add((New-PlainObject -Properties @{
    id = [string]$tool.id
    kind = [string]$tool.kind
    description = [string]$tool.description
    approved_buckets = @($tool.approved_buckets | Where-Object { $null -ne $_ })
    approved_execution_modes = @($tool.approved_execution_modes | Where-Object { $null -ne $_ })
    tool_classes = @($tool.tool_classes | Where-Object { $null -ne $_ })
    used_by_identity_ids = @($tool.used_by_identity_ids | Where-Object { $null -ne $_ })
    approved_mcp_servers = @($tool.approved_mcp_servers | Where-Object { $null -ne $_ })
    approved_mcp_tools = @($tool.approved_mcp_tools | Where-Object { $null -ne $_ })
    data_access_scope = [string]$tool.data_access_scope
  })) | Out-Null
}

$status = if (-not $registry) {
  'missing'
} elseif ($missingToolIds.Count -gt 0 -or $resolvedTools.Count -eq 0) {
  'missing'
} else {
  'ok'
}

$marker = if ($status -eq 'ok') {
  "[TOOL_REGISTRY] status=ok active=$($activeToolIds.Count) mode=$executionMode bucket=$bucket"
} elseif ($missingToolIds.Count -gt 0) {
  "[TOOL_REGISTRY] status=missing missing=$($missingToolIds -join ',')"
} else {
  '[TOOL_REGISTRY] status=missing registry_unavailable'
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
  active_tool_ids = @($activeToolIds)
  missing_tool_ids = @($missingToolIds.ToArray())
  tools = @($resolvedTools.ToArray())
  summary_marker = $marker
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$textDir = Split-Path -Parent $OutputTextPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($textDir) { New-Item -ItemType Directory -Force -Path $textDir | Out-Null }

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
@(
  $summary.summary_marker
  if ($summary.active_tool_ids.Count -gt 0) { '[TOOL_IDS] ' + ($summary.active_tool_ids -join ', ') } else { '' }
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Set-Content -LiteralPath $OutputTextPath -Encoding UTF8

Write-Output ($summary | ConvertTo-Json -Depth 8)
