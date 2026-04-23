param(
  [string]$TaskSlug = '',
  [string]$TaskSummary = '',
  [string]$ManifestPath = '',
  [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
$profileRoot = Join-Path $repoRoot 'profiles\card-game'
$cardGameRoot = 'D:\Unity\card game'
$backlogPath = Join-Path $cardGameRoot '.autopilot\BACKLOG.md'
$statePath = Join-Path $cardGameRoot '.autopilot\STATE.md'
$prefixPath = Join-Path $profileRoot 'prompt-prefix.md'

if (-not $OutputPath) {
  $OutputPath = Join-Path $profileRoot 'generated-session-prompt.md'
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

$prefix = (Get-Content -Raw -LiteralPath $prefixPath -Encoding UTF8).Trim()
$backlogExcerpt = ''
$manifest = $null

if ($ManifestPath -and (Test-Path $ManifestPath)) {
  $manifest = Get-Content -Raw -LiteralPath $ManifestPath -Encoding UTF8 | ConvertFrom-Json
  if (-not $TaskSlug -and $manifest.task.slug) {
    $TaskSlug = $manifest.task.slug
  }
  if (-not $TaskSummary -and $manifest.task.summary) {
    $TaskSummary = $manifest.task.summary
  }
  if ($manifest.task.raw_backlog_line) {
    $backlogExcerpt = [string]$manifest.task.raw_backlog_line
  }
}

if ((-not $backlogExcerpt) -and (Test-Path $backlogPath)) {
  $backlogLines = Get-Content -LiteralPath $backlogPath -Encoding UTF8
  if ($TaskSlug) {
    $matched = $backlogLines | Where-Object { $_ -match [regex]::Escape($TaskSlug) } | Select-Object -First 1
    if ($matched) {
      $backlogExcerpt = $matched.Trim()
    }
  }

  if (-not $backlogExcerpt) {
    $matchedP1 = $backlogLines | Where-Object { $_ -match '^\- \[P1\]' } | Select-Object -First 1
    if ($matchedP1) {
      $backlogExcerpt = $matchedP1.Trim()
    }
  }
}

if (Test-LooksCorruptText $backlogExcerpt) {
  $backlogExcerpt = 'Top backlog item text looks encoding-damaged; re-read BACKLOG.md locally before expanding scope.'
}

$stateSummary = ''
if (Test-Path $statePath) {
  $stateLines = Get-Content -LiteralPath $statePath -Encoding UTF8
  $stateSummary = ($stateLines | Where-Object { $_ -match '^(status|build_status|mvp_gates):' }) -join "`n"
}

if (Test-LooksCorruptText $stateSummary) {
  $stateSummary = 'status/build excerpt looks encoding-damaged; re-open STATE.md before trusting summary text.'
}

$builder = New-Object System.Text.StringBuilder
$null = $builder.AppendLine($prefix)
$null = $builder.AppendLine()
$null = $builder.AppendLine('Current task tail:')
if ($TaskSlug) {
  $null = $builder.AppendLine("- task_slug: $TaskSlug")
}
if ($TaskSummary) {
  $null = $builder.AppendLine("- task_summary: $TaskSummary")
}
if ($backlogExcerpt) {
  $null = $builder.AppendLine("- backlog_excerpt: $backlogExcerpt")
}
if ($stateSummary) {
  $null = $builder.AppendLine('- state_excerpt:')
  foreach ($line in ($stateSummary -split "`r?`n")) {
    if ($line) {
      $null = $builder.AppendLine("  $line")
    }
  }
}
if ($manifest) {
  if ($manifest.session_id) {
    $null = $builder.AppendLine("- session_id: $($manifest.session_id)")
  }
  if ($manifest.guidance.context_surface) {
    $null = $builder.AppendLine("- context_asmdef_status: $($manifest.guidance.context_surface.asmdef_status)")
    $null = $builder.AppendLine("- context_giant_file_count: $($manifest.guidance.context_surface.giant_file_count)")
    $null = $builder.AppendLine("- context_largest_file_kb: $($manifest.guidance.context_surface.largest_file_kb)")
    $null = $builder.AppendLine("- context_recommendation: $($manifest.guidance.context_surface.recommendation)")
    if ($manifest.guidance.context_surface.preferred_execution_mode) {
      $null = $builder.AppendLine("- context_preferred_execution_mode: $($manifest.guidance.context_surface.preferred_execution_mode)")
      $null = $builder.AppendLine("- context_execution_mode_reason: $($manifest.guidance.context_surface.execution_mode_reason)")
    }
  }
  if ($manifest.guidance.execution_mode) {
    $null = $builder.AppendLine("- execution_mode: $($manifest.guidance.execution_mode.mode)")
    $null = $builder.AppendLine("- execution_mode_reason: $($manifest.guidance.execution_mode.reason)")
  }
  if ($manifest.guidance.backlog_health) {
    $null = $builder.AppendLine("- backlog_auto_promotion_safe: $($manifest.guidance.backlog_health.auto_promotion_safe)")
    $null = $builder.AppendLine("- backlog_corruption_count: $($manifest.guidance.backlog_health.corrupt_item_count)")
    $null = $builder.AppendLine("- backlog_health_recommendation: $($manifest.guidance.backlog_health.recommendation)")
  }
  if ($manifest.guidance.admission_warnings -and @($manifest.guidance.admission_warnings).Count -gt 0) {
    $null = $builder.AppendLine('- admission_warnings:')
    foreach ($warning in @($manifest.guidance.admission_warnings)) {
      $null = $builder.AppendLine("  - $warning")
    }
    $null = $builder.AppendLine('- admission_rule: treat any backlog corruption warning as a scope freeze; verify live files before expanding work.')
  }
  if ($manifest.guidance.learned_policy) {
    $null = $builder.AppendLine("- learned_policy: $($manifest.guidance.learned_policy)")
  }
  if ($manifest.guidance.recommended_read_path) {
    $null = $builder.AppendLine('- recommended_read_path:')
    foreach ($path in $manifest.guidance.recommended_read_path) {
      $null = $builder.AppendLine("  - $path")
    }
  }
  if ($manifest.guidance.verification_expectation) {
    $null = $builder.AppendLine("- verification_expectation: $($manifest.guidance.verification_expectation)")
  }
  if ($manifest.guidance.learned_bucket_stats) {
    $null = $builder.AppendLine('- learned_bucket_stats:')
    if ($manifest.guidance.learned_bucket_stats.sessions -ne $null) {
      $null = $builder.AppendLine("  sessions: $($manifest.guidance.learned_bucket_stats.sessions)")
    }
    if ($manifest.guidance.learned_bucket_stats.converged -ne $null) {
      $null = $builder.AppendLine("  converged: $($manifest.guidance.learned_bucket_stats.converged)")
    }
    if ($manifest.guidance.learned_bucket_stats.stopped -ne $null) {
      $null = $builder.AppendLine("  stopped: $($manifest.guidance.learned_bucket_stats.stopped)")
    }
    if ($manifest.guidance.learned_bucket_stats.avg_input_tokens -ne $null) {
      $null = $builder.AppendLine("  avg_input_tokens: $($manifest.guidance.learned_bucket_stats.avg_input_tokens)")
    }
    if ($manifest.guidance.learned_bucket_stats.avg_turns -ne $null) {
      $null = $builder.AppendLine("  avg_turns: $($manifest.guidance.learned_bucket_stats.avg_turns)")
    }
  }
}
$null = $builder.AppendLine('- requirement: keep the next peer handoff focused on one repo slice and one verification plan.')

[System.IO.File]::WriteAllText($OutputPath, $builder.ToString(), [System.Text.Encoding]::UTF8)
Write-Host "Wrote prompt: $OutputPath"
