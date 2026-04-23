param(
  [string]$TaskSlug = '',
  [string]$OutputPath = '',
  [string]$BacklogHealthPath = '',
  [string]$ContextSurfacePath = ''
)

$ErrorActionPreference = 'Stop'

$cardGameRoot = 'D:\Unity\card game'
$autopilotRoot = Join-Path $cardGameRoot '.autopilot'
$dialogueRoot = Join-Path $cardGameRoot 'Document\dialogue'
$backlogPath = Join-Path $autopilotRoot 'BACKLOG.md'
$statePath = Join-Path $autopilotRoot 'STATE.md'
$operatorDecisionsPath = Join-Path $autopilotRoot 'OPERATOR-DECISIONS.md'
$dadDecisionsPath = Join-Path $dialogueRoot 'DECISIONS.md'
$relayRepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$heuristicsPath = Join-Path $relayRepoRoot 'docs\card-game-integration\learning-memory\heuristics.json'

if (-not $OutputPath) {
  $OutputPath = Join-Path $env:TEMP 'cardgame-dad-admission.json'
}

function Get-DecisionValue {
  param(
    [string[]]$Lines,
    [string]$DecisionName
  )

  if (-not $Lines -or -not $DecisionName) {
    return ''
  }

  $escaped = [regex]::Escape($DecisionName)
  foreach ($line in $Lines) {
    if ($line -match "DECISION:\s*$escaped\s+(.+)$") {
      return $Matches[1].Trim().Trim('`')
    }
  }

  return ''
}

function Test-LooksCorruptText {
  param([string]$Text)

  if (-not $Text) {
    return $false
  }

  $repeatedQuestionRuns = ([regex]::Matches($Text, '\?{2,}')).Count
  $questionCount = ([regex]::Matches($Text, '\?')).Count
  return ($repeatedQuestionRuns -ge 2 -or $questionCount -ge 6)
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
  param(
    [string[]]$Lines,
    [string]$RequestedTaskSlug
  )

  $items = @()
  foreach ($line in $Lines) {
    if ($line -match '^\- \[(P\d+)\] \*\*([^\*]+)\*\* -- (.+)$') {
      $items += [pscustomobject]@{
        Priority = $Matches[1]
        Slug = $Matches[2].Trim()
        Summary = $Matches[3].Trim()
        Raw = $line.Trim()
      }
    }
  }

  if ($RequestedTaskSlug) {
    $matched = $items | Where-Object { $_.Slug -eq $RequestedTaskSlug } | Select-Object -First 1
    if ($matched) {
      return $matched
    }
  }

  return $items | Select-Object -First 1
}

function Get-TaskBucket {
  param([string]$Slug)

  $s = ''
  if ($Slug) {
    $s = $Slug.ToLowerInvariant()
  }
  if ($s -match 'battle|combat|boss|enemy') { return 'battle-runtime' }
  if ($s -match 'ui|hud|popup|panel|layout|reward') { return 'ui-runtime' }
  if ($s -match 'map|route|probe|sector|zone') { return 'map-runtime' }
  if ($s -match 'network|session|sync|race') { return 'network-runtime' }
  if ($s -match 'qa|screenshot|automation|smoke|test') { return 'qa-editor' }
  if ($s -match 'research|doc|dialogue|autopilot|decision') { return 'docs-or-autopilot' }
  if ($s -match 'companion|party|character|loadout') { return 'ui-runtime' }
  return 'general'
}

function Get-RecommendedReadPath {
  param([string]$Bucket)

  switch ($Bucket) {
    'battle-runtime' { return @('AGENTS.md', 'Assets/Scripts/Battle/AGENTS.md', 'Assets/Scripts/Battle/Battle-research.md') }
    'ui-runtime' { return @('AGENTS.md', 'Assets/Scripts/UI/AGENTS.md', 'Assets/Scripts/UI/UI-research.md') }
    'map-runtime' { return @('AGENTS.md', 'Assets/Scripts/Map/AGENTS.md', 'Assets/Scripts/Map/Map-research.md') }
    'network-runtime' { return @('AGENTS.md', 'Assets/Scripts/Network/AGENTS.md', 'Assets/Scripts/Network/Network-research.md') }
    'qa-editor' { return @('AGENTS.md', 'Assets/Scripts/Editor/AGENTS.md', 'Assets/Scripts/Editor/QA/AGENTS.md', 'Assets/Scripts/Editor/Editor-research.md') }
    'docs-or-autopilot' { return @('AGENTS.md', '.autopilot/AGENTS.md', 'PROJECT-RULES.md') }
    default { return @('AGENTS.md', 'PROJECT-RULES.md') }
  }
}

