#!/usr/bin/env pwsh
# helpers/Get-HotFiles.ps1 — list churn hotspots: files edited N+ times
# in the last M iters/days, sorted by current file size. Surfaces the
# token-saving seam candidates operators usually hunt for manually —
# see Unity card-game PRs #275-277 ("토큰 절약 seam 정리") for the
# recurring pattern this helper replaces.
#
# See PITFALLS.md 2026-04-24 — token-saving seams — for the motivating
# lesson.
#
# Defaults:
#   -Days 30         — lookback window
#   -MinEdits 3      — files edited >= N times count as hot
#   -Extensions      — limit to matching extensions (default: none,
#                      all files)
#   -Path            — limit to a subtree (default: whole repo)
#   -Top 20          — cap output rows
#
# Emits JSON list { file, edits, size_bytes } sorted by size desc
# (biggest hot files first — those are the best token-saving seam
# candidates).
#
# Exit 0 always (informational); caller decides how to act.

[CmdletBinding()]
param(
    [int]$Days = 30,
    [int]$MinEdits = 3,
    [string[]]$Extensions,
    [string]$Path,
    [int]$Top = 20,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'

$since = "$Days.days"
$pathArg = if ($Path) { @('--', $Path) } else { @() }

$log = & git log --since=$since --pretty=format: --name-only @pathArg 2>$null
if (-not $log) { $log = @() }

$counts = @{}
foreach ($line in @($log)) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $rel = $line.Trim()
    if ($Extensions) {
        $ext = [System.IO.Path]::GetExtension($rel).TrimStart('.').ToLowerInvariant()
        if ($Extensions -notcontains $ext) { continue }
    }
    if ($counts.ContainsKey($rel)) { $counts[$rel]++ } else { $counts[$rel] = 1 }
}

$hot = @()
foreach ($kvp in $counts.GetEnumerator()) {
    if ($kvp.Value -lt $MinEdits) { continue }
    $full = Join-Path (Get-Location) $kvp.Key
    $size = 0
    try {
        if (Test-Path -LiteralPath $full -PathType Leaf -ErrorAction SilentlyContinue) {
            $size = (Get-Item -LiteralPath $full -ErrorAction SilentlyContinue).Length
        }
    } catch { $size = 0 }
    if ($size -eq 0) { continue }
    $hot += [pscustomobject]@{
        file = $kvp.Key
        edits = $kvp.Value
        size_bytes = $size
    }
}

$hot = $hot | Sort-Object size_bytes -Descending | Select-Object -First $Top

$payload = [ordered]@{
    probed_at = [DateTime]::UtcNow.ToString('o')
    days = $Days
    min_edits = $MinEdits
    extensions = $Extensions
    path = $Path
    total_candidates = $hot.Count
    hot_files = @($hot)
}

if ($AsJson) { $payload | ConvertTo-Json -Depth 4 -Compress } else { $payload | ConvertTo-Json -Depth 4 }

exit 0
