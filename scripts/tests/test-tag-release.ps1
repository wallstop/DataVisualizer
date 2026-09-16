Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$tagScript = Join-Path $scriptsDirectory 'release/tag-release.mjs'

Write-Host '== tag-release self-tests =='

function New-TagFixture {
    param(
        [string]$Version = '0.0.38',
        [string]$Changelog,
        [switch]$NoGit,
        [switch]$SkipCommit
    )
    $root = New-TempRoot -Prefix 'tag-fixture-'
    if (-not $NoGit) {
        & git -C $root init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'git init failed for fixture' }
        & git -C $root config user.name 'Fixture Tester'
        & git -C $root config user.email 'fixture@example.invalid'
    }
    Write-FixtureFile -Root $root -RelativePath 'package.json' -Content (
        "{`n  `"name`": `"com.test.fixture`",`n  `"version`": `"$Version`"`n}`n"
    )
    if (-not $Changelog) {
        $Changelog = @(
            '# Changelog',
            '',
            'All notable changes to this package are documented in this file.',
            '',
            '## [Unreleased]',
            '',
            '- Next up.',
            '',
            "## [$Version] - 2026-09-16",
            '',
            '### Added',
            '',
            '- Initial changelog.'
        ) -join "`n"
        $Changelog += "`n"
    }
    Write-FixtureFile -Root $root -RelativePath 'CHANGELOG.md' -Content $Changelog
    if (-not $NoGit -and -not $SkipCommit) {
        & git -C $root add -A
        & git -C $root commit --quiet -m 'Prepare release'
        if ($LASTEXITCODE -ne 0) { throw 'git commit failed for fixture' }
    }
    return $root
}

function Invoke-Tag {
    param([string]$Root, [string[]]$Arguments)
    return (& node $tagScript --root $Root @Arguments *>&1 | Out-String), $LASTEXITCODE
}

function Get-TagNames {
    param([string]$Root)
    return @(git -C $root tag)
}

function Get-TagTarget {
    param([string]$Root, [string]$Tag)
    return (git -C $root rev-parse "$Tag^{commit}").Trim()
}

function Get-HeadSha {
    param([string]$Root)
    return (git -C $root rev-parse HEAD).Trim()
}

function Get-TagMessage {
    param([string]$Root, [string]$Tag)
    return (git -C $root tag -l --format '%(contents)' $Tag).Trim()
}

function Get-FixtureHashText {
    param([string]$Root)
    return (Get-FileHash -Path @(
        (Join-Path $Root 'package.json'),
        (Join-Path $Root 'CHANGELOG.md')
    ) -Algorithm MD5 | ForEach-Object { $_.Hash }) -join ','
}

