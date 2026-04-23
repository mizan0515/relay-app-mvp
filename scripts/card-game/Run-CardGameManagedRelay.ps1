param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$TaskSlug = '',
  [int]$Turns = 2,
  [int]$TimeoutSeconds = 1800,
  [switch]$ForceRelay,
  [switch]$SkipBuild,
  [switch]$AutoCompleteTerminal
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
$profileRoot = Join-Path $repoRoot 'profiles\card-game'
$manifestPath = Join-Path $profileRoot 'generated-admission.json'
$promptPath = Join-Path $profileRoot 'generated-session-prompt.md'
$loopStatusPath = Join-Path $profileRoot 'generated-loop-status.json'

$startArgs = @(
  '-ExecutionPolicy', 'Bypass',
  '-File', (Join-Path $scriptRoot 'Start-CardGameRelay.ps1'),
  '-CardGameRoot', $CardGameRoot,
  '-PrepareOnly'
)

if ($TaskSlug) {
  $startArgs += @('-TaskSlug', $TaskSlug)
}

if ($ForceRelay) {
  $startArgs += '-ForceRelay'
}

if ($SkipBuild) {
  $startArgs += '-SkipBuild'
}

& powershell @startArgs

if (-not (Test-Path -LiteralPath $manifestPath)) {
  throw "Manifest not found after preparation: $manifestPath"
}

if (-not (Test-Path -LiteralPath $promptPath)) {
  throw "Prompt not found after preparation: $promptPath"
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath -Encoding UTF8 | ConvertFrom-Json
$sessionId = [string]$manifest.session_id
$prompt = Get-Content -Raw -LiteralPath $promptPath -Encoding UTF8

if (-not $sessionId) {
  throw 'Prepared manifest does not contain a session id.'
}

Write-Host "Prepared relay session: $sessionId"
if (Test-Path -LiteralPath $loopStatusPath) {
  Write-Host "Loop status: $loopStatusPath"
}

& powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'scripts\gui-smoke\run-gui-worksession.ps1') `
  -WorkingDir $CardGameRoot `
  -SessionId $sessionId `
  -InitialPrompt $prompt `
  -Turns $Turns `
  -TimeoutSeconds $TimeoutSeconds

$signalJson = & powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Get-CardGameRelaySignal.ps1') -CardGameRoot $CardGameRoot
$signal = $signalJson | ConvertFrom-Json
$managerSignalJson = & powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Get-CardGameManagerSignal.ps1') -CardGameRoot $CardGameRoot
$managerSignal = $managerSignalJson | ConvertFrom-Json

Write-Host ''
Write-Host '=== Managed Relay Signal ==='
Write-Host $signal.signal_marker
Write-Host $signal.done_marker
Write-Host $managerSignal.manager_signal_marker
Write-Host $managerSignal.manager_done_marker

if ($AutoCompleteTerminal -and ($signal.is_terminal -or @('Paused','Converged','Stopped','Failed') -contains [string]$signal.status)) {
  try {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Complete-CardGameRelaySession.ps1') `
      -CardGameRoot $CardGameRoot `
      -ManifestPath $manifestPath
  } catch {
    Write-Warning ("Managed terminal completion skipped: " + $_.Exception.Message)
  }
}
