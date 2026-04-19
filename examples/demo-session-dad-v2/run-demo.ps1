# examples/demo-session-dad-v2/run-demo.ps1
# 5-minute smoke check for the DAD-v2 peer-symmetric bridge.
# See README.md in this folder for the Korean operator guide.

param(
    [switch]$SkipBuild,
    [switch]$VerboseLogs
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot

$sessionDir = Join-Path $repoRoot 'Document\dialogue\sessions\demo-20260419'
$results = @()

function Add-Result {
    param($Step, $Ok, $Detail)
    $script:results += [pscustomobject]@{ Step = $Step; Ok = $Ok; Detail = $Detail }
    if ($Ok) {
        Write-Host ("  [PASS] {0,-22} {1}" -f $Step, $Detail) -ForegroundColor Green
    } else {
        Write-Host ("  [FAIL] {0,-22} {1}" -f $Step, $Detail) -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== DAD-v2 bridge smoke demo ===" -ForegroundColor Cyan
Write-Host "repo:    $repoRoot"
Write-Host "session: $sessionDir"
Write-Host ""

# Step 1: build
Write-Host "[1/5] dotnet build"
if ($SkipBuild) {
    Add-Result 'build' $true 'skipped (--SkipBuild)'
} else {
    $buildLog = dotnet build CodexClaudeRelay.sln --nologo 2>&1
    $exit = $LASTEXITCODE
    if ($exit -eq 0) {
        Add-Result 'build' $true '0 warn / 0 err'
    } else {
        Add-Result 'build' $false "exit=$exit"
        if ($VerboseLogs) { $buildLog | Select-Object -Last 20 }
    }
}

# Step 2: PacketIO round-trip (cp-1)
Write-Host "[2/5] PacketIOTests (cp-1 evidence)"
$testArgs = @('test', 'CodexClaudeRelay.sln', '--nologo',
    '--filter', 'FullyQualifiedName~PacketIOTests')
if ($SkipBuild) { $testArgs += '--no-build' }
$testLog = & dotnet @testArgs 2>&1
$exit = $LASTEXITCODE
if ($exit -eq 0) {
    $passLine = ($testLog | Select-String -Pattern 'Passed|Failed').Line | Select-Object -Last 1
    if (-not $passLine) { $passLine = 'tests green' }
    Add-Result 'packet-io-tests' $true ($passLine -replace '\s+', ' ').Trim()
} else {
    Add-Result 'packet-io-tests' $false "exit=$exit"
    if ($VerboseLogs) { $testLog | Select-Object -Last 15 }
}

# Step 3: session artifacts exist (cp-2)
Write-Host "[3/5] session artifacts present (cp-2 evidence)"
$expected = @('turn-1.yaml', 'turn-2.yaml', 'turn-1-handoff.md', 'state.json', 'summary.md')
$missing = @()
foreach ($f in $expected) {
    $full = Join-Path $sessionDir $f
    if (-not (Test-Path -LiteralPath $full)) { $missing += $f }
}
if ($missing.Count -eq 0) {
    Add-Result 'session-artifacts' $true "$($expected.Count) files present"
} else {
    Add-Result 'session-artifacts' $false "missing: $($missing -join ', ')"
}

# Step 4: validator
Write-Host "[4/5] tools/Validate-Dad-Packet.ps1"
$validator = Join-Path $repoRoot 'tools\Validate-Dad-Packet.ps1'
if (-not (Test-Path -LiteralPath $validator)) {
    Add-Result 'validator' $false 'tools/Validate-Dad-Packet.ps1 not found'
} else {
    try {
        & $validator -Path (Join-Path $sessionDir 'turn-1.yaml') | Out-Null
        & $validator -Path (Join-Path $sessionDir 'turn-2.yaml') | Out-Null
        Add-Result 'validator' $true 'turn-1 + turn-2 PASS'
    } catch {
        Add-Result 'validator' $false $_.Exception.Message
    }
}

# Step 5: state.json shape
Write-Host "[5/5] state.json shape"
try {
    $state = Get-Content -LiteralPath (Join-Path $sessionDir 'state.json') -Raw | ConvertFrom-Json
    if ($state.status -eq 'closed' -and $state.turn_count -eq 2) {
        Add-Result 'state.json' $true "status=$($state.status), turns=$($state.turn_count)"
    } else {
        Add-Result 'state.json' $false "expected status=closed/turns=2, got status=$($state.status)/turns=$($state.turn_count)"
    }
} catch {
    Add-Result 'state.json' $false $_.Exception.Message
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$failed = $results | Where-Object { -not $_.Ok }
if ($failed.Count -eq 0) {
    Write-Host "ALL PASS. relay is a working peer-symmetric bridge on current main." -ForegroundColor Green
    exit 0
} else {
    Write-Host "FAIL count: $($failed.Count)" -ForegroundColor Red
    $failed | ForEach-Object { Write-Host ("  - {0}: {1}" -f $_.Step, $_.Detail) -ForegroundColor Red }
    Write-Host ""
    Write-Host "Rerun with -VerboseLogs for full output." -ForegroundColor Yellow
    exit 1
}