Invoke-TestCase 'Creates_AnnotatedTag_AtHead_OnTheMergedReleaseCommit' {
    $root = New-TagFixture
    try {
        $head = Get-HeadSha -Root $root
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @('--branch', 'release/v0.0.38')
        Assert-ExitCode 0 'tagging a matching release tree should succeed'
        $tags = @(Get-TagNames -Root $root)
        Assert-True ($tags.Count -eq 1 -and $tags[0] -eq 'v0.0.38') (
            "exactly one tag v0.0.38 should exist, got: $($tags -join ', ')")
        Assert-True ((git -C $root cat-file -t v0.0.38).Trim() -eq 'tag') (
            'the created tag must be annotated, not lightweight')
        Assert-True ((Get-TagTarget -Root $root -Tag 'v0.0.38') -eq $head) (
            "the tag must point at HEAD ($head), got: $(Get-TagTarget -Root $root -Tag 'v0.0.38')")
        Assert-True ((Get-TagMessage -Root $root -Tag 'v0.0.38') -eq 'Release v0.0.38') (
            "the tag message should be 'Release v0.0.38', got: $(Get-TagMessage -Root $root -Tag 'v0.0.38')")
        Assert-True ($output -match 'version: 0.0.38') "output should report the version, got: $output"
        Assert-True ($output -match 'tag: v0.0.38') "output should report the tag, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Tags_TheRequestedCommit_WhenCommitArgumentIsGiven' {
    $root = New-TagFixture
    try {
        $firstSha = Get-HeadSha -Root $root
        Write-FixtureFile -Root $root -RelativePath 'docs-note.txt' -Content 'later commit'
        & git -C $root add -A
        & git -C $root commit --quiet -m 'Later commit'
        Assert-True ((Get-HeadSha -Root $root) -ne $firstSha) 'the fixture should have a second commit'
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @(
            '--branch', 'release/v0.0.38', '--commit', $firstSha)
        Assert-ExitCode 0 'tagging an explicit commit should succeed'
        Assert-True ((Get-TagTarget -Root $root -Tag 'v0.0.38') -eq $firstSha) (
            "the tag must point at the requested commit ($firstSha), got: $(Get-TagTarget -Root $root -Tag 'v0.0.38')")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Supports_PrereleaseVersions' {
    $root = New-TagFixture -Version '0.0.38-rc.1'
    try {
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @('--branch', 'release/v0.0.38-rc.1')
        Assert-ExitCode 0 'prerelease tagging should succeed'
        $tags = @(Get-TagNames -Root $root)
        Assert-True ($tags.Count -eq 1 -and $tags[0] -eq 'v0.0.38-rc.1') (
            "exactly one tag v0.0.38-rc.1 should exist, got: $($tags -join ', ')")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'DryRun_Verifies_WithoutCreatingATag' {
    $root = New-TagFixture
    try {
        $before = Get-FixtureHashText -Root $root
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @('--branch', 'release/v0.0.38', '--dry-run')
        Assert-ExitCode 0 'dry-run should succeed'
        Assert-True ($output -match 'dry-run') "output should carry the dry-run marker, got: $output"
        Assert-True ((@(Get-TagNames -Root $root)).Count -eq 0) 'dry-run must not create a tag'
        Assert-True ((Get-FixtureHashText -Root $root) -eq $before) 'dry-run must not mutate files'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_BranchDoesNotMatchPackageVersion' {
    $cases = @(
        @{ Name = 'older version branch'; Arguments = @('--branch', 'release/v0.0.37') },
        @{ Name = 'newer version branch'; Arguments = @('--branch', 'release/v0.0.39') },
        @{ Name = 'bare version branch'; Arguments = @('--branch', '0.0.38') },
        @{ Name = 'main branch'; Arguments = @('--branch', 'main') }
    )
    foreach ($case in $cases) {
        $root = New-TagFixture
        try {
            $before = Get-FixtureHashText -Root $root
            $output, $exitCode = Invoke-Tag -Root $root -Arguments $case.Arguments
            Assert-ExitCode 1 "branch mismatch ($($case.Name)) should fail"
            Assert-True ($output -match 'does not match the release branch') (
                "error should explain the branch mismatch ($($case.Name)), got: $output")
            Assert-True ((@(Get-TagNames -Root $root)).Count -eq 0) (
                "a rejected run must not create a tag ($($case.Name))")
            Assert-True ((Get-FixtureHashText -Root $root) -eq $before) (
                "a rejected run must not mutate files ($($case.Name))")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_ChangelogSection_IsMissing' {
    $changelog = @(
        '# Changelog',
        '',
        '## [Unreleased]',
        '',
        '- Entry.'
    ) -join "`n"
    $root = New-TagFixture -Changelog $changelog
    try {
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @('--branch', 'release/v0.0.38')
        Assert-ExitCode 1 'a missing version section should fail'
        Assert-True ($output -match [regex]::Escape('## [0.0.38] - YYYY-MM-DD')) (
            "error should name the missing section, got: $output")
        Assert-True ((@(Get-TagNames -Root $root)).Count -eq 0) 'a rejected run must not create a tag'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_ChangelogSection_IsDuplicated' {
    $changelog = @(
        '# Changelog',
        '',
        '## [0.0.38] - 2026-09-15',
        '',
        '- First section.',
        '',
        '## [0.0.38] - 2026-09-16',
        '',
        '- Second section.'
    ) -join "`n"
    $root = New-TagFixture -Changelog $changelog
    try {
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @('--branch', 'release/v0.0.38')
        Assert-ExitCode 1 'a duplicated version section should fail'
        Assert-True ($output -match 'exactly one is required') (
            "error should explain the duplicate sections, got: $output")
        Assert-True ((@(Get-TagNames -Root $root)).Count -eq 0) 'a rejected run must not create a tag'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_ChangelogSection_HasNoNotes' {
    $changelog = @(
        '# Changelog',
        '',
        '## [0.0.38] - 2026-09-16',
        '',
        '## [0.0.37] - 2026-08-01',
        '',
        '- Old entry.'
    ) -join "`n"
    $root = New-TagFixture -Changelog $changelog
    try {
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @('--branch', 'release/v0.0.38')
        Assert-ExitCode 1 'an empty version section should fail'
        Assert-True ($output -match 'no release notes') (
            "error should explain the missing notes, got: $output")
        Assert-True ((@(Get-TagNames -Root $root)).Count -eq 0) 'a rejected run must not create a tag'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_TagAlreadyExists_AndNeverRetargets' {
    $root = New-TagFixture
    try {
        $expectedTarget = Get-HeadSha -Root $root
        & git -C $root tag -a v0.0.38 -m 'Pre-existing tag' HEAD
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @('--branch', 'release/v0.0.38')
        Assert-ExitCode 1 'an existing tag should fail closed'
        Assert-True ($output -match 'already exists') (
            "error should name the existing tag, got: $output")
        $tags = @(Get-TagNames -Root $root)
        Assert-True ($tags.Count -eq 1) "no second tag may be created, got: $($tags -join ', ')"
        Assert-True ((Get-TagMessage -Root $root -Tag 'v0.0.38') -eq 'Pre-existing tag') (
            'the pre-existing tag must not be retargeted or rewritten')
        Assert-True ((Get-TagTarget -Root $root -Tag 'v0.0.38') -eq $expectedTarget) (
            'the pre-existing tag must keep its target')
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_WorkingTreeIsDirty' {
    $root = New-TagFixture
    try {
        Write-FixtureFile -Root $root -RelativePath 'uncommitted-note.txt' -Content 'untracked'
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @('--branch', 'release/v0.0.38')
        Assert-ExitCode 1 'a dirty working tree should fail closed'
        Assert-True ($output -match 'working tree is dirty') (
            "error should explain the dirty tree, got: $output")
        Assert-True ((@(Get-TagNames -Root $root)).Count -eq 0) 'a rejected run must not create a tag'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Rejects_InvalidArguments' {
    $cases = @(
        @{ Name = 'missing branch'; Arguments = @() },
        @{ Name = 'unknown flag'; Arguments = @('--branch', 'release/v0.0.38', '--bump', 'patch') },
        @{ Name = 'flag without value'; Arguments = @('--branch') }
    )
    foreach ($case in $cases) {
        $root = New-TagFixture
        try {
            $output, $exitCode = Invoke-Tag -Root $root -Arguments $case.Arguments
            Assert-ExitCode 1 "invalid arguments ($($case.Name)) should fail"
            Assert-True ($output -match 'error: ') (
                "output should carry the error prefix ($($case.Name)), got: $output")
            Assert-True ((@(Get-TagNames -Root $root)).Count -eq 0) (
                "a rejected run must not create a tag ($($case.Name))")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_PackageVersion_IsUnsupportedSemver' {
    $root = New-TagFixture
    try {
        Write-FixtureFile -Root $root -RelativePath 'package.json' -Content (
            "{`n  `"name`": `"com.test.fixture`",`n  `"version`": `"0.0.38-build.2`"`n}`n"
        )
        & git -C $root add -A
        & git -C $root commit --quiet -m 'Bad version'
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @('--branch', 'release/v0.0.38-build.2')
        Assert-ExitCode 1 'unsupported semver should fail'
        Assert-True ($output -match 'not supported semver') (
            "error should explain the unsupported semver, got: $output")
        Assert-True ((@(Get-TagNames -Root $root)).Count -eq 0) 'a rejected run must not create a tag'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_RequiredFiles_AreMissing' {
    $cases = @(
        @{ Name = 'missing package.json'; RelativePath = 'package.json' },
        @{ Name = 'missing CHANGELOG.md'; RelativePath = 'CHANGELOG.md' }
    )
    foreach ($case in $cases) {
        $root = New-TagFixture
        try {
            Remove-Item -LiteralPath (Join-Path $root $case.RelativePath) -Force
            $output, $exitCode = Invoke-Tag -Root $root -Arguments @('--branch', 'release/v0.0.38')
            Assert-ExitCode 1 "missing required file ($($case.Name)) should fail"
            Assert-True ($output -match 'Cannot read required release file') (
                "error should name the missing file ($($case.Name)), got: $output")
            Assert-True ((@(Get-TagNames -Root $root)).Count -eq 0) (
                "a rejected run must not create a tag ($($case.Name))")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_RootIsNotARepository' {
    $root = New-TagFixture -NoGit
    try {
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @('--branch', 'release/v0.0.38')
        Assert-ExitCode 1 'a non-git root should fail closed'
        Assert-True ($output -match 'not a git work tree') (
            "error should explain the non-git root, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_CommitArgument_DoesNotResolve' {
    $root = New-TagFixture
    try {
        $output, $exitCode = Invoke-Tag -Root $root -Arguments @(
            '--branch', 'release/v0.0.38', '--commit', 'deadbeef')
        Assert-ExitCode 1 'an unresolvable commit should fail'
        Assert-True ($output -match 'Cannot resolve') (
            "error should explain the unresolvable commit, got: $output")
        Assert-True ((@(Get-TagNames -Root $root)).Count -eq 0) 'a rejected run must not create a tag'
    } finally {
        Remove-TempRoot $root
    }
}

Write-Host "== tag-release: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
