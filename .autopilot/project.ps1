# .autopilot/project.ps1 — codex-claude-relay autopilot wrapper.
#
# All verbs run from the repo root (the script cd's there automatically).

param([string]$Verb = 'help')

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = (Resolve-Path (Join-Path $scriptDir '..')).Path

Set-Location $repoRoot

switch ($Verb) {
  'doctor' {
    foreach ($cmd in 'git','gh','dotnet','powershell') {
      if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { Write-Error "missing: $cmd"; exit 1 }
    }
    if (-not (Test-Path 'CodexClaudeRelay.sln')) { Write-Error 'CodexClaudeRelay.sln missing at repo root'; exit 1 }
    foreach ($csproj in 'CodexClaudeRelay.Core','CodexClaudeRelay.Desktop','CodexClaudeRelay.CodexProtocol','CodexClaudeRelay.CodexProtocol.Spike') {
      if (-not (Test-Path "$csproj/$csproj.csproj")) { Write-Error "missing $csproj/$csproj.csproj"; exit 1 }
    }

    $remote = git remote get-url origin 2>$null
    if (-not $remote) { Write-Error 'no origin remote'; exit 1 }

    $expected = '.githooks'
    $hp = (git config --get core.hooksPath) 2>$null
    if ($hp -ne $expected) {
      Write-Warning "core.hooksPath is '$hp' (expected '$expected'). Run: .autopilot/project.ps1 install-hooks"
    } elseif (-not (Test-Path "$expected/pre-commit")) {
      Write-Warning "core.hooksPath set, but $expected/pre-commit missing."
    } elseif (-not (Test-Path "$expected/commit-msg")) {
      Write-Warning "core.hooksPath set, but $expected/commit-msg missing (trailer gates inactive)."
    }

    Write-Host "ok (remote $remote)"
  }

  'test' {
    Write-Host 'project.ps1 test: dotnet build CodexClaudeRelay.sln -c Release'
    dotnet build 'CodexClaudeRelay.sln' -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }

  'audit' {
    Write-Host '=== dotnet outdated (CodexClaudeRelay.sln) ==='
    dotnet list 'CodexClaudeRelay.sln' package --outdated 2>$null
    Write-Host ''
    Write-Host '=== .cs file counts ==='
    foreach ($p in 'CodexClaudeRelay.Core','CodexClaudeRelay.Desktop','CodexClaudeRelay.CodexProtocol','CodexClaudeRelay.CodexProtocol.Spike') {
      $n = (Get-ChildItem $p -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }).Count
      Write-Host "  $p : $n"
    }
    Write-Host ''
    Write-Host '=== Churn hotspots (top 10, last 30 days) ==='
    git log --since="30.days" --pretty=format: --name-only -- 'CodexClaudeRelay.Core' 'CodexClaudeRelay.Desktop' 'CodexClaudeRelay.CodexProtocol' 'CodexClaudeRelay.CodexProtocol.Spike' 2>$null | `
      Where-Object { $_ -and $_ -match '\.cs$' } | Group-Object | `
      Sort-Object Count -Descending | Select-Object -First 10 | `
      ForEach-Object { "  $($_.Count)  $($_.Name)" }
  }

  'install-hooks' {
    $target = '.githooks'
    $current = (git config --get core.hooksPath) 2>$null
    if ($current -eq $target) {
      Write-Host "core.hooksPath already set to $target"
    } else {
      git config core.hooksPath $target
      Write-Host "core.hooksPath set to $target (was: $(if ($current) { $current } else { '<unset>' }))"
    }
    foreach ($hook in 'pre-commit','commit-msg','protect.sh','commit-msg-protect.sh') {
      if (-not (Test-Path "$target/$hook")) {
        Write-Warning "$target/$hook missing."
        exit 1
      }
    }
    & bash "$target/pre-commit"
    if ($LASTEXITCODE -ne 0) {
      Write-Warning "pre-commit smoke test returned $LASTEXITCODE (expected 0 when nothing is staged)"
    } else {
      Write-Host 'codex-claude-relay autopilot hooks installed and smoke-tested.'
    }
  }

  'check-reschedule' {
    # Detects the iter-0 failure mode (summary said "rescheduled" but ScheduleWakeup
    # never fired). See [IMMUTABLE:wake-reschedule] in .autopilot/PROMPT.md.
    $last    = '.autopilot/LAST_RESCHEDULE'
    $halt    = '.autopilot/LAST_HALT_NOTE'
    $metrics = '.autopilot/METRICS.jsonl'
    if (-not (Test-Path $metrics)) { Write-Host 'no METRICS yet — nothing to verify'; exit 0 }
    $metricsMtime = (Get-Item $metrics).LastWriteTimeUtc
    $candidates = @()
    if (Test-Path $last) { $candidates += (Get-Item $last).LastWriteTimeUtc }
    if (Test-Path $halt) { $candidates += (Get-Item $halt).LastWriteTimeUtc }
    if (-not $candidates) {
      Write-Warning "no LAST_RESCHEDULE or LAST_HALT_NOTE sentinel — previous iter likely skipped ScheduleWakeup (summary-only halt)"
      exit 2
    }
    $newest = ($candidates | Sort-Object -Descending | Select-Object -First 1)
    if ($newest -lt $metricsMtime) {
      Write-Warning "sentinel older than METRICS ($newest UTC < $metricsMtime UTC) — reschedule-miss suspected"
      exit 2
    }
    Write-Host "ok (newest sentinel: $newest UTC, METRICS tail: $metricsMtime UTC)"
  }

  'start' {
    Write-Host 'Paste the contents of .autopilot/RUN.txt into Claude Code.'
    Write-Host 'Absolute path:'
    Write-Host "  $repoRoot\.autopilot\RUN.txt"
  }

  'stop' {
    New-Item -ItemType File -Path '.autopilot/HALT' -Force | Out-Null
    Write-Host 'HALT file created at .autopilot/HALT. Loop will exit at next boot.'
  }

  'resume' {
    Remove-Item '.autopilot/HALT' -ErrorAction SilentlyContinue
    Write-Host 'HALT removed.'
  }

  default {
    @"
project.ps1 — codex-claude-relay autopilot wrapper

Verbs:
  doctor          Fast env check: git/gh/dotnet/powershell + .sln + csprojs + hooks. Exit 0 = OK.
  test            dotnet build CodexClaudeRelay.sln -c Release.
  audit           Outdated packages + churn hotspots + .cs counts.
  install-hooks   Sets core.hooksPath=.githooks; verifies + smoke-tests.
  start           Print path to RUN.txt for pasting into Claude Code.
  stop            Create .autopilot/HALT (polite stop).
  resume          Remove .autopilot/HALT.
  check-reschedule  Verify previous iter actually called ScheduleWakeup (sentinel vs METRICS).
"@
  }
}
