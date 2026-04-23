param(
  [string]$LearningPath = '',
  [string]$RouteLearningPath = '',
  [string]$OutputJsonPath = '',
  [string]$OutputMarkdownPath = ''
)

$ErrorActionPreference = 'Stop'

function Get-RoundedAverage {
  param(
    [object[]]$Items,
    [string]$PropertyName,
    [int]$Digits
  )

  if (-not $Items -or $Items.Count -eq 0) {
    return 0
  }

  $average = ($Items | Measure-Object -Property $PropertyName -Average).Average
  if ($null -eq $average) {
    return 0
  }

  return [math]::Round([double]$average, $Digits)
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

if (-not $LearningPath) {
  $LearningPath = 'D:\Unity\card game\Document\dialogue\learning-memory\session-outcomes.jsonl'
}

if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'docs\card-game-integration\learning-memory\heuristics.json'
}

if (-not $OutputMarkdownPath) {
  $OutputMarkdownPath = Join-Path $repoRoot 'docs\card-game-integration\learning-memory\heuristics.md'
}

if (-not $RouteLearningPath) {
  $RouteLearningPath = Join-Path $repoRoot 'docs\card-game-integration\learning-memory\route-outcomes.jsonl'
}

$routeRecords = @()
if (Test-Path -LiteralPath $RouteLearningPath) {
  $routeRecords = @(
    Get-Content -LiteralPath $RouteLearningPath -Encoding UTF8 |
      Where-Object { $_.Trim() } |
      ForEach-Object { $_ | ConvertFrom-Json }
  )
}

$records = @()
if (Test-Path -LiteralPath $LearningPath) {
  $records = @(
    Get-Content -LiteralPath $LearningPath -Encoding UTF8 |
      Where-Object { $_.Trim() } |
      ForEach-Object { $_ | ConvertFrom-Json }
  )
}

if ($records.Count -eq 0 -and $routeRecords.Count -eq 0) {
  throw "No learning records found. Session path: $LearningPath ; route path: $RouteLearningPath"
}

$allBuckets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($record in $records) {
  if (-not [string]::IsNullOrWhiteSpace($record.task_bucket)) {
    [void]$allBuckets.Add([string]$record.task_bucket)
  }
}
foreach ($routeRecord in $routeRecords) {
  if (-not [string]::IsNullOrWhiteSpace($routeRecord.task_bucket)) {
    [void]$allBuckets.Add([string]$routeRecord.task_bucket)
  }
}
if ($allBuckets.Count -eq 0) {
  [void]$allBuckets.Add('unknown')
}

$bucketStats = @(
  $allBuckets |
    Sort-Object |
    ForEach-Object {
      $currentBucket = [string]$_
      $items = @($records | Where-Object { $_.task_bucket -eq $currentBucket })
      $converged = @($items | Where-Object { $_.session_status -eq 'converged' }).Count
      $stopped = @($items | Where-Object { $_.session_status -eq 'stopped' }).Count
      $avgInput = Get-RoundedAverage -Items $items -PropertyName 'total_input_tokens' -Digits 1
      $avgOutput = Get-RoundedAverage -Items $items -PropertyName 'total_output_tokens' -Digits 1
      $avgTurns = Get-RoundedAverage -Items $items -PropertyName 'current_turn' -Digits 2
      $bucketRouteRecords = @($routeRecords | Where-Object { $_.task_bucket -eq $currentBucket })
      $routeModeCounts = @{}
      foreach ($routeItem in $bucketRouteRecords) {
        $mode = [string]$routeItem.execution_mode
        if (-not $mode) { continue }
        if (-not $routeModeCounts.ContainsKey($mode)) {
          $routeModeCounts[$mode] = 0
        }
        $routeModeCounts[$mode]++
      }

      $preferredExecutionMode = 'relay-dad'
      if ($routeModeCounts.Count -gt 0) {
        $preferredExecutionMode = ($routeModeCounts.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key
      } elseif ($avgInput -lt 80000 -and $avgTurns -le 3) {
        $preferredExecutionMode = 'direct-codex'
      }

      $recommendedPolicy = if ($avgInput -gt 120000 -or $avgTurns -gt 5) {
        'shrink slice and narrow read path'
      } elseif ($items.Count -ge 2 -and $converged -eq 0) {
        'change verification recipe before retry'
      } elseif ($bucketRouteRecords.Count -ge 1 -and $items.Count -eq 0) {
        'use route learning until terminal session evidence exists'
      } else {
        'keep current slice size'
      }

      [pscustomobject]@{
        bucket = $currentBucket
        sessions = $items.Count
        converged = $converged
        stopped = $stopped
        avg_input_tokens = $avgInput
        avg_output_tokens = $avgOutput
        avg_turns = $avgTurns
        recommended_policy = $recommendedPolicy
        route_samples = $bucketRouteRecords.Count
        preferred_execution_mode = $preferredExecutionMode
      }
    } |
    Sort-Object bucket
)

$heuristics = [ordered]@{
  generated_at = (Get-Date).ToString('o')
  source_learning_path = $LearningPath
  source_route_learning_path = $RouteLearningPath
  total_sessions = $records.Count
  total_route_records = $routeRecords.Count
  buckets = $bucketStats
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$mdDir = Split-Path -Parent $OutputMarkdownPath

if ($jsonDir) {
  New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null
}

if ($mdDir) {
  New-Item -ItemType Directory -Force -Path $mdDir | Out-Null
}

$heuristics |
  ConvertTo-Json -Depth 8 |
  Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Card Game Heuristics')
$lines.Add('')
$lines.Add('Generated at: ' + $heuristics.generated_at)
$lines.Add('Source: `' + $LearningPath + '`')
$lines.Add('Route source: `' + $RouteLearningPath + '`')
$lines.Add('Total sessions: ' + $heuristics.total_sessions)
$lines.Add('Total route records: ' + $heuristics.total_route_records)
$lines.Add('')
$lines.Add('## Bucket summary')
$lines.Add('')

foreach ($bucket in $bucketStats) {
  $lines.Add('### ' + $bucket.bucket)
  $lines.Add('- Sessions: ' + $bucket.sessions)
  $lines.Add('- Converged: ' + $bucket.converged)
  $lines.Add('- Stopped: ' + $bucket.stopped)
  $lines.Add('- Avg input tokens: ' + $bucket.avg_input_tokens)
  $lines.Add('- Avg output tokens: ' + $bucket.avg_output_tokens)
  $lines.Add('- Avg turns: ' + $bucket.avg_turns)
  $lines.Add('- Recommended policy: ' + $bucket.recommended_policy)
  $lines.Add('- Route samples: ' + $bucket.route_samples)
  $lines.Add('- Preferred execution mode: ' + $bucket.preferred_execution_mode)
  $lines.Add('')
}

[string]::Join("`r`n", $lines) |
  Set-Content -LiteralPath $OutputMarkdownPath -Encoding UTF8

Write-Host "Wrote heuristics JSON: $OutputJsonPath"
Write-Host "Wrote heuristics Markdown: $OutputMarkdownPath"
