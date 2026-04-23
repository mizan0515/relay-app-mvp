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
$skillContractsPath = Join-Path $relayRepoRoot 'profiles\card-game\skill-contracts.json'
$skillsRoot = Join-Path $relayRepoRoot 'skills\card-game'

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

function Get-SkillContract {
  param(
    [string]$ContractsPath,
    [string]$Bucket
  )

  $defaultContract = [ordered]@{
    required_skills = @()
    required_evidence = @()
    forbidden_tools = @()
  }

  if (-not (Test-Path -LiteralPath $ContractsPath)) {
    return $defaultContract
  }

  try {
    $contracts = Get-Content -Raw -LiteralPath $ContractsPath -Encoding UTF8 | ConvertFrom-Json
    $resolved = [ordered]@{
      required_skills = @()
      required_evidence = @()
      forbidden_tools = @()
    }

    if ($contracts.default) {
      $resolved.required_skills += @($contracts.default.required_skills)
      $resolved.required_evidence += @($contracts.default.required_evidence)
      $resolved.forbidden_tools += @($contracts.default.forbidden_tools)
    }

    if ($contracts.buckets -and $contracts.buckets.PSObject.Properties.Name -contains $Bucket) {
      $bucketContract = $contracts.buckets.$Bucket
      $resolved.required_skills += @($bucketContract.required_skills)
      $resolved.required_evidence += @($bucketContract.required_evidence)
      $resolved.forbidden_tools += @($bucketContract.forbidden_tools)
    }

    $resolved.required_skills = @($resolved.required_skills | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    $resolved.required_evidence = @($resolved.required_evidence | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    $resolved.forbidden_tools = @($resolved.forbidden_tools | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    return $resolved
  } catch {
    return $defaultContract
  }
}

function Resolve-SkillPaths {
  param(
    [string]$SkillsRoot,
    [string[]]$SkillNames
  )

  $resolved = New-Object System.Collections.Generic.List[object]
  foreach ($skillName in @($SkillNames)) {
    if ([string]::IsNullOrWhiteSpace($skillName)) {
      continue
    }

    $skillPath = Join-Path $SkillsRoot "$skillName\SKILL.md"
    $resolved.Add([pscustomobject]@{
      name = $skillName
      path = if (Test-Path -LiteralPath $skillPath) { $skillPath } else { '' }
      exists = [bool](Test-Path -LiteralPath $skillPath)
    })
  }

  return $resolved.ToArray()
}

function Get-AgentIdentityProfiles {
  param(
    [string]$Bucket,
    [string]$ExecutionMode
  )

  $profiles = New-Object 'System.Collections.Generic.List[string]'
  $profiles.Add('cardgame-autopilot-manager')

  switch ($ExecutionMode) {
    'relay-dad' {
      $profiles.Add('cardgame-relay-codex-peer')
      $profiles.Add('cardgame-relay-claude-peer')
    }
    'direct-codex' {
      $profiles.Add('cardgame-route-direct-codex')
    }
    'docs-lite' {
      $profiles.Add('cardgame-route-direct-codex')
    }
  }

  if ($Bucket -in @('battle-runtime', 'map-runtime', 'qa-editor', 'ui-runtime')) {
    $profiles.Add('cardgame-unity-mcp-bridge')
  }

  return @($profiles | Select-Object -Unique)
}

function Get-ApprovedToolProfiles {
  param(
    [string]$Bucket,
    [string]$ExecutionMode
  )

  $profiles = New-Object 'System.Collections.Generic.List[string]'
  $profiles.Add('cardgame-compact-artifact-surface') | Out-Null
  $profiles.Add('cardgame-powershell-runbooks') | Out-Null

  switch ($ExecutionMode) {
    'relay-dad' {
      $profiles.Add('cardgame-relay-codex-cli') | Out-Null
      $profiles.Add('cardgame-relay-claude-cli') | Out-Null
    }
    'direct-codex' {
      $profiles.Add('cardgame-direct-codex-local') | Out-Null
    }
    'docs-lite' {
      $profiles.Add('cardgame-direct-codex-local') | Out-Null
    }
  }

  if ($Bucket -in @('battle-runtime', 'map-runtime', 'qa-editor', 'ui-runtime')) {
    $profiles.Add('cardgame-unity-mcp-approved') | Out-Null
  }

  return @($profiles | Select-Object -Unique)
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
$skillContract = Get-SkillContract -ContractsPath $skillContractsPath -Bucket $bucket
$skillPaths = Resolve-SkillPaths -SkillsRoot $skillsRoot -SkillNames $skillContract.required_skills
$backlogHealth = Get-BacklogHealth -Path $BacklogHealthPath
$contextSurface = Get-ContextSurface -Path $ContextSurfacePath
$bucketSurface = $null
if ($contextSurface -and $contextSurface.buckets) {
  $bucketSurface = $contextSurface.buckets | Where-Object { $_.bucket -eq $bucket } | Select-Object -First 1
}
$executionMode = Get-ExecutionModeGuidance -Bucket $bucket -BucketSurface $bucketSurface -BucketHeuristic $bucketHeuristic
$agentIdentityProfiles = Get-AgentIdentityProfiles -Bucket $bucket -ExecutionMode $executionMode.mode
$approvedToolProfiles = Get-ApprovedToolProfiles -Bucket $bucket -ExecutionMode $executionMode.mode
$activePolicyProfiles = @('cardgame-compact-artifacts-only', 'cardgame-no-full-log-tail')
if ($bucket -eq 'qa-editor') { $activePolicyProfiles += 'cardgame-no-web-for-unity-local' }
if ($bucket -in @('battle-runtime', 'map-runtime', 'qa-editor', 'ui-runtime')) { $activePolicyProfiles += 'cardgame-require-unity-mcp-proof' }
$activePolicyProfiles = @($activePolicyProfiles | Select-Object -Unique)
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
    required_skills = @($skillContract.required_skills)
    required_skill_paths = @($skillPaths)
    required_evidence = @($skillContract.required_evidence)
    forbidden_tools = @($skillContract.forbidden_tools)
    forbidden_tool_policy = [ordered]@{
      full_log_tail = 'never read or tail the full relay JSONL log during routine operation; use compact signal/evidence artifacts only'
      web = 'for Unity-local qa-editor slices, do not browse the web unless the operator explicitly asks for external research'
    }
    approved_tool_ids = @($approvedToolProfiles)
    tool_registry_path = 'D:\cardgame-dad-relay\profiles\card-game\tool-registry.json'
    active_policy_ids = @($activePolicyProfiles)
    policy_registry_path = 'D:\cardgame-dad-relay\profiles\card-game\policy-registry.json'
    enforcement_notes = @(
      'required_evidence is enforced by loop status and completion write-back',
      'required_skills must be opened from required_skill_paths before broad repo exploration',
      'forbidden_tools is a prompt/operator contract unless a deterministic gate already exists',
      'approved_tool_ids must resolve through the central tool registry before the slice is trusted',
      'active_policy_ids must resolve through the central policy registry before the slice is trusted'
    )
    verification_expectation = 'Use the narrowest compile/test/Unity QA path that can close this slice.'
    token_policy = 'Reuse stable prefix, keep one narrow slice, avoid broad repo search.'
    agent_identity_profiles = @($agentIdentityProfiles)
    agent_identity_policy = 'Every autopilot, relay, route, and Unity-MCP role must resolve to a distinct registered identity before the slice is trusted.'
    tool_registry_policy = 'Every active runtime, script runner, relay peer, and Unity MCP bridge must resolve to a centrally registered approved tool before execution continues.'
    policy_registry_policy = 'Every active compact-policy contract must resolve to a centrally registered policy before the slice is trusted.'
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
