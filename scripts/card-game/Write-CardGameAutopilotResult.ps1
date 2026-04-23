param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$ManifestPath = '',
  [string]$SessionStatePath = '',
  [string]$LearningRecordPath = '',
  [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $ManifestPath) {
  $ManifestPath = Join-Path $repoRoot 'profiles\card-game\generated-admission.json'
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
  throw "Manifest not found: $ManifestPath"
}

$autopilotRoot = Join-Path $CardGameRoot '.autopilot'
$dialogueRoot = Join-Path $CardGameRoot 'Document\dialogue'
$stateMdPath = Join-Path $autopilotRoot 'STATE.md'
$historyMdPath = Join-Path $autopilotRoot 'HISTORY.md'
$metricsPath = Join-Path $autopilotRoot 'METRICS.jsonl'

$manifest = Get-Content -Raw -LiteralPath $ManifestPath -Encoding UTF8 | ConvertFrom-Json
$sessionId = [string]$manifest.session_id

if (-not $SessionStatePath) {
  $SessionStatePath = Join-Path $dialogueRoot "sessions\$sessionId\state.json"
}

if (-not $LearningRecordPath) {
  $LearningRecordPath = Join-Path $dialogueRoot 'learning-memory\session-outcomes.jsonl'
}

$relayEvidenceScriptPath = Join-Path $PSScriptRoot 'Get-CardGameRelayEvidence.ps1'

if (-not (Test-Path -LiteralPath $SessionStatePath)) {
  throw "Session state not found: $SessionStatePath"
}

$sessionState = Get-Content -Raw -LiteralPath $SessionStatePath -Encoding UTF8 | ConvertFrom-Json
if ($sessionState.session_id) {
  $sessionId = [string]$sessionState.session_id
}

$terminalStatuses = @('converged', 'abandoned', 'stopped', 'failed')
if ($terminalStatuses -notcontains ([string]$sessionState.session_status)) {
  throw "Session is not terminal; refusing autopilot write-back for status '$($sessionState.session_status)'."
}

function Get-LearningRecordForSession {
  param(
    [string]$Path,
    [string]$TargetSessionId
  )

  if (-not (Test-Path -LiteralPath $Path)) {
    return $null
  }

  $last = $null
  foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
    if (-not $line.Trim()) { continue }
    $obj = $line | ConvertFrom-Json
    if ($obj.session_id -eq $TargetSessionId) {
      $last = $obj
    }
  }

  return $last
}

