# .autopilot/project.ps1 — relay-app-mvp autopilot wrapper.
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
    if (-not (Test-Path 'RelayApp.sln')) { Write-Error 'RelayApp.sln missing at repo root'; exit 1 }
    foreach ($csproj in 'RelayApp.Core','RelayApp.Desktop','RelayApp.CodexProtocol','RelayApp.CodexProtocol.Spike') {
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
    Write-Host 'project.ps1 test: dotnet build RelayApp.sln -c Release'
    dotnet build 'RelayApp.sln' -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }

  'audit' {
    Write-Host '=== dotnet outdated (RelayApp.sln) ==='
    dotnet list 'RelayApp.sln' package --outdated 2>$null
    Write-Host ''
    Write-Host '=== .cs file counts ==='
    foreach ($p in 'RelayApp.Core','RelayApp.Desktop','RelayApp.CodexProtocol','RelayApp.CodexProtocol.Spike') {
      $n = (Get-ChildItem $p -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }).Count
      Write-Host "  $p : $n"
    }
    Write-Host ''
    Write-Host '=== Churn hotspots (top 10, last 30 days) ==='
    git log --since="30.days" --pretty=format: --name-only -- 'RelayApp.Core' 'RelayApp.Desktop' 'RelayApp.CodexProtocol' 'RelayApp.CodexProtocol.Spike' 2>$null | `
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
      Write-Host 'relay-app-mvp autopilot hooks installed and smoke-tested.'
    }
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
project.ps1 — relay-app-mvp autopilot wrapper

Verbs:
  doctor          Fast env check: git/gh/dotnet/powershell + .sln + csprojs + hooks. Exit 0 = OK.
  test            dotnet build RelayApp.sln -c Release.
  audit           Outdated packages + churn hotspots + .cs counts.
  install-hooks   Sets core.hooksPath=.githooks; verifies + smoke-tests.
  start           Print path to RUN.txt for pasting into Claude Code.
  stop            Create .autopilot/HALT (polite stop).
  resume          Remove .autopilot/HALT.
"@
  }
}
