#!/usr/bin/env pwsh
# tools/run-smoke.ps1 — B8 thin wrapper over xunit E2E harness.
# Runs every class matching *E2ETests under CodexClaudeRelay.Core.Tests/Broker/.
# Rationale: .autopilot/ADR-TOOLS-SMOKE-NEED.md Option A.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet test (Join-Path $root 'CodexClaudeRelay.sln') --nologo --filter 'FullyQualifiedName~E2ETests'
exit $LASTEXITCODE
