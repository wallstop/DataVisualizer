Set-StrictMode -Version 2.0

$script:LlmConfigDefaultRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)

$AllowedSkillCategories = @('Core', 'Workflow', 'Feature')
$DefaultSkillCategory = 'Feature'
$MaxFileLines = 300
$WarnFileLines = 280
$SkillNamePattern = '^[a-z0-9]+(?:-[a-z0-9]+)*$'
$AsciiPattern = '^[\x20-\x7E]*$'
$SkillsIndexFileName = 'index.md'
$ContextRelativePath = '.llm/context.md'
$SkillsRelativePath = '.llm/skills'

$AgentEntrypoints = @(
    @{ Path = 'AGENTS.md'; Link = '](./.llm/context.md)' },
    @{ Path = 'CLAUDE.md'; Link = '](./.llm/context.md)' },
    @{ Path = '.cursorrules'; Link = '](./.llm/context.md)' },
    @{ Path = '.windsurfrules'; Link = '](./.llm/context.md)' },
    @{ Path = '.github/copilot-instructions.md'; Link = '](../.llm/context.md)' }
)

function Resolve-LlmRoot {
    param([string]$Root)
    $resolved = $Root
    if ([string]::IsNullOrWhiteSpace($resolved)) {
        $resolved = $script:LlmConfigDefaultRoot
    }
    $resolved = [System.IO.Path]::GetFullPath($resolved)
    if (-not (Test-Path -LiteralPath $resolved)) {
        throw "repository root '$resolved' does not exist"
    }
    return $resolved
}

function ConvertTo-NormalizedText {
    param([string]$Text)
    return $Text.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Get-PhysicalLineCount {
    param([string]$NormalizedText)
    if ([string]::IsNullOrEmpty($NormalizedText)) {
        return 0
    }
    $count = 0
    foreach ($character in $NormalizedText.ToCharArray()) {
        if ($character -eq "`n") {
            $count++
        }
    }
    if (-not $NormalizedText.EndsWith("`n")) {
        $count++
    }
    return $count
}

function Read-Utf8Text {
    param([string]$Path)
    $text = [System.IO.File]::ReadAllText($Path)
    return ConvertTo-NormalizedText $text
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$NormalizedText)
    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrEmpty($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $NormalizedText, [System.Text.UTF8Encoding]::new($false))
}

function Get-OrdinalSorted {
    param([object[]]$Values)
    return [System.Linq.Enumerable]::OrderBy(
        [string[]]@($Values),
        [System.Func[string, string]] { param($value) $value },
        [System.StringComparer]::Ordinal
    )
}