function Get-HeuristicForBucket {
  param(
    [string]$HeuristicsPath,
    [string]$Bucket
  )

  if (-not $HeuristicsPath -or -not (Test-Path -LiteralPath $HeuristicsPath)) {
    return $null
  }

  try {
    $heuristics = Get-Content -Raw -LiteralPath $HeuristicsPath -Encoding UTF8 | ConvertFrom-Json
    if (-not $heuristics.buckets) {
      return $null
    }

    return $heuristics.buckets |
      Where-Object { $_.bucket -eq $Bucket } |
      Select-Object -First 1
  } catch {
    return $null
  }
}

function Get-BacklogHealth {
  param([string]$Path)

  if (-not $Path -or -not (Test-Path -LiteralPath $Path)) {
    return $null
  }

  try {
    return Get-Content -Raw -LiteralPath $Path -Encoding UTF8 | ConvertFrom-Json
  } catch {
    return $null
  }
}

function Get-ContextSurface {
  param([string]$Path)

  if (-not $Path -or -not (Test-Path -LiteralPath $Path)) {
    return $null
  }

  try {
    return Get-Content -Raw -LiteralPath $Path -Encoding UTF8 | ConvertFrom-Json
  } catch {
    return $null
  }
}

function Get-ExecutionModeGuidance {
  param(
    [string]$Bucket,
    $BucketSurface,
    $BucketHeuristic
  )

  if ($Bucket -eq 'docs-or-autopilot') {
    return [ordered]@{
      mode = 'docs-lite'
      reason = 'Docs and autopilot maintenance usually do not need full DAD; start with the lightest prompt/profile.'
    }
  }

  if ($BucketHeuristic -and $BucketHeuristic.preferred_execution_mode -and $BucketHeuristic.route_samples -ge 1) {
    return [ordered]@{
      mode = [string]$BucketHeuristic.preferred_execution_mode
      reason = "Learned from route/session history for bucket $Bucket across $($BucketHeuristic.route_samples) route sample(s)."
    }
  }

  if ($BucketSurface -and $BucketSurface.preferred_execution_mode) {
    return [ordered]@{
      mode = [string]$BucketSurface.preferred_execution_mode
      reason = [string]$BucketSurface.execution_mode_reason
    }
  }

  if ($Bucket -eq 'qa-editor' -or $Bucket -eq 'editmode-tests') {
    return [ordered]@{
      mode = 'relay-dad'
      reason = 'QA and editor slices usually benefit from peer verification and tool coordination.'
    }
  }

  return [ordered]@{
    mode = 'direct-codex'
    reason = 'Default to the cheapest single-agent slice unless the task clearly crosses boundaries.'
  }
}

$backlogLines = if (Test-Path $backlogPath) { Get-Content -LiteralPath $backlogPath -Encoding UTF8 } else { @() }
$stateLines = if (Test-Path $statePath) { Get-Content -LiteralPath $statePath -Encoding UTF8 } else { @() }
$operatorDecisionLines = if (Test-Path $operatorDecisionsPath) { Get-Content -LiteralPath $operatorDecisionsPath -Encoding UTF8 } else { @() }
$dadDecisionLines = if (Test-Path $dadDecisionsPath) { Get-Content -LiteralPath $dadDecisionsPath -Encoding UTF8 } else { @() }

$item = Get-TopBacklogItem -Lines $backlogLines -RequestedTaskSlug $TaskSlug
if (-not $item) {
  throw "No backlog item found."
}

$bucket = Get-TaskBucket -Slug $item.Slug
$readPath = Get-RecommendedReadPath -Bucket $bucket
$bucketHeuristic = Get-HeuristicForBucket -HeuristicsPath $heuristicsPath -Bucket $bucket
$backlogHealth = Get-BacklogHealth -Path $BacklogHealthPath
$contextSurface = Get-ContextSurface -Path $ContextSurfacePath
$bucketSurface = $null
if ($contextSurface -and $contextSurface.buckets) {
  $bucketSurface = $contextSurface.buckets | Where-Object { $_.bucket -eq $bucket } | Select-Object -First 1
}
$executionMode = Get-ExecutionModeGuidance -Bucket $bucket -BucketSurface $bucketSurface -BucketHeuristic $bucketHeuristic
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$taskSummary = $item.Summary
$rawBacklogLine = $item.Raw
$taskSummaryLooksCorrupt = Test-LooksCorruptText $taskSummary
$rawBacklogLineLooksCorrupt = Test-LooksCorruptText $rawBacklogLine
$admissionWarnings = New-Object System.Collections.Generic.List[string]

