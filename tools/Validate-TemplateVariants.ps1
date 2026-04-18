param(
    [switch]$RunVariantValidators,
    [int]$LargeRootReadmeCharThreshold = 12000,
    [int]$LargeRootContractCharThreshold = 8000
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$enRoot = Join-Path $repoRoot 'en'
$koRoot = Join-Path $repoRoot 'ko'

if (-not (Test-Path -LiteralPath $enRoot) -or -not (Test-Path -LiteralPath $koRoot)) {
    throw "Both 'en' and 'ko' directories must exist under $repoRoot."
}

$requiredRootFiles = @(
    'AGENTS.md',
    'CLAUDE.md',
    'DIALOGUE-PROTOCOL.md',
    'PROJECT-RULES.md',
    '.githooks/pre-commit',
    'tools/Validate-TemplateVariants.ps1',
    'tools/Validate-TemplateVariants.sh',
    'tools/_ps1_runner.sh',
    'README.md'
)

$requiredVariantFiles = @(
    'DIALOGUE-PROTOCOL.md',
    'AGENTS.md',
    'CLAUDE.md',
    'Document/DAD/README.md',
    'Document/DAD/BACKLOG-AND-ADMISSION.md',
    'Document/DAD/PACKET-SCHEMA.md',
    'Document/DAD/STATE-AND-LIFECYCLE.md',
    'Document/DAD/VALIDATION-AND-PROMPTS.md',
    'Document/dialogue/backlog.json',
    'tools/Manage-DadBacklog.ps1',
    'tools/Manage-DadBacklog.sh',
    'tools/Register-CodexSkills.ps1',
    'tools/Register-CodexSkills.sh',
    'tools/Unregister-CodexSkills.ps1',
    'tools/Unregister-CodexSkills.sh',
    'tools/Set-CodexSkillNamespace.ps1',
    'tools/Set-CodexSkillNamespace.sh',
    'tools/Validate-DadBacklog.ps1',
    'tools/Validate-DadBacklog.sh',
    'tools/Validate-CodexSkillMetadata.ps1',
    'tools/Validate-CodexSkillMetadata.sh'
)

function Get-RelativeFiles([string]$Root) {
    $rootPath = (Resolve-Path -LiteralPath $Root).Path
    Get-ChildItem -Path $rootPath -Recurse -File |
        ForEach-Object { $_.FullName.Substring($rootPath.Length + 1).Replace('\', '/') } |
        Sort-Object
}

function Get-NormalizedVariantPath([string]$RelativePath) {
    $path = $RelativePath.Replace('\', '/')

    if ($path -match '^\.prompts/(\d{2})-[^/]+\.md$') {
        return ".prompts/$($matches[1]).md"
    }

    if ($path -match '^Document/DAD[^/]*\.md$') {
        return 'Document/DAD-OPERATIONS.md'
    }

    return $path
}

function Get-RootReadmeIssues([string]$Path) {
    $issues = New-Object System.Collections.Generic.List[string]
    $text = Get-Content -Raw -Encoding UTF8 $Path
    $references = [regex]::Matches($text, '\[[^\]]+\]\(([^)]+)\)')

    foreach ($match in $references) {
        $reference = $match.Groups[1].Value.Trim()
        if ([string]::IsNullOrWhiteSpace($reference)) {
            continue
        }

        if ($reference -match '^[a-zA-Z][a-zA-Z0-9+.-]*://' -or $reference.StartsWith('#')) {
            continue
        }

        $reference = ($reference -split '#', 2)[0]
        $reference = ($reference -split '\?', 2)[0]
        if ([string]::IsNullOrWhiteSpace($reference)) {
            continue
        }

        $normalizedReference = $reference
        if ($normalizedReference.StartsWith('./')) {
            $normalizedReference = $normalizedReference.Substring(2)
        }

        $targetPath = Join-Path $repoRoot ($normalizedReference -replace '/', '\')
        if (-not (Test-Path -LiteralPath $targetPath)) {
            $issues.Add("Root README missing local reference target: $reference")
        }
    }

    return $issues
}

function Get-LocalMarkdownLinkIssues([string]$Path) {
    $issues = New-Object System.Collections.Generic.List[string]
    $text = Get-Content -Raw -Encoding UTF8 $Path
    $references = [regex]::Matches($text, '\[[^\]]+\]\(([^)]+)\)')

    foreach ($match in $references) {
        $reference = $match.Groups[1].Value.Trim()
        if ([string]::IsNullOrWhiteSpace($reference)) {
            continue
        }

        if ($reference -match '^[a-zA-Z][a-zA-Z0-9+.-]*://' -or $reference.StartsWith('#')) {
            continue
        }

        $reference = ($reference -split '#', 2)[0]
        $reference = ($reference -split '\?', 2)[0]
        if ([string]::IsNullOrWhiteSpace($reference)) {
            continue
        }

        $normalizedReference = $reference
        if ($normalizedReference.StartsWith('./')) {
            $normalizedReference = $normalizedReference.Substring(2)
        }

        $targetPath = Join-Path $repoRoot ($normalizedReference -replace '/', '\')
        if (-not (Test-Path -LiteralPath $targetPath)) {
            $issues.Add("Root maintainer doc missing local reference target in $([System.IO.Path]::GetFileName($Path)): $reference")
        }
    }

    return $issues
}

function Get-RootMaintainerDocIssues([string]$Path) {
    $issues = New-Object System.Collections.Generic.List[string]
    $text = Get-Content -Raw -Encoding UTF8 $Path

    if ([regex]::IsMatch($text, '[^\u0009\u000A\u000D\u0020-\u007E]')) {
        $issues.Add("Root maintainer doc contains non-ASCII characters: $([System.IO.Path]::GetFileName($Path))")
    }

    if ($text.Contains([char]0xFFFD)) {
        $issues.Add("Root maintainer doc contains replacement characters: $([System.IO.Path]::GetFileName($Path))")
    }

    foreach ($line in @($text -split "`r?`n")) {
        if ($line -match '^\s*-\s+`[^`]+`\s+\?\?.+$') {
            $issues.Add("Root maintainer doc contains suspicious broken bullet text: $([System.IO.Path]::GetFileName($Path))")
            break
        }
    }

    return $issues
}

function Get-VariantInvariantIssues([string]$VariantRoot, [string]$VariantName) {
    $issues = New-Object System.Collections.Generic.List[string]

    foreach ($relativePath in $requiredVariantFiles) {
        $fullPath = Join-Path $VariantRoot ($relativePath -replace '/', '\')
        if (-not (Test-Path -LiteralPath $fullPath)) {
            $issues.Add("Missing required split-protocol file in ${VariantName}/: $relativePath")
        }
    }

    $dialogueProtocol = Join-Path $VariantRoot 'DIALOGUE-PROTOCOL.md'
    if (Test-Path -LiteralPath $dialogueProtocol) {
        $protocolText = Get-Content -Raw -Encoding UTF8 $dialogueProtocol
        $requiredProtocolMarkers = @(
            'Document/DAD/README.md',
            'Document/DAD/BACKLOG-AND-ADMISSION.md',
            'Document/DAD/PACKET-SCHEMA.md',
            'Document/DAD/STATE-AND-LIFECYCLE.md',
            'Document/DAD/VALIDATION-AND-PROMPTS.md'
        )

        foreach ($marker in $requiredProtocolMarkers) {
            if (-not $protocolText.Contains($marker)) {
                $issues.Add("${VariantName}/DIALOGUE-PROTOCOL.md is missing split-reference marker: $marker")
            }
        }
    }

    foreach ($contractPath in @('AGENTS.md', 'CLAUDE.md')) {
        $fullPath = Join-Path $VariantRoot $contractPath
        if (-not (Test-Path -LiteralPath $fullPath)) {
            continue
        }

        $contractText = Get-Content -Raw -Encoding UTF8 $fullPath
        if (-not $contractText.Contains('Document/DAD/')) {
            $issues.Add("${VariantName}/$contractPath does not mention following split protocol references under Document/DAD/.")
        }
    }

    return $issues
}

$issues = New-Object System.Collections.Generic.List[string]
$enFiles = Get-RelativeFiles -Root $enRoot
$koFiles = Get-RelativeFiles -Root $koRoot

foreach ($relativeRootFile in $requiredRootFiles) {
    $rootFile = Join-Path $repoRoot ($relativeRootFile -replace '/', '\')
    if (-not (Test-Path -LiteralPath $rootFile)) {
        $issues.Add("Missing required root maintainer file: $relativeRootFile")
    }
}

foreach ($relativeRootDoc in @('README.md', 'PROJECT-RULES.md', 'AGENTS.md', 'CLAUDE.md', 'DIALOGUE-PROTOCOL.md')) {
    $rootDocPath = Join-Path $repoRoot $relativeRootDoc
    if (-not (Test-Path -LiteralPath $rootDocPath)) {
        continue
    }

    $rootDocText = Get-Content -Raw -Encoding UTF8 $rootDocPath
    if ($relativeRootDoc -ne 'README.md' -and $rootDocText.Length -ge $LargeRootContractCharThreshold) {
        $issues.Add("Root contract $relativeRootDoc length ($($rootDocText.Length)) exceeds large-doc threshold ($LargeRootContractCharThreshold). Split frequently read root contracts by topic.")
    }

    foreach ($docIssue in Get-RootMaintainerDocIssues -Path $rootDocPath) {
        $issues.Add($docIssue)
    }

    if ($relativeRootDoc -ne 'README.md') {
        foreach ($linkIssue in Get-LocalMarkdownLinkIssues -Path $rootDocPath) {
            $issues.Add($linkIssue)
        }
    }
}

foreach ($variant in @(
        @{ Name = 'en'; Root = $enRoot },
        @{ Name = 'ko'; Root = $koRoot }
    )) {
    foreach ($variantIssue in Get-VariantInvariantIssues -VariantRoot $variant.Root -VariantName $variant.Name) {
        $issues.Add($variantIssue)
    }
}

$enNormalized = @{}
$koNormalized = @{}
foreach ($enFile in $enFiles) {
    $normalized = Get-NormalizedVariantPath -RelativePath $enFile
    if ($enNormalized.ContainsKey($normalized)) {
        $issues.Add("Duplicate normalized path in en/: $normalized")
    }
    else {
        $enNormalized[$normalized] = $enFile
    }
}

foreach ($koFile in $koFiles) {
    $normalized = Get-NormalizedVariantPath -RelativePath $koFile
    if ($koNormalized.ContainsKey($normalized)) {
        $issues.Add("Duplicate normalized path in ko/: $normalized")
    }
    else {
        $koNormalized[$normalized] = $koFile
    }
}

foreach ($normalized in $enNormalized.Keys) {
    if (-not $koNormalized.ContainsKey($normalized)) {
        $issues.Add("Missing ko counterpart for en/$($enNormalized[$normalized])")
    }
}

foreach ($normalized in $koNormalized.Keys) {
    if (-not $enNormalized.ContainsKey($normalized)) {
        $issues.Add("Missing en counterpart for ko/$($koNormalized[$normalized])")
    }
}

$rootReadme = Join-Path $repoRoot 'README.md'
if (-not (Test-Path -LiteralPath $rootReadme)) {
    $issues.Add('Root README.md is missing.')
}
else {
    $rootReadmeText = Get-Content -Raw -Encoding UTF8 $rootReadme
    if ($rootReadmeText.Length -ge $LargeRootReadmeCharThreshold) {
        $issues.Add("Root README.md length ($($rootReadmeText.Length)) exceeds large-doc threshold ($LargeRootReadmeCharThreshold). Split frequently read root docs by topic.")
    }

    $promptMatch = [regex]::Match($rootReadmeText, 'reusable prompt library \((\d+) prompts\)')
    if (-not $promptMatch.Success) {
        $issues.Add('Root README.md is missing the reusable prompt count marker.')
    }
    else {
        $declaredPromptCount = [int]$promptMatch.Groups[1].Value
        $enPromptCount = @(Get-ChildItem -Path (Join-Path $enRoot '.prompts') -File -Filter '*.md' | Where-Object { $_.Name -ne 'README.md' }).Count
        $koPromptCount = @(Get-ChildItem -Path (Join-Path $koRoot '.prompts') -File -Filter '*.md' | Where-Object { $_.Name -ne 'README.md' }).Count

        if ($declaredPromptCount -ne $enPromptCount -or $declaredPromptCount -ne $koPromptCount) {
            $issues.Add("Root README prompt count ($declaredPromptCount) does not match en=$enPromptCount / ko=$koPromptCount.")
        }
    }

    foreach ($readmeIssue in Get-RootReadmeIssues -Path $rootReadme) {
        $issues.Add($readmeIssue)
    }
}

if ($RunVariantValidators) {
    $variantRoots = @($enRoot, $koRoot)
    foreach ($variantRoot in $variantRoots) {
        & (Join-Path $variantRoot 'tools/Validate-Documents.ps1') `
            -Root $variantRoot `
            -IncludeRootGuides `
            -IncludeAgentDocs `
            -ReportLargeDocs `
            -ReportLargeRootGuides `
            -FailOnLargeDocs | Out-Null
        if (-not $?) {
            $issues.Add("Variant document validation failed for $variantRoot.")
        }

        & (Join-Path $variantRoot 'tools/Validate-CodexSkillMetadata.ps1') -Root $variantRoot | Out-Null
        if (-not $?) {
            $issues.Add("Variant Codex skill metadata validation failed for $variantRoot.")
        }

        & (Join-Path $variantRoot 'tools/Lint-StaleTerms.ps1') | Out-Null
        if (-not $?) {
            $issues.Add("Variant stale-term lint failed for $variantRoot.")
        }

        & (Join-Path $variantRoot 'tools/Validate-DadPacket.ps1') -Root $variantRoot -AllSessions | Out-Null
        if (-not $?) {
            $issues.Add("Variant DAD packet validation failed for $variantRoot.")
        }

        & (Join-Path $variantRoot 'tools/Validate-DadBacklog.ps1') -Root $variantRoot | Out-Null
        if (-not $?) {
            $issues.Add("Variant DAD backlog validation failed for $variantRoot.")
        }

        $tempCodexHome = Join-Path ([System.IO.Path]::GetTempPath()) ("dad-v2-register-validate-" + [System.Guid]::NewGuid().ToString("N"))
        try {
            New-Item -ItemType Directory -Force -Path $tempCodexHome | Out-Null
            & (Join-Path $variantRoot 'tools/Register-CodexSkills.ps1') `
                -Root $variantRoot `
                -CodexHome $tempCodexHome `
                -ValidateOnly `
                -AllowTemplateNamespace | Out-Null
            if (-not $?) {
                $issues.Add("Variant Codex skill registration dry-run failed for $variantRoot.")
            }
        }
        finally {
            if (Test-Path -LiteralPath $tempCodexHome) {
                Remove-Item -LiteralPath $tempCodexHome -Force -Recurse
            }
        }
    }
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output 'Template variant validation passed.'
