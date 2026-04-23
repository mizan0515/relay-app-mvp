param(
  [string]$ManifestPath = '',
  [string]$PromptPath = '',
  [string]$SkillBundlePath = '',
  [string]$AdminPromptPath = '',
  [string]$PolicyPath = '',
  [string]$OutputJsonPath = '',
  [string]$OutputTextPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $ManifestPath) {
  $ManifestPath = Join-Path $repoRoot 'profiles\card-game\generated-admission.json'
}
if (-not $PromptPath) {
  $PromptPath = Join-Path $repoRoot 'profiles\card-game\generated-session-prompt.md'
}
if (-not $SkillBundlePath) {
  $SkillBundlePath = Join-Path $repoRoot 'profiles\card-game\generated-skill-bundle.md'
}
if (-not $AdminPromptPath) {
  $AdminPromptPath = Join-Path $repoRoot 'docs\card-game-integration\AUTOPILOT-ADMIN-PROMPT.md'
}
if (-not $PolicyPath) {
  $PolicyPath = Join-Path $repoRoot 'profiles\card-game\prompt-slim-policy.json'
}
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-prompt-surface-status.json'
}
if (-not $OutputTextPath) {
  $OutputTextPath = Join-Path $repoRoot 'profiles\card-game\generated-prompt-surface-status.txt'
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

function Read-TextFile {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
    return ''
  }

  try {
    return Get-Content -Raw -LiteralPath $Path -Encoding UTF8
  } catch {
    return ''
  }
}

function Get-ApproxTokens {
  param([string]$Text)

  if ([string]::IsNullOrEmpty($Text)) {
    return 0
  }

  return [int][Math]::Ceiling($Text.Length / 4.0)
}

$manifest = Read-JsonFile -Path $ManifestPath
$policy = Read-JsonFile -Path $PolicyPath
$sessionPromptText = Read-TextFile -Path $PromptPath
$skillBundleText = Read-TextFile -Path $SkillBundlePath
$adminPromptText = Read-TextFile -Path $AdminPromptPath

$requiredSkills = if ($manifest -and $manifest.guidance -and $manifest.guidance.required_skills) {
  @($manifest.guidance.required_skills | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
} else {
  @()
}
$readPath = if ($manifest -and $manifest.guidance -and $manifest.guidance.recommended_read_path) {
  @($manifest.guidance.recommended_read_path | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
} else {
  @()
}

$sessionPromptTokens = Get-ApproxTokens -Text $sessionPromptText
$skillBundleTokens = Get-ApproxTokens -Text $skillBundleText
$adminPromptTokens = Get-ApproxTokens -Text $adminPromptText
$totalTokens = $sessionPromptTokens + $skillBundleTokens + $adminPromptTokens

$issues = New-Object System.Collections.Generic.List[string]
if ($policy -and $sessionPromptTokens -gt [int]$policy.max_session_prompt_tokens) {
  $issues.Add('session_prompt_large') | Out-Null
}
if ($policy -and $skillBundleTokens -gt [int]$policy.max_skill_bundle_tokens) {
  $issues.Add('skill_bundle_large') | Out-Null
}
if ($policy -and $adminPromptTokens -gt [int]$policy.max_admin_prompt_tokens) {
  $issues.Add('admin_prompt_large') | Out-Null
}
if ($policy -and $requiredSkills.Count -gt [int]$policy.max_required_skills) {
  $issues.Add('required_skills_many') | Out-Null
}
if ($policy -and $readPath.Count -gt [int]$policy.max_recommended_read_path_entries) {
  $issues.Add('recommended_read_path_wide') | Out-Null
}

$status = if ($issues.Count -gt 0) { 'warn' } else { 'ok' }
$recommendation = if ($issues.Count -eq 0) {
  'Prompt surface is within the current slim thresholds.'
} else {
  'Trim prompt surface before the next expensive relay cycle: prefer fewer skill activations, a narrower read path, or a shorter operator prompt.'
}

$marker = if ($status -eq 'ok') {
  "[PROMPT_SURFACE] status=ok total_tokens=$totalTokens required_skills=$($requiredSkills.Count) read_path=$($readPath.Count)"
} else {
  "[PROMPT_SURFACE] status=warn issues=$($issues -join ',') total_tokens=$totalTokens"
}

$summary = [pscustomobject]@{
  generated_at = (Get-Date).ToString('o')
  manifest_path = $ManifestPath
  prompt_path = $PromptPath
  skill_bundle_path = $SkillBundlePath
  admin_prompt_path = $AdminPromptPath
  policy_path = $PolicyPath
  status = $status
  issues = @($issues)
  required_skill_count = $requiredSkills.Count
  recommended_read_path_count = $readPath.Count
  session_prompt_tokens = $sessionPromptTokens
  skill_bundle_tokens = $skillBundleTokens
  admin_prompt_tokens = $adminPromptTokens
  total_prompt_surface_tokens = $totalTokens
  recommendation = $recommendation
  summary_marker = $marker
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$textDir = Split-Path -Parent $OutputTextPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($textDir) { New-Item -ItemType Directory -Force -Path $textDir | Out-Null }

$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
@(
  $summary.summary_marker
  if ($summary.issues.Count -gt 0) { '[PROMPT_SURFACE_ISSUES] ' + ($summary.issues -join ', ') } else { '' }
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Set-Content -LiteralPath $OutputTextPath -Encoding UTF8

Write-Output ($summary | ConvertTo-Json -Depth 6)
