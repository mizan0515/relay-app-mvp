param(
  [string]$TaskSlug = '',
  [string]$TaskSummary = '',
  [string]$ManifestPath = '',
  [switch]$SkipBuild,
  [switch]$SkipAutoWriteBack,
  [switch]$PrepareOnly,
  [switch]$ForceRelay
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
$appDataDir = Join-Path $env:LOCALAPPDATA 'CodexClaudeRelayMvp'
$uiSettingsPath = Join-Path $appDataDir 'ui-settings.json'

powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Install-CardGameProfile.ps1') -Force

$promptPath = Join-Path $repoRoot 'profiles\card-game\generated-session-prompt.md'
$resolvedManifestPath = if ($ManifestPath) { $ManifestPath } else { Join-Path $repoRoot 'profiles\card-game\generated-admission.json' }
$executionRouteJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-execution-route.json'
$executionRouteMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-execution-route.md'
$directPromptPath = Join-Path $repoRoot 'profiles\card-game\generated-direct-codex-prompt.md'
$loopStatusJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-loop-status.json'
$loopStatusMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-loop-status.md'
$runbookJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-runbook.json'
$runbookMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-runbook.md'
$opsDashboardJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-ops-dashboard.json'
$opsDashboardMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-ops-dashboard.md'
$routeLearningPath = Join-Path $repoRoot 'docs\card-game-integration\learning-memory\route-outcomes.jsonl'
$heuristicsJsonPath = Join-Path $repoRoot 'docs\card-game-integration\learning-memory\heuristics.json'
$heuristicsMarkdownPath = Join-Path $repoRoot 'docs\card-game-integration\learning-memory\heuristics.md'
$contextSurfaceJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-context-surface.json'

$sessionArgs = @(
  '-ExecutionPolicy', 'Bypass',
  '-File', (Join-Path $scriptRoot 'New-CardGameSession.ps1'),
  '-TaskSlug', $TaskSlug,
  '-ManifestPath', $resolvedManifestPath,
  '-PromptPath', $promptPath
)
if ($TaskSummary -ne '') {
  $sessionArgs += @('-TaskSummary', $TaskSummary)
}
& powershell @sessionArgs

$uiSettings = Get-Content -Raw -LiteralPath $uiSettingsPath | ConvertFrom-Json
$uiSettings.InitialPrompt = Get-Content -Raw -LiteralPath $promptPath
$manifest = Get-Content -Raw -LiteralPath $resolvedManifestPath | ConvertFrom-Json
$executionMode = if ($manifest.guidance.execution_mode.mode) { [string]$manifest.guidance.execution_mode.mode } else { 'relay-dad' }
$uiSettings.SessionId = if ($manifest.session_id) { $manifest.session_id } else { '' }
if ($uiSettings.PSObject.Properties.Name -contains 'AdmissionManifestPath') {
  $uiSettings.AdmissionManifestPath = $resolvedManifestPath
} else {
  $uiSettings | Add-Member -NotePropertyName AdmissionManifestPath -NotePropertyValue $resolvedManifestPath
}
$uiSettings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $uiSettingsPath -Encoding utf8

powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Write-CardGameExecutionRoute.ps1') `
  -ManifestPath $resolvedManifestPath `
  -OutputJsonPath $executionRouteJsonPath `
  -OutputMarkdownPath $executionRouteMarkdownPath

if ($executionMode -ne 'relay-dad') {
  powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Write-CardGameDirectPrompt.ps1') `
    -ManifestPath $resolvedManifestPath `
    -RoutePath $executionRouteJsonPath `
    -OutputPath $directPromptPath
}

powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Get-CardGameLoopStatus.ps1') `
  -CardGameRoot 'D:\Unity\card game' `
  -ManifestPath $resolvedManifestPath `
  -OutputJsonPath $loopStatusJsonPath `
  -OutputMarkdownPath $loopStatusMarkdownPath

powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Write-CardGameRunbook.ps1') `
  -ManifestPath $resolvedManifestPath `
  -LoopStatusPath $loopStatusJsonPath `
  -ExecutionRoutePath $executionRouteJsonPath `
  -DirectPromptPath $directPromptPath `
  -OutputJsonPath $runbookJsonPath `
  -OutputMarkdownPath $runbookMarkdownPath

powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Write-CardGameOpsDashboard.ps1') `
  -CardGameRoot 'D:\Unity\card game' `
  -LoopStatusPath $loopStatusJsonPath `
  -ContextSurfacePath $contextSurfaceJsonPath `
  -HeuristicsPath $heuristicsJsonPath `
  -OutputJsonPath $opsDashboardJsonPath `
  -OutputMarkdownPath $opsDashboardMarkdownPath

powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Get-CardGameLoopStatus.ps1') `
  -CardGameRoot 'D:\Unity\card game' `
  -ManifestPath $resolvedManifestPath `
  -OutputJsonPath $loopStatusJsonPath `
  -OutputMarkdownPath $loopStatusMarkdownPath

if ($PrepareOnly) {
  Write-Host "Prepared relay session only."
  Write-Host "Prompt: $promptPath"
  Write-Host "Manifest: $resolvedManifestPath"
  Write-Host "Execution route: $executionRouteMarkdownPath"
  if (Test-Path -LiteralPath $directPromptPath) {
    Write-Host "Direct prompt: $directPromptPath"
  }
  Write-Host "Runbook: $runbookMarkdownPath"
  Write-Host "Ops dashboard: $opsDashboardMarkdownPath"
  return
}

if (-not $ForceRelay -and $executionMode -ne 'relay-dad') {
  powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Append-CardGameRouteLearningRecord.ps1') `
    -ManifestPath $resolvedManifestPath `
    -RoutePath $executionRouteJsonPath `
    -OutputPath $routeLearningPath
  powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Update-CardGameHeuristics.ps1') `
    -RouteLearningPath $routeLearningPath `
    -OutputJsonPath $heuristicsJsonPath `
    -OutputMarkdownPath $heuristicsMarkdownPath
  powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Write-CardGameRouteHandoff.ps1') `
    -ManifestPath $resolvedManifestPath `
    -CardGameRoot 'D:\Unity\card game' `
    -RoutePath $executionRouteJsonPath `
    -RunbookPath $runbookJsonPath `
    -DirectPromptPath $directPromptPath
  powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Write-CardGameOpsDashboard.ps1') `
    -CardGameRoot 'D:\Unity\card game' `
    -LoopStatusPath $loopStatusJsonPath `
    -ContextSurfacePath $contextSurfaceJsonPath `
    -HeuristicsPath $heuristicsJsonPath `
    -OutputJsonPath $opsDashboardJsonPath `
    -OutputMarkdownPath $opsDashboardMarkdownPath
  Write-Host "Prepared session but skipped desktop relay because execution mode is $executionMode."
  Write-Host "Prompt: $promptPath"
  Write-Host "Manifest: $resolvedManifestPath"
  Write-Host "Execution route: $executionRouteMarkdownPath"
  if (Test-Path -LiteralPath $directPromptPath) {
    Write-Host "Direct prompt: $directPromptPath"
  }
  Write-Host "Runbook: $runbookMarkdownPath"
  Write-Host "Ops dashboard: $opsDashboardMarkdownPath"
  return
}

if (-not $SkipBuild) {
  dotnet build (Join-Path $repoRoot 'CodexClaudeRelay.Desktop\CodexClaudeRelay.Desktop.csproj')
}

dotnet run --project (Join-Path $repoRoot 'CodexClaudeRelay.Desktop\CodexClaudeRelay.Desktop.csproj')

if (-not $SkipAutoWriteBack) {
  try {
    powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Complete-CardGameRelaySession.ps1') `
      -CardGameRoot 'D:\Unity\card game' `
      -ManifestPath $resolvedManifestPath
  } catch {
    Write-Warning ("Relay completion skipped: " + $_.Exception.Message)
    Write-Host "If the session is terminal, run:"
    Write-Host "powershell -ExecutionPolicy Bypass -File `"$((Join-Path $scriptRoot 'Complete-CardGameRelaySession.ps1'))`" -ManifestPath `"$resolvedManifestPath`""
  }
}
