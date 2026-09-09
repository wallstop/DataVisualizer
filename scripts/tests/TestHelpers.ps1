Set-StrictMode -Version 2.0

$script:TestHelpersScriptsDir = Split-Path -Parent $PSCommandPath

function New-TempRoot {
    param([string]$Prefix = 'llm-selftest-')
    $path = Join-Path (
        [System.IO.Path]::GetTempPath()
    ) ($Prefix + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

function Remove-TempRoot {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Write-FixtureFile {
    param([string]$Root, [string]$RelativePath, [string]$Content)
    $fullPath = Join-Path $Root $RelativePath
    $directory = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText(
        $fullPath,
        $Content,
        [System.Text.UTF8Encoding]::new($false)
    )
}

function Get-FixturePath {
    param([string]$Root, [string]$RelativePath)
    return (Join-Path $Root $RelativePath)
}

function Get-FixtureLines {
    param([int]$Count)
    if ($Count -le 0) {
        return ''
    }
    $lines = New-Object System.Collections.Generic.List[string]
    for ($i = 1; $i -le $Count; $i++) {
        $lines.Add("line-$i")
    }
    return (($lines -join "`n") + "`n")
}

function Invoke-TestCase {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        Write-Host "  PASS: $Name"
        return $true
    } catch {
        Write-Host "  FAIL: $Name :: $($_.Exception.Message)"
        $script:TestFailureCount++
        return $false
    }
}

function Assert-True {
    param([object]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-ExitCode {
    param([int]$Expected, [string]$Description)
    $actual = $LASTEXITCODE
    if ($actual -ne $Expected) {
        throw "$Description (expected exit $Expected, got $actual)"
    }
}

function New-LlmFixtureRoot {
    param([string]$PackageVersion = '1.2.3')

    $root = New-TempRoot -Prefix 'llm-fixture-'
    $contextLink = '](./.llm/context.md)'
    $contextLinkNested = '](../.llm/context.md)'

    $context = @(
        '# LLM Agent Instructions',
        '',
        'Procedural skills are in the [skills/](./skills/) directory.',
        '',
        '## Repository Overview',
        '',
        '- **Package**: com.test.fixture',
        "- **Version**: $PackageVersion",
        '',
        '## Skills Reference',
        '',
        'See the generated [Skills Index](./skills/index.md).'
    ) -join "`n"

    Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content $context
    Write-FixtureFile -Root $root -RelativePath 'package.json' -Content (
        '{"name":"com.test.fixture","version":"' + $PackageVersion + '"}'
    )

    $pointerContent = @(
        '# Repository Guidelines',
        '',
        "See the [AI Agent Guidelines]($contextLink) for all AI agent guidelines."
    ) -join "`n"
    $pointerContentNested = @(
        '# GitHub Copilot Instructions',
        '',
        "See the [AI Agent Guidelines]($contextLinkNested) for all AI agent guidelines."
    ) -join "`n"

    foreach ($pointer in @('AGENTS.md', 'CLAUDE.md', 'GEMINI.md', '.cursorrules', '.windsurfrules')) {
        Write-FixtureFile -Root $root -RelativePath $pointer -Content $pointerContent
    }
    Write-FixtureFile -Root $root -RelativePath '.github/copilot-instructions.md' -Content $pointerContentNested

    $skillAlpha = @(
        '---',
        'name: alpha-skill',
        'description: Core skill used by the fixture. Use when testing core behavior.',
        'metadata:',
        '  category: Core',
        '---',
        '',
        '# Skill: Alpha Skill',
        '',
        'Body.'
    ) -join "`n"
    $skillZulu = @(
        '---',
        'name: zulu-skill',
        'description: Feature skill used by the fixture. Use when testing feature behavior.',
        'metadata:',
        '  category: Feature',
        '---',
        '',
        '# Skill: Zulu Skill',
        '',
        'Body.'
    ) -join "`n"
    Write-FixtureFile -Root $root -RelativePath '.llm/skills/alpha-skill/SKILL.md' -Content $skillAlpha
    Write-FixtureFile -Root $root -RelativePath '.llm/skills/zulu-skill/SKILL.md' -Content $skillZulu

    return $root
}
