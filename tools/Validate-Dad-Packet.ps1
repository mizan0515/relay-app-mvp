#Requires -Version 5.1
<#
.SYNOPSIS
  DAD-v2 turn-packet YAML validator (root source-repo companion).

.DESCRIPTION
  Scraped down from en/tools/Validate-DadPacket.ps1 to cover the root-repo's
  realistic use case: validate a single turn packet file (or a directory of
  them) against the DAD-v2 packet rules that don't require a live session
  workspace. Drops state.json / session summary / prompt-artifact file
  existence checks — those belong inside a deployed en/ or ko/ variant, not
  at the template source-repo root.

  Matches the invariants the C# CodexClaudeRelay.Core.Protocol.PacketIO
  round-trip tests already enforce (required top-level fields, closeout_kind
  enum + conditional requirements, checkpoint status enum, forbidden
  fields).

  Exit 0 on pass (warnings allowed). Exit 1 on any issue.

.PARAMETER Path
  A single turn-*.yaml packet file.

.PARAMETER Directory
  A directory to scan recursively for turn-*.yaml files.

.EXAMPLE
  pwsh tools/Validate-Dad-Packet.ps1 -Path fixtures/turn-01.yaml

.EXAMPLE
  pwsh tools/Validate-Dad-Packet.ps1 -Directory Document/dialogue/sessions
#>
[CmdletBinding(DefaultParameterSetName = "File")]
param(
    [Parameter(ParameterSetName = "File", Mandatory = $true)]
    [string]$Path,

    [Parameter(ParameterSetName = "Dir", Mandatory = $true)]
    [string]$Directory
)

$ErrorActionPreference = "Stop"

function Add-Issue   { param($List, [string]$Msg) [void]$List.Add($Msg) }
function Add-Warning { param($List, [string]$Msg) [void]$List.Add($Msg) }

