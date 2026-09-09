Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$lintScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'lint-llm-instructions.ps1'
$generateScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'generate-skills-index.ps1'
Write-Host '== lint-llm-instructions self-tests =='

function New-LintedFixture {
    param([string]$PackageVersion = '1.2.3')
    $root = New-LlmFixtureRoot -PackageVersion $PackageVersion
    $scriptsSource = Split-Path -Parent $PSScriptRoot
    foreach ($scriptFile in (Get-ChildItem -Path $scriptsSource -Filter '*.ps1' -File)) {
        Write-FixtureFile -Root $root -RelativePath (
            "scripts/$($scriptFile.Name)"
        ) -Content ([System.IO.File]::ReadAllText($scriptFile.FullName))
    }
    & $generateScript -Root $root *> $null
    Assert-ExitCode 0 'fixture index generation should succeed'
    return $root
}

Invoke-TestCase 'Passes_When_FixtureIsValid' {
    $root = New-LintedFixture
    try {
        & $lintScript -Root $root *> $null
        Assert-ExitCode 0 'valid fixture should pass'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Passes_With_OptionalFrontmatterKeys' {
    $root = New-LintedFixture
    try {
        $skill = @(
            '---',
            'name: extra-skill',
            'description: Optional-keys fixture skill. Use when testing tolerance.',
            'license: MIT',
            'allowed-tools: Read Grep',
            'metadata:',
            '  category: Core',
            '---',
            '',
            '# Skill: Extra',
            '',
            'Body.'
        ) -join "`n"
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/extra-skill/SKILL.md' -Content $skill
        & $generateScript -Root $root *> $null
        Assert-ExitCode 0 'index generation should tolerate optional keys'
        & $lintScript -Root $root *> $null
        Assert-ExitCode 0 'lint should tolerate optional frontmatter keys'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_PointerFileMissingDelegationLink' {
    $root = New-LintedFixture
    try {
        Write-FixtureFile -Root $root -RelativePath 'CLAUDE.md' -Content (
            "# Claude Configuration`n`nAll rules live elsewhere, sorry.`n"
        )
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'pointer missing delegation link should fail'
        Assert-True ($output -match 'CLAUDE\.md') "output should name the pointer, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_PointerFileMissing' {
    $root = New-LintedFixture
    try {
        Remove-Item -LiteralPath (Get-FixturePath -Root $root -RelativePath 'GEMINI.md') -Force
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'missing pointer file should fail'
        Assert-True ($output -match 'GEMINI\.md') "output should name the pointer, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_SkillNameUppercase' {
    $root = New-LintedFixture
    try {
        $skill = @(
            '---',
            'name: Bad-Name',
            'description: Uppercase fixture skill. Use when testing validation.',
            'metadata:',
            '  category: Core',
            '---',
            '',
            '# Skill: Bad',
            '',
            'Body.'
        ) -join "`n"
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/Bad-Name/SKILL.md' -Content $skill
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'uppercase skill name should fail'
        Assert-True ($output -match 'Bad-Name') "output should name the skill, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_DescriptionOver1024Chars' {
    $root = New-LintedFixture
    try {
        $longDescription = 'x' * 1025
        $skill = @(
            '---',
            'name: long-description-skill',
            "description: $longDescription",
            'metadata:',
            '  category: Core',
            '---',
            '',
            '# Skill: Long',
            '',
            'Body.'
        ) -join "`n"
        Write-FixtureFile -Root $root -RelativePath (
            '.llm/skills/long-description-skill/SKILL.md'
        ) -Content $skill
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'description over 1024 characters should fail'
        Assert-True ($output -match '1024') 'output should mention the limit, got: $output'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_IndexDrift_And_FixRegenerates' {
    $root = New-LintedFixture
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/index.md' -Content 'stale'
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'stale index should fail'
        Assert-True ($output -match 'index') "output should mention the index, got: $output"
        & $lintScript -Root $root -Fix *> $null
        Assert-ExitCode 0 '-Fix should regenerate the index and pass'
        $content = [System.IO.File]::ReadAllText(
            (Get-FixturePath -Root $root -RelativePath '.llm/skills/index.md')
        )
        Assert-True ($content -match '# Skills Index') 'regenerated index should be real content'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_ContextHasMultipleH1' {
    $root = New-LintedFixture
    try {
        $context = @(
            '# LLM Agent Instructions',
            '',
            'See the generated [Skills Index](./skills/index.md).',
            '',
            '## Repository Overview',
            '',
            '- **Version**: 1.2.3',
            '',
            '# Another Title',
            '',
            'Oops.'
        ) -join "`n"
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content $context
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'multiple H1s should fail'
        Assert-True ($output -match 'H1') 'output should mention the H1 rule, got: $output'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_ContextVersionMismatchesPackage_And_FixSyncs' {
    $root = New-LintedFixture
    try {
        $context = [System.IO.File]::ReadAllText(
            (Get-FixturePath -Root $root -RelativePath '.llm/context.md')
        )
        $staleContext = $context -replace '1\.2\.3', '0.0.1'
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content $staleContext
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'stale version should fail'
        Assert-True ($output -match '[Vv]ersion') 'output should mention version, got: $output'
        & $lintScript -Root $root -Fix *> $null
        Assert-ExitCode 0 '-Fix should sync the version and pass'
        $fixed = [System.IO.File]::ReadAllText(
            (Get-FixturePath -Root $root -RelativePath '.llm/context.md')
        )
        Assert-True ($fixed -match '\*\*Version\*\*: 1\.2\.3') (
            "fixed context should carry the package version, got: $fixed"
        )
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_SkillDirectoryHasNoSkillMd' {
    $root = New-LintedFixture
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/empty-skill/README.md' -Content (
            'not a skill'
        )
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'skill directory without SKILL.md should fail'
        Assert-True ($output -match 'empty-skill') "output should name the directory, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_ContextMissingSkillsLink' {
    $root = New-LintedFixture
    try {
        $context = [System.IO.File]::ReadAllText(
            (Get-FixturePath -Root $root -RelativePath '.llm/context.md')
        )
        $stripped = $context -replace 'See the generated \[Skills Index\]\(\./skills/index\.md\)\.', ''
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content $stripped
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'context without skills index link should fail'
        Assert-True ($output -match 'skills/index\.md') (
            'output should mention the required link, got: $output'
        )
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_MissingContextFile' {
    $root = New-TempRoot
    try {
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'missing context should fail'
        Assert-True ($output -match 'context\.md') "output should name context.md, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Write-Host "== lint-llm-instructions: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
