param(
  [string]$ManifestPath,
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$RoutePath = '',
  [string]$RunbookPath = '',
  [string]$DirectPromptPath = '',
  [string]$OutputJsonPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $RoutePath) {
  $RoutePath = Join-Path $repoRoot 'profiles\card-game\generated-execution-route.json'
}
if (-not $RunbookPath) {
  $RunbookPath = Join-Path $repoRoot 'profiles\card-game\generated-runbook.json'
}
if (-not $DirectPromptPath) {
  $DirectPromptPath = Join-Path $repoRoot 'profiles\card-game\generated-direct-codex-prompt.md'
}
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $CardGameRoot '.autopilot\generated\relay-route-handoff.json'
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
  throw "Manifest not found: $ManifestPath"
}
if (-not (Test-Path -LiteralPath $RoutePath)) {
  throw "Route artifact not found: $RoutePath"
}

$manifest = Get-Content -Raw -LiteralPath $ManifestPath -Encoding UTF8 | ConvertFrom-Json
$route = Get-Content -Raw -LiteralPath $RoutePath -Encoding UTF8 | ConvertFrom-Json
$runbook = if (Test-Path -LiteralPath $RunbookPath) {
  Get-Content -Raw -LiteralPath $RunbookPath -Encoding UTF8 | ConvertFrom-Json
} else {
  $null
}

$handoff = [ordered]@{
  generated_at = (Get-Date).ToString('o')
  relay_root = $repoRoot.Path
  card_game_root = $CardGameRoot
  session_id = [string]$manifest.session_id
  task_slug = [string]$manifest.task.slug
  task_bucket = [string]$manifest.task.bucket
  task_summary = [string]$manifest.task.summary
  execution_mode = [string]$route.execution_mode
  execution_mode_reason = [string]$route.execution_mode_reason
  representative_target = [string]$route.representative_target
  verification_expectation = [string]$route.verification_expectation
  token_policy = [string]$route.token_policy
  route_path = $RoutePath
  runbook_path = if (Test-Path -LiteralPath $RunbookPath) { $RunbookPath } else { '' }
  direct_prompt_path = if (Test-Path -LiteralPath $DirectPromptPath) { $DirectPromptPath } else { '' }
  recommended_read_path = @($route.recommended_read_path)
  next_commands = if ($runbook) { @($runbook.next_commands) } else { @($route.next_commands) }
}

$dir = Split-Path -Parent $OutputJsonPath
if ($dir) {
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

$handoff | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8
Write-Host "Wrote route handoff JSON: $OutputJsonPath"
