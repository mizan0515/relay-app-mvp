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
  $OutputPath = Join-Path $repoRoot 'profiles\card-game\generated-direct-codex-prompt.md'
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
  throw "Manifest not found: $ManifestPath"
}
if (-not (Test-Path -LiteralPath $RoutePath)) {
  throw "Route artifact not found: $RoutePath"
}

$manifest = Get-Content -Raw -LiteralPath $ManifestPath -Encoding UTF8 | ConvertFrom-Json
$route = Get-Content -Raw -LiteralPath $RoutePath -Encoding UTF8 | ConvertFrom-Json

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Direct Codex Route Prompt')
$lines.Add('')
$lines.Add('Use this prompt when the relay resolved the slice to a cheaper direct path instead of desktop DAD.')
$lines.Add('')
$lines.Add('Task:')
$lines.Add('- slug: ' + [string]$manifest.task.slug)
$lines.Add('- bucket: ' + [string]$manifest.task.bucket)
$lines.Add('- summary: ' + [string]$manifest.task.summary)
$lines.Add('')
$lines.Add('Operating constraints:')
$lines.Add('- Stay inside one narrow slice.')
$lines.Add('- Read only the recommended path before widening scope.')
$lines.Add('- Prefer the representative target first.')
$lines.Add('- Verification must stay narrow and inspectable.')
$lines.Add('')
$lines.Add('Route summary:')
$lines.Add('- execution_mode: ' + [string]$route.execution_mode)
$lines.Add('- execution_mode_reason: ' + [string]$route.execution_mode_reason)
$lines.Add('- representative_target: ' + [string]$route.representative_target)
$lines.Add('- verification_expectation: ' + [string]$route.verification_expectation)
$lines.Add('- token_policy: ' + [string]$route.token_policy)
$lines.Add('')
$lines.Add('Read path:')
foreach ($path in @($route.recommended_read_path)) {
  $lines.Add('- ' + [string]$path)
}

if ($route.codex_route_suggestion) {
  $lines.Add('')
  $lines.Add('Codex route suggestion:')
  foreach ($line in ([string]$route.codex_route_suggestion -split "`r?`n")) {
    if ($line) {
      $lines.Add($line)
    }
  }
}

if ($route.next_commands -and @($route.next_commands).Count -gt 0) {
  $lines.Add('')
  $lines.Add('Suggested commands:')
  foreach ($command in @($route.next_commands)) {
    $lines.Add('- ' + [string]$command)
  }
}

$lines.Add('')
$lines.Add('Required output:')
$lines.Add('- name the exact file or tiny file set you will touch')
$lines.Add('- name the narrow verification step before editing')
$lines.Add('- keep the slice small enough that direct Codex stays cheaper than relay DAD')

$outDir = Split-Path -Parent $OutputPath
if ($outDir) {
  New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

[string]::Join("`r`n", $lines) | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host "Wrote direct route prompt: $OutputPath"