function Get-FlatStateValue {
  param(
    [System.Collections.Generic.List[string]]$Lines,
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

function Set-FlatStateValue {
  param(
    [System.Collections.Generic.List[string]]$Lines,
    [string]$Key,
    [string]$Value
  )

  $escaped = [regex]::Escape($Key)
  for ($i = 0; $i -lt $Lines.Count; $i++) {
    if ($Lines[$i] -match "^${escaped}:\s*(.+)$") {
      $Lines[$i] = "${Key}: $Value"
      return
    }
  }

  $Lines.Add("${Key}: $Value")
}

function Replace-StateHistory {
  param(
    [System.Collections.Generic.List[string]]$Lines,
    [string[]]$Bullets
  )

  $start = -1
  for ($i = 0; $i -lt $Lines.Count; $i++) {
    if ($Lines[$i] -match '^history:\s*$') {
      $start = $i
      break
    }
  }

  if ($start -lt 0) {
    $Lines.Add('history:')
    $start = $Lines.Count - 1
  }

  $end = $Lines.Count
  for ($j = $start + 1; $j -lt $Lines.Count; $j++) {
    if ($Lines[$j] -match '^\S') {
      $end = $j
      break
    }
  }

  for ($remove = $end - 1; $remove -gt $start; $remove--) {
    $Lines.RemoveAt($remove)
  }

  for ($index = 0; $index -lt $Bullets.Count; $index++) {
    $escapedBullet = $Bullets[$index].Replace('"', '\"')
    $Lines.Insert($start + 1 + $index, "  - `"$escapedBullet`"")
  }
}

function Get-HistorySections {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path)) {
    return @()
  }

  $lines = Get-Content -LiteralPath $Path -Encoding UTF8
  $sections = New-Object System.Collections.Generic.List[object]
  $current = $null
  foreach ($line in $lines) {
    if ($line -match '^##\s*iter\s+(\d+)\s+\(([^)]+)\)\s*-\s*(.+)$') {
      if ($current) { $sections.Add([pscustomobject]$current) }
      $current = [ordered]@{
        Iter = [int]$Matches[1]
        Date = $Matches[2]
        Tail = $Matches[3]
        Bullets = New-Object System.Collections.Generic.List[string]
      }
      continue
    }

    if ($current -and $line -match '^\-\s*(.+)$') {
      $current.Bullets.Add($Matches[1])
    }
  }

  if ($current) { $sections.Add([pscustomobject]$current) }
  return $sections.ToArray()
}

function Write-HistorySections {
  param(
    [string]$Path,
    [object[]]$Sections
  )

  $header = @(
    '# HISTORY -- completed iterations, newest first, keep last 10',
    '',
    'Archive older entries to HISTORY-ARCHIVE.md when this file exceeds 10 entries.',
    'Max 3 bullets per entry. No paragraphs.',
    ''
  )

  $lines = New-Object System.Collections.Generic.List[string]
  foreach ($line in $header) { $lines.Add($line) }

  foreach ($section in $Sections) {
    $lines.Add("## iter $($section.Iter) ($($section.Date)) - $($section.Tail)")
    foreach ($bullet in $section.Bullets) {
      $lines.Add("- $bullet")
    }
    $lines.Add('')
  }

  [System.IO.File]::WriteAllLines($Path, $lines, [System.Text.Encoding]::UTF8)
}

function Get-ResultBullets {
  param(
    $Manifest,
    $SessionState,
    $Learning
  )

  $taskSlug = [string]$Manifest.task.slug
  $bucket = [string]$Manifest.task.bucket
  $statusText = [string]$SessionState.session_status
  $buildSummary = if ($Learning -and $Learning.done_reason) {
    [string]$Learning.done_reason
  } elseif ($SessionState.closed_reason) {
    [string]$SessionState.closed_reason
  } else {
    'relay session closed.'
  }

  $verifySummary = if ($Learning -and $Learning.checkpoint_summary) {
    'verify: ' + (($Learning.checkpoint_summary | Select-Object -First 3) -join ', ')
  } else {
    'verify: session state and handoff evidence recorded.'
  }

  $tokenSummary = if ($Learning) {
    "tokens: input $($Learning.total_input_tokens), output $($Learning.total_output_tokens), turns $($Learning.current_turn)"
  } else {
    'tokens: learning record missing'
  }

  $cleanSummary = ($buildSummary -replace '\s+', ' ').Trim()

  return @(
    "done: $taskSlug ($bucket) session closed as $statusText; summary: $cleanSummary",
    $verifySummary,
    $tokenSummary
  )
}

$learning = Get-LearningRecordForSession -Path $LearningRecordPath -TargetSessionId $sessionId
$stateLines = New-Object System.Collections.Generic.List[string]
foreach ($line in (Get-Content -LiteralPath $stateMdPath -Encoding UTF8)) {
  $stateLines.Add($line)
}

$currentIteration = 0
[int]::TryParse((Get-FlatStateValue -Lines $stateLines -Key 'iteration'), [ref]$currentIteration) | Out-Null
$nextIteration = $currentIteration + 1

$resultStatus = [string]$sessionState.session_status
$taskSlug = [string]$manifest.task.slug
$resultBullets = Get-ResultBullets -Manifest $manifest -SessionState $sessionState -Learning $learning
$buildStatusText = if ($learning -and $learning.done_reason) {
  [string]$learning.done_reason
} elseif ($sessionState.closed_reason) {
  [string]$sessionState.closed_reason
} else {
  "relay session $sessionId closed"
}

Set-FlatStateValue -Lines $stateLines -Key 'iteration' -Value $nextIteration
Set-FlatStateValue -Lines $stateLines -Key 'status' -Value ("relay-{0}-{1}" -f $resultStatus, $taskSlug)
Set-FlatStateValue -Lines $stateLines -Key 'active_task' -Value 'null'
Set-FlatStateValue -Lines $stateLines -Key 'build_status' -Value ('"' + $buildStatusText.Replace('"', '\"') + '"')
Replace-StateHistory -Lines $stateLines -Bullets $resultBullets

$historySections = @(Get-HistorySections -Path $historyMdPath)
$newSection = [pscustomobject]@{
  Iter = $nextIteration
  Date = (Get-Date).ToString('yyyy-MM-dd')
  Tail = "relay - [$resultStatus] $taskSlug"
  Bullets = $resultBullets
}
$updatedSections = @($newSection) + @($historySections | Select-Object -First 9)
$relayEvidence = $null
if (Test-Path -LiteralPath $relayEvidenceScriptPath) {
  try {
    $relayEvidence = & powershell -ExecutionPolicy Bypass -File $relayEvidenceScriptPath -CardGameRoot $CardGameRoot -SessionId $sessionId | ConvertFrom-Json
  } catch {
    $relayEvidence = $null
  }
}

$relayMcpCalls = 0
$relayUnityMcpCalls = 0
$relayEventLogPath = ''
if ($relayEvidence) {
  if ($relayEvidence.PSObject.Properties.Name -contains 'mcp_calls_observed') {
    $relayMcpCalls = [int]$relayEvidence.mcp_calls_observed
  } elseif ($relayEvidence.PSObject.Properties.Name -contains 'tool_events_observed') {
    $relayMcpCalls = [int]$relayEvidence.tool_events_observed
  }

  if ($relayEvidence.PSObject.Properties.Name -contains 'unity_mcp_calls_observed') {
    $relayUnityMcpCalls = [int]$relayEvidence.unity_mcp_calls_observed
  } elseif ($relayEvidence.PSObject.Properties.Name -contains 'unity_mcp_calls') {
    $relayUnityMcpCalls = [int]$relayEvidence.unity_mcp_calls
  }

  if ($relayEvidence.PSObject.Properties.Name -contains 'event_log_path') {
    $relayEventLogPath = [string]$relayEvidence.event_log_path
  }
}

$metrics = [ordered]@{
  iter = $nextIteration
  ts = (Get-Date).ToString('o')
  mode = 'active'
  status = "relay-$resultStatus"
  duration_s = 0
  files_read = 0
  bash_calls = 0
  mcp_calls = $relayMcpCalls
  unity_mcp_calls = $relayUnityMcpCalls
  commits = 0
  prs = 0
  merged = 0
  editmode_tests = 0
  screenshots = 0
  budget_exceeded = $null
  mvp_gates_passing = (Get-FlatStateValue -Lines $stateLines -Key 'mvp_gates')
  relay_session_id = $sessionId
  relay_origin_backlog_id = [string]$manifest.task.slug
  relay_bucket = [string]$manifest.task.bucket
  relay_turns = if ($learning) { $learning.current_turn } else { $sessionState.current_turn }
  relay_input_tokens = if ($learning) { $learning.total_input_tokens } else { 0 }
  relay_output_tokens = if ($learning) { $learning.total_output_tokens } else { 0 }
  relay_cache_read_tokens = if ($learning) { $learning.total_cache_read_input_tokens } else { 0 }
  relay_cost_claude_usd = if ($learning) { $learning.total_cost_claude_usd } else { 0 }
  relay_cost_codex_usd = if ($learning) { $learning.total_cost_codex_usd } else { 0 }
  relay_unity_mcp_calls = $relayUnityMcpCalls
  relay_event_log_path = $relayEventLogPath
}

if ($WhatIf) {
  Write-Host 'STATE.md preview:'
  $stateLines | Select-Object -First 60 | ForEach-Object { Write-Host $_ }
  Write-Host ''
  Write-Host 'HISTORY preview header:'
  Write-Host "## iter $($newSection.Iter) ($($newSection.Date)) - $($newSection.Tail)"
  $newSection.Bullets | ForEach-Object { Write-Host "- $_" }
  Write-Host ''
  Write-Host 'METRICS preview:'
  Write-Host ($metrics | ConvertTo-Json -Compress)
  exit 0
}

[System.IO.File]::WriteAllLines($stateMdPath, $stateLines, [System.Text.Encoding]::UTF8)
Write-HistorySections -Path $historyMdPath -Sections $updatedSections
Add-Content -LiteralPath $metricsPath -Value (($metrics | ConvertTo-Json -Compress)) -Encoding UTF8

Write-Host "Updated STATE.md: $stateMdPath"
Write-Host "Updated HISTORY.md: $historyMdPath"
Write-Host "Appended METRICS.jsonl: $metricsPath"
