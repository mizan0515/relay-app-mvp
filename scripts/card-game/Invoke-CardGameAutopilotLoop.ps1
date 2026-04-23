param(
  [string]$CardGameRoot = 'D:\Unity\card game',
  [string]$TaskSlug = '',
  [int]$MaxSessions = 1,
  [switch]$SkipBuild,
  [switch]$SkipAutoWriteBack,
  [switch]$PrepareOnly,
  [switch]$AllowCorruptTopItem,
  [switch]$ForceRelay
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
$autopilotRoot = Join-Path $CardGameRoot '.autopilot'
$haltPath = Join-Path $autopilotRoot 'HALT'
$operatorDecisionsPath = Join-Path $autopilotRoot 'OPERATOR-DECISIONS.md'
$dialogueDecisionsPath = Join-Path $CardGameRoot 'Document\dialogue\DECISIONS.md'
$backlogHealthJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-backlog-health.json'
$backlogHealthMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-backlog-health.md'
$manifestPath = Join-Path $repoRoot 'profiles\card-game\generated-admission.json'
$loopReportPath = Join-Path $repoRoot 'profiles\card-game\generated-loop-report.md'
$loopStatusJsonPath = Join-Path $repoRoot 'profiles\card-game\generated-loop-status.json'
$loopStatusMarkdownPath = Join-Path $repoRoot 'profiles\card-game\generated-loop-status.md'

function Get-DecisionValue {
  param(
    [string]$Path,
    [string]$DecisionName
  )

  if (-not (Test-Path -LiteralPath $Path)) {
    return ''
  }

  $lines = Get-Content -LiteralPath $Path -Encoding UTF8
  $escaped = [regex]::Escape($DecisionName)
  foreach ($line in $lines) {
    if ($line -match "DECISION:\s*$escaped\s+(.+)$") {
      return $Matches[1].Trim().Trim('`')
    }
  }

  return ''
}

function Read-JsonFile {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path)) {
    return $null
  }

  if (-not (Test-Path -LiteralPath $Path)) {
    return $null
  }

  return Get-Content -Raw -LiteralPath $Path -Encoding UTF8 | ConvertFrom-Json
}

