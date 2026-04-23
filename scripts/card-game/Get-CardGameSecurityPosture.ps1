param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$OutputJsonPath = '',
  [string]$OutputTextPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-security-posture.json'
}
if (-not $OutputTextPath) {
  $OutputTextPath = Join-Path $repoRoot 'profiles\card-game\generated-security-posture.txt'
}

$governancePath = Join-Path $repoRoot 'profiles\card-game\generated-governance-status.json'
$identityPath = Join-Path $repoRoot 'profiles\card-game\generated-agent-identity-status.json'
$toolRegistryPath = Join-Path $repoRoot 'profiles\card-game\generated-tool-registry-status.json'
$policyRegistryPath = Join-Path $repoRoot 'profiles\card-game\generated-policy-registry-status.json'
$promptSurfacePath = Join-Path $repoRoot 'profiles\card-game\generated-prompt-surface-status.json'
$anomalyPath = Join-Path $repoRoot 'profiles\card-game\generated-anomaly-status.json'

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

$governance = Read-JsonFile -Path $governancePath
$identity = Read-JsonFile -Path $identityPath
$toolRegistry = Read-JsonFile -Path $toolRegistryPath
$policyRegistry = Read-JsonFile -Path $policyRegistryPath
$promptSurface = Read-JsonFile -Path $promptSurfacePath
$anomaly = Read-JsonFile -Path $anomalyPath

$risk = 'low'
$reason = 'all_layers_green'
if ($anomaly -and [string]$anomaly.status -eq 'anomaly') {
  $risk = 'high'
  $reason = 'anomaly_detected'
} elseif ($governance -and [string]$governance.status -eq 'blocked') {
  $risk = 'medium'
  $reason = 'governance_blocked'
} elseif ($promptSurface -and [string]$promptSurface.status -eq 'warn') {
  $risk = 'medium'
  $reason = 'prompt_surface_warn'
}

$marker = "[SECURITY_POSTURE] risk=$risk reason=$reason"

$summary = [pscustomobject]@{
  generated_at = (Get-Date).ToString('o')
  card_game_root = $CardGameRoot
  risk = $risk
  reason = $reason
  governance_status = if ($governance) { [string]$governance.status } else { '' }
  agent_identity_status = if ($identity) { [string]$identity.status } else { '' }
  tool_registry_status = if ($toolRegistry) { [string]$toolRegistry.status } else { '' }
  policy_registry_status = if ($policyRegistry) { [string]$policyRegistry.status } else { '' }
  prompt_surface_status = if ($promptSurface) { [string]$promptSurface.status } else { '' }
  anomaly_status = if ($anomaly) { [string]$anomaly.status } else { '' }
  anomaly_flags = if ($anomaly) { @($anomaly.flags) } else { @() }
  summary_marker = $marker
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$textDir = Split-Path -Parent $OutputTextPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($textDir) { New-Item -ItemType Directory -Force -Path $textDir | Out-Null }

$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
@(
  $summary.summary_marker
  if ($summary.anomaly_flags.Count -gt 0) { '[SECURITY_FLAGS] ' + ($summary.anomaly_flags -join ', ') } else { '' }
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Set-Content -LiteralPath $OutputTextPath -Encoding UTF8

Write-Output ($summary | ConvertTo-Json -Depth 6)