function Test-Regex {
    param([string]$Text, [string]$Pattern)
    return [regex]::IsMatch($Text, $Pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
}

function Get-RegexMatch {
    param([string]$Text, [string]$Pattern)
    return [regex]::Match($Text, $Pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
}

function Normalize-YamlScalar {
    param([string]$Value)
    if ($null -eq $Value) { return $null }
    $v = $Value.Trim()
    if ($v.Length -ge 2 -and $v.StartsWith('"') -and $v.EndsWith('"')) {
        return $v.Substring(1, $v.Length - 2)
    }
    return $v
}

function Get-YamlChildBlock {
    param([string]$Text, [string]$Key)

    $lines = $Text -split "`r?`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $pat = '^(?<indent>\s*)' + [regex]::Escape($Key) + ':\s*(?<inline>.*)$'
        $m = [regex]::Match($lines[$i], $pat)
        if (-not $m.Success) { continue }

        $base = $m.Groups["indent"].Value.Length
        $block = New-Object System.Collections.Generic.List[string]
        for ($j = $i + 1; $j -lt $lines.Count; $j++) {
            $line = $lines[$j]
            if ([string]::IsNullOrWhiteSpace($line)) { [void]$block.Add($line); continue }
            $ind = ([regex]::Match($line, '^\s*')).Value.Length
            if ($ind -le $base) { break }
            [void]$block.Add($line)
        }
        return [PSCustomObject]@{
            Found = $true
            Indent = $base
            InlineValue = $m.Groups["inline"].Value.Trim()
            Block = $block -join "`n"
        }
    }
    return [PSCustomObject]@{ Found = $false; Indent = 0; InlineValue = $null; Block = "" }
}

function Validate-PacketFile {
    param([string]$FilePath)

    $issues   = New-Object System.Collections.Generic.List[string]
    $warnings = New-Object System.Collections.Generic.List[string]
    $text = Get-Content -Path $FilePath -Raw -Encoding UTF8

    # Required top-level fields (aligned with Document/DAD/PACKET-SCHEMA.md and
    # C# TurnPacketYamlPersister.Render output).
    $required = @(
        '^type:\s*turn\s*$',
        '^from:\s*.+$',
        '^turn:\s*\d+\s*$',
        '^session_id:\s*.+$',
        '^handoff:\s*$',
        '^peer_review:\s*$'
    )
    foreach ($pat in $required) {
        if (-not (Test-Regex -Text $text -Pattern $pat)) {
            Add-Issue -List $issues -Msg "Missing required field/section matching: $pat"
        }
    }

    # Forbidden root-level fields (must live under handoff).
    if (Test-Regex -Text $text -Pattern '^\s*self_work:\s*$') {
        Add-Issue -List $issues -Msg "Forbidden section 'self_work'. Use 'my_work'."
    }
    if (Test-Regex -Text $text -Pattern '^suggest_done:\s*(true|false)\s*$') {
        Add-Issue -List $issues -Msg "Forbidden root-level 'suggest_done'. Move under handoff."
    }
    if (Test-Regex -Text $text -Pattern '^done_reason:\s*') {
        Add-Issue -List $issues -Msg "Forbidden root-level 'done_reason'. Move under handoff."
    }

    # from: must be one of the two peers.
    $fromMatch = Get-RegexMatch -Text $text -Pattern '^from:\s*(?<v>\S+)\s*$'
    if ($fromMatch.Success) {
        $from = Normalize-YamlScalar -Value $fromMatch.Groups["v"].Value
        if ($from -notin @("codex", "claude-code")) {
            Add-Issue -List $issues -Msg "from='$from' must be one of: codex, claude-code."
        }
    }

    # Checkpoint status enum (tolerate the augmented set the live validator allows).
    $allowedStatuses = 'PASS|FAIL|FAIL-then-FIXED|FAIL-then-PASS|BLOCKED|SKIPPED'
    $cr = Get-YamlChildBlock -Text $text -Key 'checkpoint_results'
    if ($cr.Found -and $cr.InlineValue -ne '[]' -and $cr.InlineValue -ne '{}') {
        $statusHits = [regex]::Matches($cr.Block, '^\s+status:\s*(?<v>[A-Za-z-]+)\s*$', [System.Text.RegularExpressions.RegexOptions]::Multiline)
        foreach ($h in $statusHits) {
            $v = $h.Groups["v"].Value
            if ($v -notmatch "^($allowedStatuses)$") {
                Add-Issue -List $issues -Msg "Unsupported checkpoint status '$v'."
            }
        }
    }

    # Handoff sub-fields.
    $handoffReady     = Get-RegexMatch -Text $text -Pattern '^\s+ready_for_peer_verification:\s*(?<v>true|false)\s*$'
    $handoffSuggest   = Get-RegexMatch -Text $text -Pattern '^\s+suggest_done:\s*(?<v>true|false)\s*$'
    $closeoutMatch    = Get-RegexMatch -Text $text -Pattern '^\s+closeout_kind:\s*(?<v>[^\r\n]*)\s*$'
    $nextTaskMatch    = Get-RegexMatch -Text $text -Pattern '^\s+next_task:\s*(?<v>[^\r\n]*)\s*$'
    $contextMatch     = Get-RegexMatch -Text $text -Pattern '^\s+context:\s*(?<v>[^\r\n]*)\s*$'
    $promptArtMatch   = Get-RegexMatch -Text $text -Pattern '^\s+prompt_artifact:\s*(?<v>[^\r\n]*)\s*$'

    $closeout    = if ($closeoutMatch.Success)  { Normalize-YamlScalar -Value $closeoutMatch.Groups["v"].Value }  else { $null }
    $nextTask    = if ($nextTaskMatch.Success)  { Normalize-YamlScalar -Value $nextTaskMatch.Groups["v"].Value }  else { $null }
    $context     = if ($contextMatch.Success)   { Normalize-YamlScalar -Value $contextMatch.Groups["v"].Value }   else { $null }
    $promptArt   = if ($promptArtMatch.Success) { Normalize-YamlScalar -Value $promptArtMatch.Groups["v"].Value } else { $null }

    $readyTrue    = $handoffReady.Success   -and $handoffReady.Groups["v"].Value   -eq "true"
    $readyFalse   = $handoffReady.Success   -and $handoffReady.Groups["v"].Value   -eq "false"
    $suggestTrue  = $handoffSuggest.Success -and $handoffSuggest.Groups["v"].Value -eq "true"

    $allowedCloseout = @("peer_handoff", "final_no_handoff", "recovery_resume")
    if (-not [string]::IsNullOrWhiteSpace($closeout) -and $allowedCloseout -notcontains $closeout) {
        Add-Issue -List $issues -Msg "handoff.closeout_kind must be one of: $($allowedCloseout -join ', ')."
    }

    if ($closeout -eq "peer_handoff") {
        if (-not $readyTrue)  { Add-Issue -List $issues -Msg "closeout_kind=peer_handoff requires ready_for_peer_verification=true." }
        if ($suggestTrue)     { Add-Issue -List $issues -Msg "closeout_kind=peer_handoff must not also set suggest_done=true." }
    }
    elseif ($closeout -eq "final_no_handoff") {
        if (-not $readyFalse)                              { Add-Issue -List $issues -Msg "closeout_kind=final_no_handoff requires ready_for_peer_verification=false." }
        if (-not [string]::IsNullOrWhiteSpace($promptArt)) { Add-Issue -List $issues -Msg "closeout_kind=final_no_handoff must keep prompt_artifact empty." }
        if (-not [string]::IsNullOrWhiteSpace($nextTask))  { Add-Issue -List $issues -Msg "closeout_kind=final_no_handoff must keep next_task empty." }
    }
    elseif ($closeout -eq "recovery_resume") {
        if (-not $readyFalse)                              { Add-Issue -List $issues -Msg "closeout_kind=recovery_resume requires ready_for_peer_verification=false." }
        if ($suggestTrue)                                  { Add-Issue -List $issues -Msg "closeout_kind=recovery_resume must not set suggest_done=true." }
        if (-not [string]::IsNullOrWhiteSpace($promptArt)) { Add-Issue -List $issues -Msg "closeout_kind=recovery_resume must keep prompt_artifact empty." }
    }

    if ($readyTrue) {
        if ([string]::IsNullOrWhiteSpace($nextTask)) { Add-Issue -List $issues -Msg "ready_for_peer_verification=true requires handoff.next_task." }
        if ([string]::IsNullOrWhiteSpace($context))  { Add-Issue -List $issues -Msg "ready_for_peer_verification=true requires handoff.context." }
    }

    if ($suggestTrue -and -not (Test-Regex -Text $text -Pattern '^\s+done_reason:\s*\S')) {
        Add-Issue -List $issues -Msg "suggest_done=true requires handoff.done_reason."
    }

    return [PSCustomObject]@{
        Path = $FilePath
        Issues = $issues
        Warnings = $warnings
    }
}

# ---- Entry ----

$targets = @()
if ($PSCmdlet.ParameterSetName -eq "File") {
    if (-not (Test-Path $Path)) { throw "Path not found: $Path" }
    $targets += (Resolve-Path $Path).Path
}
else {
    if (-not (Test-Path $Directory)) { throw "Directory not found: $Directory" }
    $targets = Get-ChildItem -Path $Directory -Recurse -File -Filter "turn-*.yaml" |
        ForEach-Object { $_.FullName }
    if ($targets.Count -eq 0) {
        Write-Host "No turn-*.yaml files found under $Directory."
        exit 0
    }
}

$allIssues   = New-Object System.Collections.Generic.List[string]
$allWarnings = New-Object System.Collections.Generic.List[string]

foreach ($t in $targets) {
    $r = Validate-PacketFile -FilePath $t
    foreach ($i in $r.Issues)   { [void]$allIssues.Add("$($r.Path): $i") }
    foreach ($w in $r.Warnings) { [void]$allWarnings.Add("$($r.Path): $w") }
}

if ($allIssues.Count -gt 0) {
    Write-Output "DAD packet validation failed:"
    foreach ($i in $allIssues) { Write-Output "- $i" }
    exit 1
}

if ($allWarnings.Count -gt 0) {
    Write-Output "DAD packet validation warnings:"
    foreach ($w in $allWarnings) { Write-Output "- $w" }
}

Write-Output "DAD packet validation passed ($($targets.Count) file(s))."
exit 0