if ($taskSummaryLooksCorrupt) {
  $taskSummary = 'Backlog summary looks encoding-damaged; re-open BACKLOG.md locally before widening scope.'
  $admissionWarnings.Add('backlog_summary_corrupt')
}

if ($rawBacklogLineLooksCorrupt) {
  $rawBacklogLine = 'Backlog line looks encoding-damaged; use task slug plus fresh local read.'
  $admissionWarnings.Add('backlog_line_corrupt')
}
if ($backlogHealth -and -not $backlogHealth.auto_promotion_safe) {
  $admissionWarnings.Add('backlog_auto_promotion_blocked')
}

$manifest = [ordered]@{
  created_at = (Get-Date).ToString('o')
  session_id = "$($item.Slug)-$timestamp"
  task = [ordered]@{
    slug = $item.Slug
    priority = $item.Priority
    summary = $taskSummary
    raw_backlog_line = $rawBacklogLine
    bucket = $bucket
    summary_was_corrupt = $taskSummaryLooksCorrupt
    raw_line_was_corrupt = $rawBacklogLineLooksCorrupt
  }
  repo = [ordered]@{
    root = $cardGameRoot
    backlog = $backlogPath
    autopilot_state = $statePath
    dialogue_decisions = $dadDecisionsPath
  }
  decisions = [ordered]@{
    post_mvp = (Get-DecisionValue -Lines $operatorDecisionLines -DecisionName 'post-mvp')
    focus = (Get-DecisionValue -Lines $operatorDecisionLines -DecisionName 'focus')
    human_review = (Get-DecisionValue -Lines $operatorDecisionLines -DecisionName 'human-review')
    dad_focus = (Get-DecisionValue -Lines $dadDecisionLines -DecisionName 'focus')
    next_session = (Get-DecisionValue -Lines $dadDecisionLines -DecisionName 'next-session')
    approval = (Get-DecisionValue -Lines $dadDecisionLines -DecisionName 'approval')
  }
  state = [ordered]@{
    status = (Get-StateValue -Lines $stateLines -Key 'status')
    mvp_gates = (Get-StateValue -Lines $stateLines -Key 'mvp_gates')
    build_status = (Get-StateValue -Lines $stateLines -Key 'build_status')
  }
  guidance = [ordered]@{
    recommended_read_path = $readPath
    verification_expectation = 'Use the narrowest compile/test/Unity QA path that can close this slice.'
    token_policy = 'Reuse stable prefix, keep one narrow slice, avoid broad repo search.'
    execution_mode = $executionMode
    admission_warnings = @($admissionWarnings)
    backlog_health = if ($backlogHealth) {
      [ordered]@{
        auto_promotion_safe = $backlogHealth.auto_promotion_safe
        corrupt_item_count = $backlogHealth.corrupt_item_count
        recommendation = $backlogHealth.recommendation
      }
    } else {
      $null
    }
    learned_policy = if ($bucketHeuristic) { $bucketHeuristic.recommended_policy } else { '' }
    context_surface = if ($bucketSurface) {
      [ordered]@{
        asmdef_status = if ($contextSurface) { $contextSurface.asmdef_status } else { 'unknown' }
        giant_file_count = $bucketSurface.giant_file_count
        largest_file = $bucketSurface.largest_file
        largest_file_kb = $bucketSurface.largest_file_kb
        recommendation = $bucketSurface.recommendation
        preferred_execution_mode = $bucketSurface.preferred_execution_mode
        execution_mode_reason = $bucketSurface.execution_mode_reason
      }
    } else {
      $null
    }
    learned_bucket_stats = if ($bucketHeuristic) {
      [ordered]@{
        sessions = $bucketHeuristic.sessions
        converged = $bucketHeuristic.converged
        stopped = $bucketHeuristic.stopped
        avg_input_tokens = $bucketHeuristic.avg_input_tokens
        avg_output_tokens = $bucketHeuristic.avg_output_tokens
        avg_turns = $bucketHeuristic.avg_turns
      }
    } else {
      $null
    }
  }
}

$manifestJson = $manifest | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($OutputPath, $manifestJson, [System.Text.Encoding]::UTF8)
Write-Host "Wrote admission manifest: $OutputPath"
