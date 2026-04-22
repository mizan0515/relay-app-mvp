#!/usr/bin/env pwsh
# examples/live-roundtrip/run-live.ps1
# Live round-trip driver: builds and runs the LiveRoundtrip console app to
# emit fresh turn-1.yaml / turn-2.yaml into ./session-out/, then reports
# schema alignment with D:\dad-v2-system-template's PACKET-SCHEMA.md.

param(
    [string]$OutDir,
    [string]$TemplateRoot = $(if ($env:DAD_TEMPLATE_ROOT) { $env:DAD_TEMPLATE_ROOT } else { 'D:\dad-v2-system-template' }),
    [string]$TemplateVariant = $(if ($env:DAD_TEMPLATE_VARIANT) { $env:DAD_TEMPLATE_VARIANT } else { 'en' })
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$project = Join-Path $here 'LiveRoundtrip\LiveRoundtrip.csproj'
if (-not $OutDir) { $OutDir = Join-Path $here 'session-out' }

Write-Host '=== 1. Build + run live driver ===' -ForegroundColor Cyan
dotnet run --project $project -- $OutDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] driver exited $LASTEXITCODE" -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host '=== 2. Schema alignment against template spec ===' -ForegroundColor Cyan
$schemaPath = Join-Path $TemplateRoot "$TemplateVariant\Document\DAD\PACKET-SCHEMA.md"
if (-not (Test-Path $schemaPath)) {
    Write-Host "[SKIP] template schema not at $schemaPath" -ForegroundColor Yellow
} else {
    $schema = Get-Content -Raw $schemaPath
    $turn1  = Get-Content -Raw (Join-Path $OutDir 'turn-1.yaml')
    $fields = @('type', 'from', 'turn', 'session_id', 'handoff', 'peer_review')
    $ok = $true
    foreach ($f in $fields) {
        $inSpec  = $schema -match "(?m)^${f}:"
        $inLive  = $turn1  -match "(?m)^${f}:"
        $mark = if ($inSpec -and $inLive) { '[OK]' } else { $ok = $false; '[X]' }
        Write-Host ("  {0} {1}: spec={2} live={3}" -f $mark, $f, $inSpec, $inLive)
    }
    if (-not $ok) { Write-Host "[FAIL] field alignment gap"; exit 1 }
}

Write-Host ''
Write-Host '=== 3. Relay self-validator on live turn-1 ===' -ForegroundColor Cyan
$repoRoot = Resolve-Path (Join-Path $here '..\..')
& (Join-Path $repoRoot 'tools\Validate-Dad-Packet.ps1') -Path (Join-Path $OutDir 'turn-1.yaml')
if ($LASTEXITCODE -ne 0) { Write-Host "[FAIL] relay validator exit $LASTEXITCODE"; exit 1 }

Write-Host ''
Write-Host "=== Done. Fresh session written to $OutDir ===" -ForegroundColor Green
Get-ChildItem $OutDir | ForEach-Object { Write-Host ("  {0}  {1} B" -f $_.Name, $_.Length) }
exit 0
