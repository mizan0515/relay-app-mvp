param(
  [string]$TaskSlug = '',
  [string]$TaskSummary = '',
  [string]$ManifestPath = '',
  [string]$PromptPath = '',
  [string]$SessionPlanPath = '',
  [switch]$SkipHeuristics
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
$profileRoot = Join-Path $repoRoot 'profiles\card-game'
$learningPath = 'D:\Unity\card game\Document\dialogue\learning-memory\session-outcomes.jsonl'
$heuristicsJsonPath = Join-Path $repoRoot 'docs\card-game-integration\learning-memory\heuristics.json'
$heuristicsMarkdownPath = Join-Path $repoRoot 'docs\card-game-integration\learning-memory\heuristics.md'
$backlogHealthJsonPath = Join-Path $profileRoot 'generated-backlog-health.json'
$backlogHealthMarkdownPath = Join-Path $profileRoot 'generated-backlog-health.md'
$contextSurfaceJsonPath = Join-Path $profileRoot 'generated-context-surface.json'
$contextSurfaceMarkdownPath = Join-Path $profileRoot 'generated-context-surface.md'
$claudeBackendJsonPath = Join-Path $profileRoot 'generated-claude-backend.json'
$claudeBackendMarkdownPath = Join-Path $profileRoot 'generated-claude-backend.md'

if (-not $ManifestPath) {
  $ManifestPath = Join-Path $profileRoot 'generated-admission.json'
}

if (-not $PromptPath) {
  $PromptPath = Join-Path $profileRoot 'generated-session-prompt.md'
}

if (-not $SessionPlanPath) {
  $SessionPlanPath = Join-Path $profileRoot 'generated-session-plan.md'
}

if (-not $SkipHeuristics -and (Test-Path -LiteralPath $learningPath)) {
  powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Update-CardGameHeuristics.ps1') `
    -LearningPath $learningPath `
    -OutputJsonPath $heuristicsJsonPath `
    -OutputMarkdownPath $heuristicsMarkdownPath
}

powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Test-CardGameBacklogHealth.ps1') `
  -OutputJsonPath $backlogHealthJsonPath `
  -OutputMarkdownPath $backlogHealthMarkdownPath

powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Test-CardGameContextSurface.ps1') `
  -CardGameRoot 'D:\Unity\card game' `
  -OutputJsonPath $contextSurfaceJsonPath `
  -OutputMarkdownPath $contextSurfaceMarkdownPath

powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Test-ClaudeBackendReadiness.ps1') `
  -OutputJsonPath $claudeBackendJsonPath `
  -OutputMarkdownPath $claudeBackendMarkdownPath

powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'New-CardGameAdmission.ps1') `
  -TaskSlug $TaskSlug `
  -OutputPath $ManifestPath `
  -BacklogHealthPath $backlogHealthJsonPath `
  -ContextSurfacePath $contextSurfaceJsonPath

$promptArgs = @(
  '-ExecutionPolicy', 'Bypass',
  '-File', (Join-Path $scriptRoot 'New-CardGameSessionPrompt.ps1'),
  '-TaskSlug', $TaskSlug,
  '-ManifestPath', $ManifestPath,
  '-OutputPath', $PromptPath
)

if ($TaskSummary -ne '') {
  $promptArgs += @('-TaskSummary', $TaskSummary)
}

& powershell @promptArgs

$manifest = Get-Content -Raw -LiteralPath $ManifestPath -Encoding UTF8 | ConvertFrom-Json
$lines = @()
$lines += '# Card Game Session Plan'
$lines += ''
$lines += "- Generated at: $(Get-Date -Format o)"
$lines += "- Session id: $($manifest.session_id)"
$lines += "- Task slug: $($manifest.task.slug)"
$lines += "- Priority: $($manifest.task.priority)"
$lines += "- Bucket: $($manifest.task.bucket)"
$lines += "- Task summary: $($manifest.task.summary)"
$lines += ''
$lines += '## Guidance'
$lines += ''
$lines += "- Verification expectation: $($manifest.guidance.verification_expectation)"
$lines += "- Token policy: $($manifest.guidance.token_policy)"
if ($manifest.guidance.execution_mode) {
  $lines += "- Execution mode: $($manifest.guidance.execution_mode.mode)"
  $lines += "- Execution mode reason: $($manifest.guidance.execution_mode.reason)"
}
if ($manifest.guidance.learned_policy) {
  $lines += "- Learned policy: $($manifest.guidance.learned_policy)"
}
$backlogHealth = $manifest.guidance.backlog_health
if ($backlogHealth) {
  $lines += "- Backlog auto-promotion safe: $($backlogHealth.auto_promotion_safe)"
  $lines += "- Backlog corruption count: $($backlogHealth.corrupt_item_count)"
  $lines += "- Backlog recommendation: $($backlogHealth.recommendation)"
}
$contextSurface = $manifest.guidance.context_surface
if ($contextSurface) {
  $lines += "- Context asmdef status: $($contextSurface.asmdef_status)"
  $lines += "- Context giant file count: $($contextSurface.giant_file_count)"
  $lines += "- Context largest file KB: $($contextSurface.largest_file_kb)"
  $lines += "- Context recommendation: $($contextSurface.recommendation)"
  if ($contextSurface.preferred_execution_mode) {
    $lines += "- Context preferred execution mode: $($contextSurface.preferred_execution_mode)"
    $lines += "- Context execution mode reason: $($contextSurface.execution_mode_reason)"
  }
}
$claudeBackend = Get-Content -Raw -LiteralPath $claudeBackendJsonPath -Encoding UTF8 | ConvertFrom-Json
if ($claudeBackend) {
  $lines += "- Claude auth precedence: $($claudeBackend.auth.precedence)"
  $lines += "- Claude gateway enabled: $($claudeBackend.routing.using_gateway)"
  $lines += "- Claude thinking mode: $($claudeBackend.thinking.mode)"
  $lines += "- Claude 1M context: $($claudeBackend.context_1m.expectation)"
}
$admissionWarnings = @($manifest.guidance.admission_warnings)
if ($admissionWarnings.Count -gt 0) {
  $lines += ''
  $lines += '## Admission Warnings'
  $lines += ''
  foreach ($warning in $admissionWarnings) {
    $lines += "- $warning"
  }
  $lines += '- Rule: freeze scope expansion until the live source file is re-read locally.'
}
$lines += ''
$lines += '## Read Path'
$lines += ''
foreach ($path in @($manifest.guidance.recommended_read_path)) {
  $lines += "- $path"
}

if ($manifest.guidance.learned_bucket_stats) {
  $lines += ''
  $lines += '## Learned Bucket Stats'
  $lines += ''
  $lines += "- Sessions: $($manifest.guidance.learned_bucket_stats.sessions)"
  $lines += "- Converged: $($manifest.guidance.learned_bucket_stats.converged)"
  $lines += "- Stopped: $($manifest.guidance.learned_bucket_stats.stopped)"
  $lines += "- Avg input tokens: $($manifest.guidance.learned_bucket_stats.avg_input_tokens)"
  $lines += "- Avg output tokens: $($manifest.guidance.learned_bucket_stats.avg_output_tokens)"
  $lines += "- Avg turns: $($manifest.guidance.learned_bucket_stats.avg_turns)"
}

$lines += ''
$lines += '## Artifacts'
$lines += ''
$lines += "- Backlog health: $backlogHealthJsonPath"
$lines += "- Context surface: $contextSurfaceJsonPath"
$lines += "- Claude backend readiness: $claudeBackendJsonPath"
$lines += "- Admission manifest: $ManifestPath"
$lines += "- Session prompt: $PromptPath"

$planDir = Split-Path -Parent $SessionPlanPath
if ($planDir) {
  New-Item -ItemType Directory -Force -Path $planDir | Out-Null
}

$lines -join "`r`n" | Set-Content -LiteralPath $SessionPlanPath -Encoding UTF8

Write-Host "Prepared session manifest: $ManifestPath"
Write-Host "Prepared session prompt: $PromptPath"
Write-Host "Prepared session plan: $SessionPlanPath"
