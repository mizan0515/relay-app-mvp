#!/usr/bin/env pwsh
# examples/deep-integration/run-deep.ps1
# Deep integration driver: runs a 4-turn scripted session through the real
# RelayBroker and reports all artifacts + event log tail + field-level
# alignment with D:\dad-v2-system-template's PACKET-SCHEMA.md.

param(
    [string]$WorkDir,
    [string]$TemplateRoot = 'D:\dad-v2-system-template',
    [string]$TemplateVariant = 'en'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$project = Join-Path $here 'DeepIntegration\DeepIntegration.csproj'
$repoRoot = Resolve-Path (Join-Path $here '..\..')
if (-not $WorkDir) { $WorkDir = Join-Path $here 'session-workspace' }

Write-Host '=== 1. Build + run scripted 4-turn session ===' -ForegroundColor Cyan
dotnet run --project $project -- $WorkDir
if ($LASTEXITCODE -ne 0) { Write-Host "[FAIL] driver exit $LASTEXITCODE" -ForegroundColor Yellow; exit 1 }

# Find the most recently written session.
$sessionsDir = Join-Path $WorkDir 'Document\dialogue\sessions'
$session = Get-ChildItem $sessionsDir -Directory | Sort-Object LastWriteTime -Desc | Select-Object -First 1
if (-not $session) { Write-Host "[FAIL] no session under $sessionsDir"; exit 1 }
$sessionDir = $session.FullName

Write-Host ''
Write-Host '=== 2. Relay self-validator on every turn packet ===' -ForegroundColor Cyan
$yamls = Get-ChildItem $sessionDir -Filter 'turn-*.yaml' | Sort-Object Name
foreach ($y in $yamls) {
    & (Join-Path $repoRoot 'tools\Validate-Dad-Packet.ps1') -Path $y.FullName
    if ($LASTEXITCODE -ne 0) { Write-Host "[FAIL] validator on $($y.Name)"; exit 1 }
}

Write-Host ''
Write-Host '=== 3. Template spec field alignment (first turn packet) ===' -ForegroundColor Cyan
$schema = Join-Path $TemplateRoot "$TemplateVariant\Document\DAD\PACKET-SCHEMA.md"
if (Test-Path $schema) {
    $schemaText = Get-Content -Raw $schema
    $turn1 = Get-Content -Raw ($yamls[0].FullName)
    $fields = @('type', 'from', 'turn', 'session_id', 'handoff', 'peer_review')
    $ok = $true
    foreach ($f in $fields) {
        $inSpec  = $schemaText -match "(?m)^${f}:"
        $inTurn  = $turn1      -match "(?m)^${f}:"
        $mark = if ($inSpec -and $inTurn) { '[OK]' } else { $ok = $false; '[X]' }
        Write-Host ("  {0} {1}: spec={2} turn={3}" -f $mark, $f, $inSpec, $inTurn)
    }
    if (-not $ok) { Write-Host "[FAIL] alignment gap"; exit 1 }
} else {
    Write-Host "[SKIP] template schema not found at $schema"
}

Write-Host ''
Write-Host "=== Done. Session under: $sessionDir ===" -ForegroundColor Green
Get-ChildItem $sessionDir | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0}  {1} B" -f $_.Name, $_.Length)
}

$logFile = Join-Path $WorkDir "Document\dialogue\logs\$($session.Name).jsonl"
if (Test-Path $logFile) {
    $lineCount = (Get-Content $logFile | Measure-Object -Line).Lines
    Write-Host ''
    Write-Host ("Event log: {0} ({1} events)" -f $logFile, $lineCount)
}
exit 0
