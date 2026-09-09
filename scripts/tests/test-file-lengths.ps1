Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$lintScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'lint-file-lengths.ps1'
Write-Host '== lint-file-lengths self-tests =='

Invoke-TestCase 'Passes_When_FileUnderLimit' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content (Get-FixtureLines 10)
        & $lintScript -Root $root *> $null
        Assert-ExitCode 0 'short file should pass'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_FileExceedsHardLimit' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/oversize.md' -Content (Get-FixtureLines 350)
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'oversize file should fail'
        Assert-True ($output -match 'oversize\.md') "output should name the offending file, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_StagedFileOverLimit' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content (Get-FixtureLines 301)
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 '301-line file should fail'
        Assert-True ($output -match 'file-length') "output should carry [file-length] prefix, got: $output"
        Assert-True ($output -match '301') "output should mention the offending line count, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Passes_When_FileAtExactLimit' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content (Get-FixtureLines 300)
        & $lintScript -Root $root *> $null
        Assert-ExitCode 0 '300-line file should pass'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Warns_When_FileNearLimit_WithVerboseOutput' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content (Get-FixtureLines 290)
        $output = & $lintScript -Root $root -VerboseOutput *>&1 | Out-String
        Assert-ExitCode 0 '290-line file should pass'
        Assert-True ($output -match 'near') "output should warn near limit, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Hides_NearLimitWarning_WithoutVerboseOutput' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content (Get-FixtureLines 290)
        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 0 '290-line file should pass'
        Assert-True (-not ($output -match 'near')) "output should be silent without -VerboseOutput, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Ignores_GeneratedSkillsIndex' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/index.md' -Content (Get-FixtureLines 400)
        & $lintScript -Root $root *> $null
        Assert-ExitCode 0 'generated index should be excluded'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Checks_SkillFiles_InNestedDirectories' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/big-skill/SKILL.md' -Content (
            Get-FixtureLines 350
        )
        & $lintScript -Root $root *> $null
        Assert-ExitCode 1 'oversize SKILL.md should fail'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Scopes_ToPaths_WhenProvided' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content (Get-FixtureLines 400)
        Write-FixtureFile -Root $root -RelativePath '.llm/skills/ok/SKILL.md' -Content (
            Get-FixtureLines 10
        )
        & $lintScript -Root $root -Paths @('.llm/skills/ok/SKILL.md') *> $null
        Assert-ExitCode 0 'scoped run should only check the named file'
        $output = & $lintScript -Root $root -Paths @('.llm/context.md') *>&1 | Out-String
        Assert-ExitCode 1 'scoped run should catch the named bad file'
        Assert-True ($output -match 'context\.md') "output should name the bad file, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_PathEscapesLlmDirectory' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath 'README.md' -Content (Get-FixtureLines 400)
        $output = & $lintScript -Root $root -Paths @('README.md') *>&1 | Out-String
        Assert-ExitCode 1 'paths outside .llm should be rejected'
        Assert-True ($output -match 'outside') "output should explain rejection, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Counts_Lines_WithCrlfEndings' {
    $root = New-TempRoot
    try {
        $content = (Get-FixtureLines 305) -replace "`n", "`r`n"
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content $content
        & $lintScript -Root $root *> $null
        Assert-ExitCode 1 'CRLF file should be counted correctly'
    } finally {
        Remove-TempRoot $root
    }
}

Write-Host "== lint-file-lengths: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
