param(
  [string]$ManifestPath,
  [string]$RoutePath = '',
  [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $RoutePath) {
  $RoutePath = Join-Path $repoRoot 'profiles\card-game\generated-execution-route.json'
}
if (-not $OutputPath) {
  $OutputPath = Join-Path $repoRoot 'docs\card-game-integration\learning-memory\route-outcomes.jsonl'
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
  throw "Manifest not found: $ManifestPath"
}
if (-not (Test-Path -LiteralPath $RoutePath)) {
  throw "Route artifact not found: $RoutePath"
}

$manifest = Get-Content -Raw -LiteralPath $ManifestPath -Encoding UTF8 | ConvertFrom-Json
$route = Get-Content -Raw -LiteralPath $RoutePath -Encoding UTF8 | ConvertFrom-Json

$record = [ordered]@{
  recorded_at = (Get-Date).ToString('o')
  session_id = [string]$route.session_id
  task_slug = [string]$route.task_slug
  task_bucket = [string]$route.bucket
  execution_mode = [string]$route.execution_mode
  execution_mode_reason = [string]$route.execution_mode_reason
  recommended_action = [string]$route.recommended_action
  verification_expectation = [string]$route.verification_expectation
  token_policy = [string]$route.token_policy
  read_path_count = @($route.recommended_read_path).Count
  asmdef_status = if ($manifest.guidance.context_surface) { [string]$manifest.guidance.context_surface.asmdef_status } else { 'unknown' }
  giant_file_count = if ($manifest.guidance.context_surface) { [int]$manifest.guidance.context_surface.giant_file_count } else { 0 }
  largest_file_kb = if ($manifest.guidance.context_surface) { [double]$manifest.guidance.context_surface.largest_file_kb } else { 0 }
}

$dir = Split-Path -Parent $OutputPath
if ($dir) {
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

($record | ConvertTo-Json -Depth 6 -Compress) + [Environment]::NewLine |
  Add-Content -LiteralPath $OutputPath -Encoding UTF8

Write-Host "Appended route learning record: $OutputPath"
