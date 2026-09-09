Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$generateScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'generate-skills-index.ps1'
Write-Host '== generate-skills-index self-tests =='

function Assert-SectionOrder {
    param([string]$Content)
    $coreIndex = $Content.IndexOf('## Core Skills')
    $workflowIndex = $Content.IndexOf('## Workflow Skills')
    $featureIndex = $Content.IndexOf('## Feature Skills')
    Assert-True ($coreIndex -ge 0) 'index should contain a Core Skills section'
    Assert-True ($workflowIndex -ge 0) 'index should contain a Workflow Skills section'
    Assert-True ($featureIndex -ge 0) 'index should contain a Feature Skills section'
    Assert-True (
        $coreIndex -lt $workflowIndex -and $workflowIndex -lt $featureIndex
    ) 'sections should appear in fixed category order'
}

Invoke-TestCase 'Generates_Index_GroupedAndSorted' {
    $root = New-TempRoot
    try {
        $skills = @(
            @{ Dir = 'zulu-skill'; Name = 'zulu-skill'; Category = 'Feature' },
            @{ Dir = 'alpha-skill'; Name = 'alpha-skill'; Category = 'Core' },
            @{ Dir = 'mid-skill'; Name = 'mid-skill'; Category = 'Workflow' },
            @{ Dir = 'uncat-skill'; Name = 'uncat-skill'; Category = $null }
        )
        foreach ($skill in $skills) {
            $frontmatter = @(
                '---',
                "name: $($skill.Name)",
                "description: $($skill.Dir) description for testing. Use when handling $($skill.Dir)."
            )
            if ($null -ne $skill.Category) {
                $frontmatter += 'metadata:'
                $frontmatter += "  category: $($skill.Category)"
            }
            $frontmatter += @('---', '', "# Skill: $($skill.Dir)", '', 'Body.')
            Write-FixtureFile -Root $root -RelativePath (
                ".llm/skills/$($skill.Dir)/SKILL.md"
            ) -Content (($frontmatter -join "`n") + "`n")
        }
        & $generateScript -Root $root *> $null
        Assert-ExitCode 0 'generation should succeed'
        $indexPath = Get-FixturePath -Root $root -RelativePath '.llm/skills/index.md'
        Assert-True (Test-Path -LiteralPath $indexPath) 'index.md should be created'
        $content = [System.IO.File]::ReadAllText($indexPath)
        Assert-SectionOrder $content
        Assert-True ($content -match '# Skills Index') 'index should have an H1'
        Assert-True ($content -match '\[alpha-skill\]\(\./alpha-skill/SKILL\.md\)') (
            'core table should link alpha-skill with SKILL.md path'
        )
        Assert-True ($content -match 'generated') 'index should carry a generated-file banner'
        $coreSection = $content.Substring(
            $content.IndexOf('## Core Skills'),
            $content.IndexOf('## Workflow Skills') - $content.IndexOf('## Core Skills')
        )
        Assert-True ($coreSection -match 'alpha-skill') 'alpha-skill should be listed under Core'
        Assert-True ($coreSection -notmatch 'zulu-skill') 'zulu-skill should not be listed under Core'
        $featureSection = $content.Substring($content.IndexOf('## Feature Skills'))
        Assert-True ($featureSection -match 'zulu-skill') 'zulu-skill should be listed under Feature'
        Assert-True ($featureSection -match 'uncat-skill') (
            'uncategorized skill should default to Feature'
        )
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_NameMismatchesDirectory' {
    $root = New-TempRoot
    try {
        $skill = @(
            '---',
            'name: other-name',
            'description: Mismatched fixture skill. Use when testing validation.',
            'metadata:',
            '  category: Core',
            '---',
            '',
            '# Skill: Other',
            '',
            'Body.'
        ) -join "`n"
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/foo/SKILL.md' -Content $skill
        $output = & $generateScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'name/directory mismatch should fail'
        Assert-True ($output -match 'name') "output should explain the mismatch, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_FrontmatterMissing' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/foo/SKILL.md' -Content (
            "# Skill: Foo`n`nNo frontmatter here.`n"
        )
        & $generateScript -Root $root *> $null
        Assert-ExitCode 1 'missing frontmatter should fail'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_DescriptionMissing' {
    $root = New-TempRoot
    try {
        $skill = @('---', 'name: foo', '---', '', '# Skill: Foo', '', 'Body.') -join "`n"
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/foo/SKILL.md' -Content $skill
        & $generateScript -Root $root *> $null
        Assert-ExitCode 1 'missing description should fail'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_CategoryUnknown' {
    $root = New-TempRoot
    try {
        $skill = @(
            '---',
            'name: foo',
            'description: Unknown category fixture. Use when testing validation.',
            'metadata:',
            '  category: Invalid',
            '---',
            '',
            '# Skill: Foo',
            '',
            'Body.'
        ) -join "`n"
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/foo/SKILL.md' -Content $skill
        & $generateScript -Root $root *> $null
        Assert-ExitCode 1 'unknown category should fail'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_NoSkillsFound' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content 'placeholder'
        $output = & $generateScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'empty skills directory should fail'
        Assert-True ($output -match 'no skills') "output should explain the empty state, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Output_IsDeterministic_And_EncodedWithoutBom_WithLf' {
    $root = New-TempRoot
    try {
        $skill = @(
            '---',
            'name: alpha-skill',
            'description: Determinism fixture skill. Use when testing output stability.',
            'metadata:',
            '  category: Core',
            '---',
            '',
            '# Skill: Alpha',
            '',
            'Body.'
        ) -join "`n"
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/alpha-skill/SKILL.md' -Content $skill
        & $generateScript -Root $root *> $null
        Assert-ExitCode 0 'first run should succeed'
        $indexPath = Get-FixturePath -Root $root -RelativePath '.llm/skills/index.md'
        $first = [System.IO.File]::ReadAllBytes($indexPath)
        Start-Sleep -Milliseconds 20
        & $generateScript -Root $root *> $null
        Assert-ExitCode 0 'second run should succeed'
        $second = [System.IO.File]::ReadAllBytes($indexPath)
        Assert-True ($first.Length -gt 0) 'index should not be empty'
        Assert-True (
            ($first.Length -eq $second.Length) -and (
                [System.Linq.Enumerable]::SequenceEqual($first, $second)
            )
        ) 'index bytes should be identical across runs'
        Assert-True (
            -not ($first[0] -eq 0xEF -and $first[1] -eq 0xBB -and $first[2] -eq 0xBF)
        ) 'index should not start with a BOM'
        $text = [System.Text.UTF8Encoding]::new($false).GetString($first)
        Assert-True (-not ($text.Contains("`r"))) 'index should use LF endings only'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_SkillNameExceeds64Chars' {
    $root = New-TempRoot
    try {
        $longName = 'a' * 65
        $skill = @(
            '---',
            "name: $longName",
            'description: Long-name fixture skill. Use when testing validation.',
            '---',
            '',
            '# Skill: Long',
            '',
            'Body.'
        ) -join "`n"
        Write-FixtureFile -Root $root -RelativePath ".llm/skills/$longName/SKILL.md" -Content $skill
        & $generateScript -Root $root *> $null
        Assert-ExitCode 1 'names over 64 characters should fail'
    } finally {
        Remove-TempRoot $root
    }
}

Write-Host "== generate-skills-index: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
