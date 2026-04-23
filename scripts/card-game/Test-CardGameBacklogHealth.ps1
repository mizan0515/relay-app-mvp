param(
  [string]$BacklogPath = 'D:\Unity\card game\.autopilot\BACKLOG.md',
  [string]$OutputJsonPath = '',
  [string]$OutputMarkdownPath = ''
)

$ErrorActionPreference = 'Stop'

function Test-LooksCorruptText {
  param([string]$Text)

  if ([string]::IsNullOrWhiteSpace($Text)) {
    return $false
  }

  $repeatedQuestionRuns = ([regex]::Matches($Text, '\?{2,}')).Count
  $questionCount = ([regex]::Matches($Text, '\?')).Count
  return ($repeatedQuestionRuns -ge 2 -or $questionCount -ge 6)
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-backlog-health.json'
}
if (-not $OutputMarkdownPath) {
  $OutputMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-backlog-health.md'
}

if (-not (Test-Path -LiteralPath $BacklogPath)) {
  throw "Backlog not found: $BacklogPath"
}

$lines = Get-Content -LiteralPath $BacklogPath -Encoding UTF8
$items = New-Object System.Collections.Generic.List[object]

foreach ($line in $lines) {
  if ($line -match '^\- \[(P\d+)\] \*\*([^\*]+)\*\* -- (.+)$') {
    $summary = $Matches[3].Trim()
    $isCorrupt = Test-LooksCorruptText $summary
    $items.Add([pscustomobject]@{
        priority = $Matches[1]
        slug = $Matches[2].Trim()
        summary = $summary
        summary_was_corrupt = $isCorrupt
      })
  }
}

$topItem = $items | Select-Object -First 1
$corruptItems = @($items | Where-Object { $_.summary_was_corrupt })
$report = [ordered]@{
  generated_at = (Get-Date).ToString('o')
  backlog_path = $BacklogPath
  total_items = $items.Count
  corrupt_item_count = $corruptItems.Count
  auto_promotion_safe = [bool]($topItem -and -not $topItem.summary_was_corrupt)
  recommendation = if (-not $topItem) {
    'no backlog items found'
  } elseif ($topItem.summary_was_corrupt) {
    'freeze auto-promotion and re-read BACKLOG.md locally before widening scope'
  } elseif ($corruptItems.Count -gt 0) {
    'allow admission by slug, but keep corruption warnings visible until backlog cleanup'
  } else {
    'backlog is healthy enough for automatic admission'
  }
  top_item = $topItem
  corrupt_items = @($corruptItems | Select-Object priority, slug, summary)
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$mdDir = Split-Path -Parent $OutputMarkdownPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($mdDir) { New-Item -ItemType Directory -Force -Path $mdDir | Out-Null }

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# Card Game Backlog Health')
$mdLines.Add('')
$mdLines.Add('Generated at: ' + $report.generated_at)
$mdLines.Add('Backlog: `' + $report.backlog_path + '`')
$mdLines.Add('Total items: ' + $report.total_items)
$mdLines.Add('Corrupt items: ' + $report.corrupt_item_count)
$mdLines.Add('Auto-promotion safe: ' + $report.auto_promotion_safe)
$mdLines.Add('Recommendation: ' + $report.recommendation)
$mdLines.Add('')

if ($topItem) {
  $mdLines.Add('## Top Item')
  $mdLines.Add('')
  $mdLines.Add('- Priority: ' + $topItem.priority)
  $mdLines.Add('- Slug: ' + $topItem.slug)
  $mdLines.Add('- Summary corrupt: ' + $topItem.summary_was_corrupt)
  $mdLines.Add('- Summary: ' + $topItem.summary)
  $mdLines.Add('')
}

if ($corruptItems.Count -gt 0) {
  $mdLines.Add('## Corrupt Items')
  $mdLines.Add('')
  foreach ($item in $corruptItems) {
    $mdLines.Add('### ' + $item.slug)
    $mdLines.Add('- Priority: ' + $item.priority)
    $mdLines.Add('- Summary: ' + $item.summary)
    $mdLines.Add('')
  }
}

[string]::Join("`r`n", $mdLines) | Set-Content -LiteralPath $OutputMarkdownPath -Encoding UTF8

Write-Host "Wrote backlog health JSON: $OutputJsonPath"
Write-Host "Wrote backlog health Markdown: $OutputMarkdownPath"
