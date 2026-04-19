#!/usr/bin/env pwsh
# examples/template-integration/run-experiment.ps1
# Experimental driver: runs this relay against the read-only DAD-v2 template
# reference repo and reports compatibility findings honestly.
#
# Default template root: D:\dad-v2-system-template  (override with -TemplateRoot).
# Does NOT write into the template repo — it's treated as read-only reference.

param(
    [string]$TemplateRoot = 'D:\dad-v2-system-template',
    [string]$TemplateVariant = 'en'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $repoRoot

$results = [ordered]@{}

function Step($name, [scriptblock]$body) {
    Write-Host ''
    Write-Host "=== $name ===" -ForegroundColor Cyan
    try {
        & $body
        $results[$name] = 'PASS'
        Write-Host "[PASS] $name" -ForegroundColor Green
    } catch {
        $results[$name] = "FAIL: $($_.Exception.Message)"
        Write-Host "[FAIL] $name -> $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

Step '1. Template repo accessible' {
    if (-not (Test-Path $TemplateRoot)) { throw "missing: $TemplateRoot" }
    $schema = Join-Path $TemplateRoot "$TemplateVariant\Document\DAD\PACKET-SCHEMA.md"
    if (-not (Test-Path $schema)) { throw "missing spec: $schema" }
    Write-Host ("Spec found: {0}" -f $schema)
}

Step '2. Relay E2E smoke (tools/run-smoke.ps1)' {
    & (Join-Path $repoRoot 'tools\run-smoke.ps1')
    if ($LASTEXITCODE -ne 0) { throw "smoke exit $LASTEXITCODE" }
}

Step '3. Relay validator on demo session (in-repo)' {
    & (Join-Path $repoRoot 'tools\Validate-Dad-Packet.ps1') `
        -Path (Join-Path $repoRoot 'Document\dialogue\sessions\demo-20260419\turn-1.yaml')
    if ($LASTEXITCODE -ne 0) { throw "relay validator exit $LASTEXITCODE" }
}

Step '4. Schema field cross-check (relay packet vs template spec)' {
    $schemaText = Get-Content -Raw (Join-Path $TemplateRoot "$TemplateVariant\Document\DAD\PACKET-SCHEMA.md")
    $relayTurn = Get-Content -Raw (Join-Path $repoRoot 'Document\dialogue\sessions\demo-20260419\turn-1.yaml')
    $topFields = @('type', 'from', 'turn', 'session_id', 'contract', 'peer_review', 'my_work', 'handoff')
    $missing = @()
    foreach ($f in $topFields) {
        $inSpec  = $schemaText -match "(?m)^${f}:"
        $inRelay = $relayTurn  -match "(?m)^${f}:"
        if (-not ($inSpec -and $inRelay)) { $missing += "$f (spec=$inSpec relay=$inRelay)" }
    }
    if ($missing.Count -gt 0) { throw ("fields not matched: " + ($missing -join ', ')) }
    Write-Host ("All {0} top-level fields present in both spec and relay packet." -f $topFields.Count)
}

Step '5. Template validator compatibility probe (known gap: turn-NN filename)' {
    $templateValidator = Join-Path $TemplateRoot "$TemplateVariant\tools\Validate-DadPacket.ps1"
    if (-not (Test-Path $templateValidator)) { throw "missing: $templateValidator" }
    Write-Host 'Note: template validator expects turn-01.yaml (two-digit).'
    Write-Host '      Relay emits turn-1.yaml (one-digit). This is a known format gap.'
    Write-Host '      Probe result is informational — no writes to template repo.'
}

Write-Host ''
Write-Host '=== Summary ===' -ForegroundColor Cyan
$results.GetEnumerator() | ForEach-Object { Write-Host ("  {0}: {1}" -f $_.Key, $_.Value) }

$failed = @($results.Values | Where-Object { $_ -ne 'PASS' }).Count
if ($failed -gt 0) { Write-Host "`n$failed step(s) did not pass." -ForegroundColor Yellow; exit 1 }
Write-Host "`nAll steps passed." -ForegroundColor Green
exit 0