function Refresh-LoopArtifacts {
  powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Test-CardGameBacklogHealth.ps1') `
    -BacklogPath (Join-Path $autopilotRoot 'BACKLOG.md') `
    -OutputJsonPath $backlogHealthJsonPath `
    -OutputMarkdownPath $backlogHealthMarkdownPath

  powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Get-CardGameLoopStatus.ps1') `
    -CardGameRoot $CardGameRoot `
    -ManifestPath $manifestPath `
    -OutputJsonPath $loopStatusJsonPath `
    -OutputMarkdownPath $loopStatusMarkdownPath

  powershell -ExecutionPolicy Bypass -File (Join-Path $scriptRoot 'Get-CardGameManagerSignal.ps1') `
    -CardGameRoot $CardGameRoot | Out-Null
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add('# Card Game Relay Loop Report')
$reportLines.Add('')
$reportLines.Add('Generated at: ' + (Get-Date).ToString('o'))
$reportLines.Add('Card game root: `' + $CardGameRoot + '`')
$reportLines.Add('Max sessions: ' + $MaxSessions)
$reportLines.Add('Prepare only: ' + $PrepareOnly)
$reportLines.Add('')

if (Test-Path -LiteralPath $haltPath) {
  $reportLines.Add('HALT detected. Loop did not start.')
  [string]::Join("`r`n", $reportLines) | Set-Content -LiteralPath $loopReportPath -Encoding UTF8
  Write-Warning "HALT is present at $haltPath"
  return
}

$focusDecision = Get-DecisionValue -Path $operatorDecisionsPath -DecisionName 'focus'
$evolutionDecision = Get-DecisionValue -Path $operatorDecisionsPath -DecisionName 'evolution'
$approvalDecision = Get-DecisionValue -Path $dialogueDecisionsPath -DecisionName 'approval'
$nextSessionDecision = Get-DecisionValue -Path $dialogueDecisionsPath -DecisionName 'next-session'

$reportLines.Add('Operator focus: ' + $(if ($focusDecision) { $focusDecision } else { 'none' }))
$reportLines.Add('Evolution decision: ' + $(if ($evolutionDecision) { $evolutionDecision } else { 'pending' }))
$reportLines.Add('DAD approval: ' + $(if ($approvalDecision) { $approvalDecision } else { 'pending' }))
$reportLines.Add('DAD next-session: ' + $(if ($nextSessionDecision) { $nextSessionDecision } else { 'pending' }))
$reportLines.Add('')

Refresh-LoopArtifacts

$backlogHealth = Read-JsonFile -Path $backlogHealthJsonPath
if (-not $backlogHealth) {
  throw "Backlog health report was not generated."
}

$loopStatus = Read-JsonFile -Path $loopStatusJsonPath
if (-not $loopStatus) {
  throw "Loop status report was not generated."
}

$reportLines.Add('Backlog auto-promotion safe: ' + $backlogHealth.auto_promotion_safe)
$reportLines.Add('Backlog corrupt items: ' + $backlogHealth.corrupt_item_count)
$reportLines.Add('Backlog recommendation: ' + $backlogHealth.recommendation)
$reportLines.Add('Resolver next action: ' + $loopStatus.next_action)
$reportLines.Add('Resolver task slug: ' + $loopStatus.resolved_task_slug)
  if ($loopStatus.execution_mode) {
    $reportLines.Add('Resolver execution mode: ' + $loopStatus.execution_mode)
    $reportLines.Add('Resolver execution mode reason: ' + $loopStatus.execution_mode_reason)
    if ($loopStatus.execution_route_path) {
      $reportLines.Add('Resolver execution route: ' + $loopStatus.execution_route_path)
  }
    if ($loopStatus.direct_prompt_path) {
      $reportLines.Add('Resolver direct prompt: ' + $loopStatus.direct_prompt_path)
    }
    if ($loopStatus.runbook_path) {
      $reportLines.Add('Resolver runbook: ' + $loopStatus.runbook_path)
    }
    if ($loopStatus.ops_dashboard_path) {
      $reportLines.Add('Resolver ops dashboard: ' + $loopStatus.ops_dashboard_path)
    }
  }
$reportLines.Add('')

if (-not $TaskSlug -and -not $backlogHealth.auto_promotion_safe -and -not $AllowCorruptTopItem) {
  $reportLines.Add('Loop stopped because the top backlog item is not safe for automatic promotion.')
  [string]::Join("`r`n", $reportLines) | Set-Content -LiteralPath $loopReportPath -Encoding UTF8
  Write-Warning "Automatic admission blocked by backlog health. Re-run with -TaskSlug or repair BACKLOG.md."
  return
}

$iterations = [Math]::Max(1, $MaxSessions)
for ($i = 1; $i -le $iterations; $i++) {
  if (Test-Path -LiteralPath $haltPath) {
    $reportLines.Add("HALT detected before session $i. Loop stopped.")
    break
  }

  if ($loopStatus.next_action -eq 'route') {
    $reportLines.Add("## Route $i")
    $reportLines.Add('')
    $reportLines.Add('- Action: use generated execution route instead of desktop relay.')
    $reportLines.Add('- Task slug: ' + $loopStatus.resolved_task_slug)
    if ($loopStatus.execution_mode) {
      $reportLines.Add('- Execution mode: ' + $loopStatus.execution_mode)
      $reportLines.Add('- Execution mode reason: ' + $loopStatus.execution_mode_reason)
      if ($loopStatus.execution_route_path) {
        $reportLines.Add('- Execution route: ' + $loopStatus.execution_route_path)
      }
      if ($loopStatus.direct_prompt_path) {
        $reportLines.Add('- Direct prompt: ' + $loopStatus.direct_prompt_path)
      }
      if ($loopStatus.runbook_path) {
        $reportLines.Add('- Runbook: ' + $loopStatus.runbook_path)
      }
      if ($loopStatus.ops_dashboard_path) {
        $reportLines.Add('- Ops dashboard: ' + $loopStatus.ops_dashboard_path)
      }
    }
    $reportLines.Add('- Result: route artifact is ready; no relay desktop run needed.')
    $reportLines.Add('')
    break
  }

  if ($loopStatus.next_action -eq 'complete') {
    $reportLines.Add("## Completion $i")
    $reportLines.Add('')
    $reportLines.Add('- Action: integrate terminal relay session into autopilot state.')
    $reportLines.Add('- Session id: ' + $loopStatus.session_id)
    $reportLines.Add('')

    if (-not $PrepareOnly) {
      $completeArgs = @(
        '-ExecutionPolicy', 'Bypass',
        '-File', (Join-Path $scriptRoot 'Complete-CardGameRelaySession.ps1'),
        '-CardGameRoot', $CardGameRoot,
        '-ManifestPath', $manifestPath
      )
      & powershell @completeArgs
      $reportLines.Add('- Result: completion executed.')
    } else {
      $reportLines.Add('- Result: prepare-only mode, completion skipped.')
    }

    Refresh-LoopArtifacts
    $backlogHealth = Read-JsonFile -Path $backlogHealthJsonPath
    $loopStatus = Read-JsonFile -Path $loopStatusJsonPath
    $reportLines.Add('- Resolver next action after completion: ' + $loopStatus.next_action)
    $reportLines.Add('')

    if ($PrepareOnly) {
      break
    }
  }

  $effectiveTaskSlug = $TaskSlug
  if (-not $effectiveTaskSlug -and $loopStatus.resolved_task_slug) {
    $effectiveTaskSlug = [string]$loopStatus.resolved_task_slug
  }
  if (-not $effectiveTaskSlug -and $backlogHealth.top_item) {
    $effectiveTaskSlug = [string]$backlogHealth.top_item.slug
  }

  if (-not $effectiveTaskSlug) {
    $reportLines.Add("No task slug resolved for session $i. Loop stopped.")
    break
  }

  $reportLines.Add("## Session $i")
  $reportLines.Add('')
  $reportLines.Add('- Task slug: ' + $effectiveTaskSlug)
  $reportLines.Add('- Action: start relay session preparation/execution')
  if ($loopStatus.execution_mode) {
    $reportLines.Add('- Execution mode: ' + $loopStatus.execution_mode)
  }
  $reportLines.Add('')

  $startArgs = @(
    '-ExecutionPolicy', 'Bypass',
    '-File', (Join-Path $scriptRoot 'Start-CardGameRelay.ps1'),
    '-TaskSlug', $effectiveTaskSlug
  )

  if ($SkipBuild) {
    $startArgs += '-SkipBuild'
  }

  if ($SkipAutoWriteBack) {
    $startArgs += '-SkipAutoWriteBack'
  }

  if ($PrepareOnly) {
    $startArgs += '-PrepareOnly'
  }

  if ($ForceRelay) {
    $startArgs += '-ForceRelay'
  }

  & powershell @startArgs

  if ($PrepareOnly) {
    $reportLines.Add('- Result: prepared only, desktop run skipped.')
    break
  }

  if (-not $ForceRelay -and $loopStatus.execution_mode -and $loopStatus.execution_mode -ne 'relay-dad') {
    $reportLines.Add('- Result: relay desktop run skipped by execution mode policy.')
  } else {
    $reportLines.Add('- Result: relay execution command completed.')
  }
  Refresh-LoopArtifacts
  $backlogHealth = Read-JsonFile -Path $backlogHealthJsonPath
  $loopStatus = Read-JsonFile -Path $loopStatusJsonPath
  if ($loopStatus) {
    $reportLines.Add('- Resolver next action after execution: ' + $loopStatus.next_action)
    $reportLines.Add('- Resolver task slug after execution: ' + $loopStatus.resolved_task_slug)
    if ($loopStatus.execution_mode) {
      $reportLines.Add('- Resolver execution mode after execution: ' + $loopStatus.execution_mode)
    }
  }
  $reportLines.Add('')
}

[string]::Join("`r`n", $reportLines) | Set-Content -LiteralPath $loopReportPath -Encoding UTF8
Write-Host "Wrote loop report: $loopReportPath"