function Read-SkillMetadata {
    param([string]$SkillPath, [string]$ExpectedDirectory)

    $text = Read-Utf8Text $SkillPath
    if ([string]::IsNullOrEmpty($text)) {
        throw "SKILL.md is empty"
    }
    $lines = $text.Split("`n")
    if ($lines[0].TrimEnd() -ne '---') {
        throw 'SKILL.md must start with a YAML frontmatter block (---)'
    }
    $closingIndex = -1
    for ($i = 1; $i -lt $lines.Count -and $i -le 64; $i++) {
        if ($lines[$i].TrimEnd() -eq '---') {
            $closingIndex = $i
            break
        }
    }
    if ($closingIndex -lt 0) {
        throw 'SKILL.md frontmatter is not terminated by ---'
    }

    $name = $null
    $description = $null
    $category = $null
    $inMetadata = $false
    $seenKeys = New-Object 'System.Collections.Generic.HashSet[string]'
    for ($i = 1; $i -lt $closingIndex; $i++) {
        $line = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) {
            continue
        }
        if ($line.StartsWith(' ') -or $line.StartsWith("`t")) {
            if (-not $inMetadata) {
                throw "unexpectedly indented frontmatter line: '$line'"
            }
            if ($line -notmatch '^\s+([A-Za-z][A-Za-z0-9_-]*):\s*(.*)$') {
                throw "malformed metadata entry: '$line'"
            }
            if ($Matches[1] -eq 'category') {
                $category = $Matches[2].Trim()
            }
            continue
        }
        if ($line -notmatch '^([A-Za-z][A-Za-z0-9_-]*):\s*(.*)$') {
            throw "malformed frontmatter line: '$line'"
        }
        $key = $Matches[1]
        $value = $Matches[2].Trim()
        if ($value.StartsWith('"') -and $value.EndsWith('"') -and $value.Length -ge 2) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        if (-not $seenKeys.Add($key)) {
            throw "duplicate frontmatter key: '$key'"
        }
        if ($key -eq 'metadata') {
            if ([string]::IsNullOrEmpty($value)) {
                $inMetadata = $true
                continue
            }
            throw "'metadata' must be a mapping with indented entries"
        }
        $inMetadata = $false
        switch ($key) {
            'name' { $name = $value }
            'description' { $description = $value }
            default { }
        }
    }

    if ([string]::IsNullOrEmpty($name)) {
        throw 'frontmatter is missing required field: name'
    }
    if ([string]::IsNullOrEmpty($description)) {
        throw 'frontmatter is missing required field: description'
    }
    if ($name.Length -gt 64) {
        throw "name must be at most 64 characters (got $($name.Length))"
    }
    if ($name -notmatch $SkillNamePattern) {
        throw "name '$name' is invalid: lowercase letters, numbers, and single hyphens only"
    }
    if ($name -ne $ExpectedDirectory) {
        throw "name '$name' does not match skill directory '$ExpectedDirectory'"
    }
    if ($description.Length -gt 1024) {
        throw "description must be at most 1024 characters (got $($description.Length))"
    }
    foreach ($field in @($name, $description)) {
        if ($field -notmatch $AsciiPattern) {
            throw "frontmatter field must be ASCII-only: '$field'"
        }
    }
    if (-not [string]::IsNullOrEmpty($category) -and $AllowedSkillCategories -notcontains $category) {
        throw "unknown category '$category' (allowed: $($AllowedSkillCategories -join ', '))"
    }
    if ([string]::IsNullOrEmpty($category)) {
        $category = $DefaultSkillCategory
    }

    return [PSCustomObject]@{
        Name = $name
        Description = $description
        Category = $category
        Directory = $ExpectedDirectory
        Path = $SkillPath
    }
}

function Get-SkillMetadataList {
    param([string]$SkillsRoot)
    $skills = @()
    foreach ($directory in (Get-OrdinalSorted @(
                (Get-ChildItem -Path $SkillsRoot -Directory | ForEach-Object { $_.Name })
            ))) {
        $skillPath = Join-Path $SkillsRoot (Join-Path $directory 'SKILL.md')
        $skills += Read-SkillMetadata -SkillPath $skillPath -ExpectedDirectory $directory
    }
    return $skills
}

function ConvertTo-SkillsIndexContent {
    param([object[]]$Skills)

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.AppendLine('<!-- DO NOT EDIT - generated by scripts/generate-skills-index.ps1. -->')
    [void]$builder.AppendLine('<!-- Regenerate: pwsh -NoProfile -File scripts/generate-skills-index.ps1 -->')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('# Skills Index')
    [void]$builder.AppendLine()
    foreach ($category in $AllowedSkillCategories) {
        [void]$builder.AppendLine("## $category Skills")
        [void]$builder.AppendLine()
        [void]$builder.AppendLine('| Skill | When to Use |')
        [void]$builder.AppendLine('| --- | --- |')
        $entries = [System.Linq.Enumerable]::OrderBy(
            [object[]]@($Skills | Where-Object { $_.Category -eq $category }),
            [System.Func[object, string]] { param($skill) $skill.Name },
            [System.StringComparer]::Ordinal
        )
        foreach ($entry in $entries) {
            $row = "| [$($entry.Name)](./$($entry.Directory)/SKILL.md) | $($entry.Description) |"
            [void]$builder.AppendLine($row)
        }
        [void]$builder.AppendLine()
    }
    return ConvertTo-NormalizedText $builder.ToString()
}

Export-ModuleMember -Variable @(
    'AllowedSkillCategories',
    'DefaultSkillCategory',
    'MaxFileLines',
    'WarnFileLines',
    'SkillNamePattern',
    'AsciiPattern',
    'SkillsIndexFileName',
    'ContextRelativePath',
    'SkillsRelativePath',
    'AgentEntrypoints'
) -Function @(
    'Resolve-LlmRoot',
    'ConvertTo-NormalizedText',
    'Get-PhysicalLineCount',
    'Read-Utf8Text',
    'Write-Utf8NoBom',
    'Get-OrdinalSorted',
    'Read-SkillMetadata',
    'Get-SkillMetadataList',
    'ConvertTo-SkillsIndexContent'
)
