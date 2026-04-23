param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$OutputJsonPath = '',
  [string]$OutputMarkdownPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $OutputJsonPath) {
  $OutputJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-context-surface.json'
}
if (-not $OutputMarkdownPath) {
  $OutputMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-context-surface.md'
}

function Get-BucketSummary {
  param(
    [string]$Bucket,
    [string]$Path
  )

  if (-not (Test-Path -LiteralPath $Path)) {
    return [pscustomobject]@{
      bucket = $Bucket
      path = $Path
      file_count = 0
      giant_file_count = 0
      largest_file = ''
      largest_file_kb = 0
      recommendation = 'path missing'
    }
  }

  $files = @(Get-ChildItem -LiteralPath $Path -Recurse -File -Filter *.cs)
  $giants = @($files | Where-Object { $_.Length -ge 60kb } | Sort-Object Length -Descending)
  $largest = $files | Sort-Object Length -Descending | Select-Object -First 1

  $recommendation = if ($giants.Count -ge 3) {
    'high context surface: keep slices tiny and prefer one target file plus research'
  } elseif ($giants.Count -ge 1) {
    'medium context surface: avoid broad rereads and prefer direct file targeting'
  } else {
    'lower context surface: direct Codex slice is usually cheaper than DAD'
  }

  $executionMode = 'direct-codex'
  $executionModeReason = 'small or medium local slice: direct Codex is usually cheaper than relay coordination.'

  if ($Bucket -eq 'qa-editor' -or $Bucket -eq 'editmode-tests') {
    $executionMode = 'relay-dad'
    $executionModeReason = 'QA/editor work tends to cross verification boundaries; peer review is usually worth the extra coordination.'
  } elseif ($giants.Count -ge 3) {
    $executionMode = 'direct-codex'
    $executionModeReason = 'Large runtime files make repo reacquisition expensive; start with a one-file direct Codex slice before escalating to DAD.'
  }

  [pscustomobject]@{
    bucket = $Bucket
    path = $Path
    file_count = $files.Count
    giant_file_count = $giants.Count
    largest_file = if ($largest) { $largest.FullName } else { '' }
    largest_file_kb = if ($largest) { [math]::Round($largest.Length / 1kb, 1) } else { 0 }
    recommendation = $recommendation
    preferred_execution_mode = $executionMode
    execution_mode_reason = $executionModeReason
  }
}

$asmdefs = @(Get-ChildItem -LiteralPath (Join-Path $CardGameRoot 'Assets') -Recurse -File -Filter *.asmdef -ErrorAction SilentlyContinue)
$bucketSummaries = @(
  Get-BucketSummary -Bucket 'ui-runtime' -Path (Join-Path $CardGameRoot 'Assets\Scripts\UI')
  Get-BucketSummary -Bucket 'battle-runtime' -Path (Join-Path $CardGameRoot 'Assets\Scripts\Battle')
  Get-BucketSummary -Bucket 'map-runtime' -Path (Join-Path $CardGameRoot 'Assets\Scripts\Map')
  Get-BucketSummary -Bucket 'network-runtime' -Path (Join-Path $CardGameRoot 'Assets\Scripts\Network')
  Get-BucketSummary -Bucket 'qa-editor' -Path (Join-Path $CardGameRoot 'Assets\Scripts\Editor\QA')
  Get-BucketSummary -Bucket 'editmode-tests' -Path (Join-Path $CardGameRoot 'Assets\Tests\EditMode\Editor')
)

$highestRisk = $bucketSummaries | Sort-Object giant_file_count, largest_file_kb -Descending | Select-Object -First 1
$report = [ordered]@{
  generated_at = (Get-Date).ToString('o')
  card_game_root = $CardGameRoot
  asmdef_count = $asmdefs.Count
  asmdef_status = if ($asmdefs.Count -eq 0) { 'none' } else { 'present' }
  highest_risk_bucket = if ($highestRisk) { $highestRisk.bucket } else { '' }
  highest_risk_recommendation = if ($highestRisk) { $highestRisk.recommendation } else { '' }
  buckets = $bucketSummaries
  operator_guidance = @(
    'If asmdef_count is zero, keep validation narrow and avoid assuming fast compile feedback.',
    'If a bucket has giant_file_count >= 3, prefer one-file direct Codex slices before escalating to full DAD.',
    'Use DAD when the slice crosses ownership boundaries or requires peer verification, not as the default for giant files.',
    'Treat editmode-tests as a separate context surface from runtime buckets.',
    'Use preferred_execution_mode as a routing hint, not a hard rule; operator focus or risky cross-boundary work can override it.'
  )
}

$jsonDir = Split-Path -Parent $OutputJsonPath
$mdDir = Split-Path -Parent $OutputMarkdownPath
if ($jsonDir) { New-Item -ItemType Directory -Force -Path $jsonDir | Out-Null }
if ($mdDir) { New-Item -ItemType Directory -Force -Path $mdDir | Out-Null }

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Card Game Context Surface')
$lines.Add('')
$lines.Add('Generated at: ' + $report.generated_at)
$lines.Add('asmdef count: ' + $report.asmdef_count)
$lines.Add('asmdef status: ' + $report.asmdef_status)
$lines.Add('Highest risk bucket: ' + $report.highest_risk_bucket)
$lines.Add('Highest risk recommendation: ' + $report.highest_risk_recommendation)
$lines.Add('')
$lines.Add('## Buckets')
$lines.Add('')
foreach ($bucket in $bucketSummaries) {
  $lines.Add('### ' + $bucket.bucket)
  $lines.Add('- File count: ' + $bucket.file_count)
  $lines.Add('- Giant file count (>=60KB): ' + $bucket.giant_file_count)
  $lines.Add('- Largest file KB: ' + $bucket.largest_file_kb)
  if ($bucket.largest_file) {
    $lines.Add('- Largest file: `' + $bucket.largest_file + '`')
  }
  $lines.Add('- Recommendation: ' + $bucket.recommendation)
  $lines.Add('- Preferred execution mode: ' + $bucket.preferred_execution_mode)
  $lines.Add('- Execution mode reason: ' + $bucket.execution_mode_reason)
  $lines.Add('')
}

$lines.Add('## Operator Guidance')
$lines.Add('')
foreach ($guidance in $report.operator_guidance) {
  $lines.Add('- ' + $guidance)
}

[string]::Join("`r`n", $lines) | Set-Content -LiteralPath $OutputMarkdownPath -Encoding UTF8

Write-Host "Wrote context surface JSON: $OutputJsonPath"
Write-Host "Wrote context surface Markdown: $OutputMarkdownPath"
