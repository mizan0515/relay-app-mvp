param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$LoopStatusPath = '',
  [string]$ContextSurfacePath = '',
  [string]$ExecutionRoutePath = '',
  [string]$HeuristicsPath = '',
  [string]$OutputJsonPath = '',
  [string]$OutputMarkdownPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$statePath = Join-Path $CardGameRoot '.autopilot\STATE.md'
$backlogPath = Join-Path $CardGameRoot '.autopilot\BACKLOG.md'

if (-not $LoopStatusPath) {
  $LoopStatusPath = Join-Path $repoRoot 'profiles\card-game\generated-loop-status.json'
}
if (-not $ContextSurfacePath) {
  $ContextSurfacePath = Join-Path $repoRoot 'profiles\card-game\generated-context-surface.json'
}
if (-not $ExecutionRoutePath) {
  $ExecutionRoutePath = Join-Path $repoRoot 'profiles\card-game\generated-execution-route.json'
}
if (-not $HeuristicsPath) {
  $HeuristicsPath = Join-Path $repoRoot 'docs\card-game-integration\learning-memory\heuristics.json'
}
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-ops-dashboard.json'
}
if (-not $OutputMarkdownPath) {
  $OutputMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-ops-dashboard.md'
}

function Get-StateValue {
  param(
    [string[]]$Lines,
    [string]$Key
  )

  $escaped = [regex]::Escape($Key)
  foreach ($line in $Lines) {
    if ($line -match "^${escaped}:\s*(.+)$") {
      return $Matches[1].Trim().Trim('"')
    }
  }

  return ''
}

function Get-TopBacklogItem {
  param([string[]]$Lines)

  foreach ($line in $Lines) {
    if ($line -match '^\- \[(P\d+)\] \*\*([^\*]+)\*\* -- (.+)$') {
      return [pscustomobject]@{
        priority = $Matches[1]
        slug = $Matches[2].Trim()
        summary = $Matches[3].Trim()
      }
    }
  }

  return $null
}

