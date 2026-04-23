param(
  [string]$ManifestPath,
  [string]$OutputJsonPath = '',
  [string]$OutputMarkdownPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-execution-route.json'
}
if (-not $OutputMarkdownPath) {
  $OutputMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-execution-route.md'
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
  throw "Manifest not found: $ManifestPath"
}

function Get-RepresentativeTarget {
  param($Manifest)

  $cardGameRoot = if ($Manifest.repo.root) { [string]$Manifest.repo.root } else { '' }
  $bucket = if ($Manifest.task.bucket) { [string]$Manifest.task.bucket } else { '' }
  $largestFile = if ($Manifest.guidance.context_surface -and $Manifest.guidance.context_surface.largest_file) {
    [string]$Manifest.guidance.context_surface.largest_file
  } else {
    ''
  }

  if ($largestFile -and $cardGameRoot -and $largestFile.StartsWith($cardGameRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    return $largestFile.Substring($cardGameRoot.Length).TrimStart('\')
  }

  switch ($bucket) {
    'ui-runtime' { return 'Assets/Scripts/UI/BattleHUD.cs' }
    'battle-runtime' { return 'Assets/Scripts/Battle/BattleManager.cs' }
    'map-runtime' { return 'Assets/Scripts/Map' }
    'network-runtime' { return 'Assets/Scripts/Network' }
    'qa-editor' { return 'Assets/Scripts/Editor/QA' }
    'editmode-tests' { return 'Assets/Tests/EditMode/Editor' }
    'docs-or-autopilot' { return '.autopilot/STATE.md' }
    default { return '' }
  }
}

function Get-CodexRouteSuggestion {
  param(
    [string]$CardGameRoot,
    [string]$RepresentativeTarget
  )

  if (-not $CardGameRoot -or -not $RepresentativeTarget) {
    return ''
  }

  $projectScriptPath = Join-Path $CardGameRoot '.autopilot\project.ps1'
  if (-not (Test-Path -LiteralPath $projectScriptPath)) {
    return ''
  }

  try {
    $output = & powershell -ExecutionPolicy Bypass -File $projectScriptPath codex-route -Note $RepresentativeTarget 2>&1
    return ($output | Out-String).Trim()
  } catch {
    return ''
  }
}

function Get-AsmdefReadinessSuggestion {
  param(
    [string]$CardGameRoot,
    [string]$RepresentativeTarget
  )

  if (-not $CardGameRoot -or -not $RepresentativeTarget) {
    return ''
  }

  $projectScriptPath = Join-Path $CardGameRoot '.autopilot\project.ps1'
  if (-not (Test-Path -LiteralPath $projectScriptPath)) {
    return ''
  }

  try {
    $output = & powershell -ExecutionPolicy Bypass -File $projectScriptPath codex-asmdef-readiness -Note $RepresentativeTarget 2>&1
    return ($output | Out-String).Trim()
  } catch {
    return ''
  }
}

function Get-NextCommands {
  param(
    [string]$CardGameRoot,
    [string]$RepresentativeTarget,
    [string]$Mode,
    [string[]]$RecommendedReadPath
  )

  $commands = New-Object System.Collections.Generic.List[string]
  if (-not $CardGameRoot) {
    return @($commands)
  }

  $commands.Add(("Set-Location '{0}'" -f $CardGameRoot))

  if ($RepresentativeTarget) {
    $commands.Add(("powershell -ExecutionPolicy Bypass -File .autopilot\\project.ps1 codex-route -Note '{0}'" -f $RepresentativeTarget))
    $commands.Add(("powershell -ExecutionPolicy Bypass -File .autopilot\\project.ps1 codex-asmdef-readiness -Note '{0}'" -f $RepresentativeTarget))
  }

  switch ($Mode) {
    'docs-lite' {
      $commands.Add("Get-Content -Raw '.autopilot\\PROMPT.codex-lite.md'")
      $commands.Add("Get-Content -Raw '.autopilot\\AGENTS.md'")
    }
    'direct-codex' {
      foreach ($path in @($RecommendedReadPath)) {
        if (-not [string]::IsNullOrWhiteSpace($path)) {
          $commands.Add(("Get-Content -Raw '{0}'" -f $path))
        }
      }
      if ($RepresentativeTarget) {
        $commands.Add(("Get-Content -Raw '{0}'" -f $RepresentativeTarget))
      }
    }
    default {
      $commands.Add("powershell -ExecutionPolicy Bypass -File 'D:\\cardgame-dad-relay\\scripts\\card-game\\Start-CardGameRelay.ps1' -TaskSlug '<task-slug>'")
    }
  }

  return @($commands)
}

$manifest = Get-Content -Raw -LiteralPath $ManifestPath -Encoding UTF8 | ConvertFrom-Json
$mode = if ($manifest.guidance.execution_mode.mode) { [string]$manifest.guidance.execution_mode.mode } else { 'relay-dad' }
$reason = if ($manifest.guidance.execution_mode.reason) { [string]$manifest.guidance.execution_mode.reason } else { 'missing execution mode reason' }
$readPath = @($manifest.guidance.recommended_read_path)
$representativeTarget = Get-RepresentativeTarget -Manifest $manifest
$codexRouteSuggestion = Get-CodexRouteSuggestion -CardGameRoot ([string]$manifest.repo.root) -RepresentativeTarget $representativeTarget
$asmdefReadinessSuggestion = Get-AsmdefReadinessSuggestion -CardGameRoot ([string]$manifest.repo.root) -RepresentativeTarget $representativeTarget
$nextCommands = Get-NextCommands -CardGameRoot ([string]$manifest.repo.root) -RepresentativeTarget $representativeTarget -Mode $mode -RecommendedReadPath $readPath

$recommendedAction = switch ($mode) {
  'docs-lite' { 'Skip desktop relay. Use the lightest prompt/profile path for docs or autopilot maintenance.' }
  'direct-codex' { 'Skip desktop relay. Run a narrow single-agent slice against one target file plus research/read-path artifacts.' }
  default { 'Run the desktop relay. This slice is expensive or cross-boundary enough to justify DAD coordination.' }
}

$route = [ordered]@{
  generated_at = (Get-Date).ToString('o')
  session_id = [string]$manifest.session_id
  task_slug = [string]$manifest.task.slug
  bucket = [string]$manifest.task.bucket
  execution_mode = $mode
  execution_mode_reason = $reason
  recommended_action = $recommendedAction
  recommended_read_path = $readPath
  representative_target = $representativeTarget
  codex_route_suggestion = $codexRouteSuggestion
  asmdef_readiness_suggestion = $asmdefReadinessSuggestion
  next_commands = $nextCommands
  verification_expectation = [string]$manifest.guidance.verification_expectation
  token_policy = [string]$manifest.guidance.token_policy
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$mdDir = Split-Path -Parent $OutputMarkdownPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($mdDir) { New-Item -ItemType Directory -Force -Path $mdDir | Out-Null }

$route | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Card Game Execution Route')
$lines.Add('')
$lines.Add('Generated at: ' + $route.generated_at)
$lines.Add('Session id: ' + $route.session_id)
$lines.Add('Task slug: ' + $route.task_slug)
$lines.Add('Bucket: ' + $route.bucket)
$lines.Add('Execution mode: ' + $route.execution_mode)
$lines.Add('Execution mode reason: ' + $route.execution_mode_reason)
$lines.Add('Recommended action: ' + $route.recommended_action)
$lines.Add('Representative target: ' + $route.representative_target)
$lines.Add('Verification expectation: ' + $route.verification_expectation)
$lines.Add('Token policy: ' + $route.token_policy)
$lines.Add('')
$lines.Add('## Read Path')
$lines.Add('')
foreach ($path in $route.recommended_read_path) {
  $lines.Add('- ' + $path)
}

if ($route.codex_route_suggestion) {
  $lines.Add('')
  $lines.Add('## Codex Route Suggestion')
  $lines.Add('')
  foreach ($line in ($route.codex_route_suggestion -split "`r?`n")) {
    $lines.Add($line)
  }
}

if ($route.asmdef_readiness_suggestion) {
  $lines.Add('')
  $lines.Add('## Asmdef Readiness')
  $lines.Add('')
  foreach ($line in ($route.asmdef_readiness_suggestion -split "`r?`n")) {
    $lines.Add($line)
  }
}

if ($route.next_commands -and @($route.next_commands).Count -gt 0) {
  $lines.Add('')
  $lines.Add('## Next Commands')
  $lines.Add('')
  foreach ($command in @($route.next_commands)) {
    $lines.Add('- `' + $command + '`')
  }
}

[string]::Join("`r`n", $lines) | Set-Content -LiteralPath $OutputMarkdownPath -Encoding UTF8

Write-Host "Wrote execution route JSON: $OutputJsonPath"
Write-Host "Wrote execution route Markdown: $OutputMarkdownPath"
