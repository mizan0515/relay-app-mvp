param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$ManifestPath = '',
  [string]$OutputJsonPath = '',
  [string]$OutputTextPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $ManifestPath) {
  $ManifestPath = Join-Path $repoRoot 'profiles\card-game\generated-admission.json'
}
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-anomaly-status.json'
}
if (-not $OutputTextPath) {
  $OutputTextPath = Join-Path $repoRoot 'profiles\card-game\generated-anomaly-status.txt'
}

$rulesPath = Join-Path $repoRoot 'profiles\card-game\anomaly-rules.json'
$governancePath = Join-Path $repoRoot 'profiles\card-game\generated-governance-status.json'
$relaySignalPath = Join-Path $CardGameRoot '.autopilot\generated\relay-live-signal.json'
$managerSignalPath = Join-Path $CardGameRoot '.autopilot\generated\relay-manager-signal.json'

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

$rules = Read-JsonFile -Path $rulesPath
$governance = Read-JsonFile -Path $governancePath
$relaySignal = Read-JsonFile -Path $relaySignalPath
$managerSignal = Read-JsonFile -Path $managerSignalPath

$flags = New-Object System.Collections.Generic.List[string]

if ($rules.relay_session_mismatch_is_anomaly -and $managerSignal -and [bool]$managerSignal.relay_session_mismatch) {
  $flags.Add('relay_session_mismatch') | Out-Null
}
if ($rules.retry_budget_exhausted_is_anomaly -and $managerSignal -and [bool]$managerSignal.retry_budget_exhausted) {
  $flags.Add('retry_budget_exhausted') | Out-Null
}
if ($rules.prompt_surface_warn_is_anomaly -and $managerSignal -and [string]$managerSignal.prompt_surface_status -eq 'warn') {
  $flags.Add('prompt_surface_warn') | Out-Null
}
if ($rules.tool_policy_violation_is_anomaly -and $governance -and [string]$governance.tool_policy_status -eq 'violation') {
  $flags.Add('forbidden_tool_violation') | Out-Null
}
if ($relaySignal -and [string]$relaySignal.derived_status -eq 'HungWatchdog') {
  $flags.Add('hung_watchdog') | Out-Null
}
if ($relaySignal -and [string]$relaySignal.derived_status -eq 'StaleActive') {
  $flags.Add('stale_active') | Out-Null
}
if ($relaySignal -and [int]$relaySignal.last_progress_age_seconds -ge [int]$rules.hung_progress_age_seconds -and [string]$relaySignal.derived_status -eq 'Active') {
  $flags.Add('long_progress_gap') | Out-Null
}

$flags = @($flags | Select-Object -Unique)
$status = if ($flags.Count -gt 0) { 'anomaly' } else { 'ok' }
$marker = if ($status -eq 'ok') {
  '[ANOMALY] status=ok'
} else {
  '[ANOMALY] status=anomaly flags=' + ($flags -join ',')
}

$summary = [pscustomobject]@{
  generated_at = (Get-Date).ToString('o')
  card_game_root = $CardGameRoot
  manifest_path = $ManifestPath
  rules_path = $rulesPath
  governance_path = $governancePath
  relay_signal_path = $relaySignalPath
  manager_signal_path = $managerSignalPath
  status = $status
  flags = @($flags)
  summary_marker = $marker
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$textDir = Split-Path -Parent $OutputTextPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($textDir) { New-Item -ItemType Directory -Force -Path $textDir | Out-Null }

$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
@(
  $summary.summary_marker
  if ($summary.flags.Count -gt 0) { '[ANOMALY_FLAGS] ' + ($summary.flags -join ', ') } else { '' }
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Set-Content -LiteralPath $OutputTextPath -Encoding UTF8

Write-Output ($summary | ConvertTo-Json -Depth 6)