$stateLines = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Encoding UTF8 } else { @() }
$backlogLines = if (Test-Path -LiteralPath $backlogPath) { Get-Content -LiteralPath $backlogPath -Encoding UTF8 } else { @() }
$loopStatus = if (Test-Path -LiteralPath $LoopStatusPath) { Get-Content -Raw -LiteralPath $LoopStatusPath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$contextSurface = if (Test-Path -LiteralPath $ContextSurfacePath) { Get-Content -Raw -LiteralPath $ContextSurfacePath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$executionRoute = if (Test-Path -LiteralPath $ExecutionRoutePath) { Get-Content -Raw -LiteralPath $ExecutionRoutePath -Encoding UTF8 | ConvertFrom-Json } else { $null }
$heuristics = if (Test-Path -LiteralPath $HeuristicsPath) { Get-Content -Raw -LiteralPath $HeuristicsPath -Encoding UTF8 | ConvertFrom-Json } else { $null }

$topBacklog = Get-TopBacklogItem -Lines $backlogLines
$topBucketHeuristic = $null
if ($heuristics -and $loopStatus -and $loopStatus.execution_mode) {
  $topBucketHeuristic = @($heuristics.buckets | Where-Object { $_.preferred_execution_mode -eq $loopStatus.execution_mode } | Select-Object -First 1)
}

$dashboard = [ordered]@{
  generated_at = (Get-Date).ToString('o')
  state = [ordered]@{
    iteration = Get-StateValue -Lines $stateLines -Key 'iteration'
    status = Get-StateValue -Lines $stateLines -Key 'status'
    mvp_gates = Get-StateValue -Lines $stateLines -Key 'mvp_gates'
    active_task = Get-StateValue -Lines $stateLines -Key 'active_task'
  }
  backlog = [ordered]@{
    top_priority = if ($topBacklog) { [string]$topBacklog.priority } else { '' }
    top_slug = if ($topBacklog) { [string]$topBacklog.slug } else { '' }
    top_summary = if ($topBacklog) { [string]$topBacklog.summary } else { '' }
  }
  loop = if ($loopStatus) { $loopStatus } else { $null }
  context = if ($contextSurface) {
    [ordered]@{
      asmdef_status = [string]$contextSurface.asmdef_status
      highest_risk_bucket = [string]$contextSurface.highest_risk_bucket
      highest_risk_recommendation = [string]$contextSurface.highest_risk_recommendation
    }
  } else {
    $null
  }
  structure = if ($executionRoute) {
    [ordered]@{
      representative_target = [string]$executionRoute.representative_target
      asmdef_readiness_suggestion = [string]$executionRoute.asmdef_readiness_suggestion
    }
  } else {
    $null
  }
  heuristics = if ($heuristics) {
    [ordered]@{
      total_route_records = $heuristics.total_route_records
      top_mode_bucket = if ($topBucketHeuristic.Count -gt 0) { [string]$topBucketHeuristic[0].bucket } else { '' }
      top_mode_route_samples = if ($topBucketHeuristic.Count -gt 0) { $topBucketHeuristic[0].route_samples } else { 0 }
      top_mode_policy = if ($topBucketHeuristic.Count -gt 0) { [string]$topBucketHeuristic[0].recommended_policy } else { '' }
    }
  } else {
    $null
  }
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$mdDir = Split-Path -Parent $OutputMarkdownPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($mdDir) { New-Item -ItemType Directory -Force -Path $mdDir | Out-Null }

$dashboard | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Card Game Ops Dashboard')
$lines.Add('')
$lines.Add('Generated at: ' + $dashboard.generated_at)
$lines.Add('Iteration: ' + $dashboard.state.iteration)
$lines.Add('Status: ' + $dashboard.state.status)
$lines.Add('MVP gates: ' + $dashboard.state.mvp_gates)
$lines.Add('Active task: ' + $dashboard.state.active_task)
$lines.Add('')
$lines.Add('## Backlog')
$lines.Add('')
$lines.Add('- Top priority: ' + $dashboard.backlog.top_priority)
$lines.Add('- Top slug: ' + $dashboard.backlog.top_slug)
$lines.Add('- Top summary: ' + $dashboard.backlog.top_summary)
$lines.Add('')
$lines.Add('## Loop')
$lines.Add('')
if ($dashboard.loop) {
  $lines.Add('- Next action: ' + $dashboard.loop.next_action)
  $lines.Add('- Execution mode: ' + $dashboard.loop.execution_mode)
  $lines.Add('- Execution mode reason: ' + $dashboard.loop.execution_mode_reason)
  if ($dashboard.loop.execution_route_path) { $lines.Add('- Execution route: ' + $dashboard.loop.execution_route_path) }
  if ($dashboard.loop.direct_prompt_path) { $lines.Add('- Direct prompt: ' + $dashboard.loop.direct_prompt_path) }
  if ($dashboard.loop.runbook_path) { $lines.Add('- Runbook: ' + $dashboard.loop.runbook_path) }
}
$lines.Add('')
$lines.Add('## Context')
$lines.Add('')
if ($dashboard.context) {
  $lines.Add('- asmdef status: ' + $dashboard.context.asmdef_status)
  $lines.Add('- Highest risk bucket: ' + $dashboard.context.highest_risk_bucket)
  $lines.Add('- Highest risk recommendation: ' + $dashboard.context.highest_risk_recommendation)
}
$lines.Add('')
$lines.Add('## Structure')
$lines.Add('')
if ($dashboard.structure) {
  $lines.Add('- Representative target: ' + $dashboard.structure.representative_target)
  if ($dashboard.structure.asmdef_readiness_suggestion) {
    foreach ($line in ($dashboard.structure.asmdef_readiness_suggestion -split "`r?`n")) {
      $lines.Add($line)
    }
  }
}
$lines.Add('')
$lines.Add('## Learning')
$lines.Add('')
if ($dashboard.heuristics) {
  $lines.Add('- Total route records: ' + $dashboard.heuristics.total_route_records)
  $lines.Add('- Mode-aligned bucket: ' + $dashboard.heuristics.top_mode_bucket)
  $lines.Add('- Mode-aligned route samples: ' + $dashboard.heuristics.top_mode_route_samples)
  $lines.Add('- Mode-aligned policy: ' + $dashboard.heuristics.top_mode_policy)
}

[string]::Join("`r`n", $lines) | Set-Content -LiteralPath $OutputMarkdownPath -Encoding UTF8

Write-Host "Wrote ops dashboard JSON: $OutputJsonPath"
Write-Host "Wrote ops dashboard Markdown: $OutputMarkdownPath"
