#!/usr/bin/env pwsh
# tools/Validate-Upstream-Contract.ps1 — invoke the upstream
# dad-v2-system-template validator against this relay's live Document/dialogue
# tree. Surfaces drift where the relay's own slim per-packet validator
# (tools/Validate-Dad-Packet.ps1, 242 lines) accepts packets the upstream
# template validator (en/tools/Validate-DadPacket.ps1, 780 lines) would
# reject — specifically session-level invariants like Test-RequiresDisconfirmation,
# Validate-StateObject, Get-SessionPackets.
#
# See:
#   D:\dad-v2-system-template\TEMPLATE-INTERACTION.md (Real-Usage Lessons)
#   .autopilot/PITFALLS.md entry 2026-04-24 (validator coverage gap)
#
# Soft tripwire: exits 0 even when the upstream template is missing or its
# validator reports issues — prints a diagnosis for the operator. Hard-fail
# semantics are deferred until the port of session-level checks lands or
# until the operator opts in via -Strict.

[CmdletBinding()]
param(
    [string]$TemplateRoot = 'D:\dad-v2-system-template',
    [ValidateSet('en', 'ko')]
    [string]$Variant = 'en',
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$validator = Join-Path $TemplateRoot "$Variant\tools\Validate-DadPacket.ps1"
if (-not (Test-Path $validator)) {
    Write-Host "[validate-upstream] upstream validator not found at $validator"
    Write-Host "[validate-upstream] skipping tripwire (this is not a failure)."
    exit 0
}

$dialogueRoot = Join-Path $repoRoot 'Document\dialogue'
if (-not (Test-Path $dialogueRoot)) {
    Write-Host "[validate-upstream] no Document\dialogue tree; nothing to validate."
    exit 0
}

Write-Host "[validate-upstream] running $validator -Root $repoRoot -AllSessions"
& pwsh -NoProfile -ExecutionPolicy Bypass -File $validator -Root $repoRoot -AllSessions
$rc = $LASTEXITCODE

if ($rc -ne 0) {
    Write-Warning "[validate-upstream] upstream validator reported issues (exit=$rc)"
    Write-Warning "[validate-upstream] see .autopilot/PITFALLS.md 2026-04-24 for context"
    if ($Strict) { exit $rc }
}

exit 0
