Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$prepareScript = Join-Path $scriptsDirectory 'release/prepare-release.mjs'

Write-Host '== prepare-release self-tests =='

function New-ReleaseFixture {
    param(
        [string]$Version = '0.0.37',
        [string]$Changelog
    )
    $root = New-TempRoot -Prefix 'release-fixture-'
    Write-FixtureFile -Root $root -RelativePath 'package.json' -Content (
        "{`n  `"name`": `"com.test.fixture`",`n  `"version`": `"$Version`"`n}`n"
    )
    Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content (
        "- **Package**: com.test.fixture`n- **Version**: $Version`n"
    )
    if (-not $Changelog) {
        $Changelog = @(
            '# Changelog',
            '',
            'All notable changes to this package are documented in this file.',
            '',
            '## [Unreleased]',
            '',
            '### Added',
            '',
            '- Initial changelog.',
            '',
            '## [0.0.36] - 2026-07-07',
            '',
            '### Fixed',
            '',
            '- Old entry.'
        ) -join "`n"
        $Changelog += "`n"
    }
    Write-FixtureFile -Root $root -RelativePath 'CHANGELOG.md' -Content $Changelog
    return $root
}

function Invoke-Prepare {
    param([string]$Root, [string[]]$Arguments)
    return (& node $prepareScript --root $Root @Arguments *>&1 | Out-String), $LASTEXITCODE
}

function Get-FileHashText {
    param([string]$Root)
    return (Get-FileHash -Path @(
        (Join-Path $Root 'package.json'),
        (Join-Path $Root 'CHANGELOG.md'),
        (Join-Path $Root '.llm/context.md')
    ) -Algorithm MD5 | ForEach-Object { $_.Hash }) -join ','
}

Invoke-TestCase 'Bumps_EachBumpKind_AndSyncsEveryFile' {
    $cases = @(
        @{ Bump = 'patch'; Start = '0.0.37'; Expected = '0.0.38' },
        @{ Bump = 'minor'; Start = '0.0.37'; Expected = '0.1.0' },
        @{ Bump = 'major'; Start = '0.0.37'; Expected = '1.0.0' }
    )
    foreach ($case in $cases) {
        $root = New-ReleaseFixture -Version $case.Start
        try {
            $output, $exitCode = Invoke-Prepare -Root $root -Arguments @('--bump', $case.Bump)
            Assert-ExitCode 0 "bump $($case.Bump) should succeed"
            $package = Get-Content (Join-Path $root 'package.json') -Raw | ConvertFrom-Json
            Assert-True ($package.version -eq $case.Expected) (
                "package.json should carry $($case.Expected), got $($package.version)")
            $context = Get-Content (Join-Path $root '.llm/context.md') -Raw
            Assert-True ($context -match [regex]::Escape("**Version**: $($case.Expected)")) (
                "context.md should carry **Version**: $($case.Expected), got: $context")
            Assert-True ($output -match "previous-version: $($case.Start)") (
                "output should report the previous version, got: $output")
            Assert-True ($output -match "next-version: $($case.Expected)") (
                "output should report the next version, got: $output")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Applies_ExplicitPrereleaseVersion' {
    $root = New-ReleaseFixture
    try {
        $output, $exitCode = Invoke-Prepare -Root $root -Arguments @('--version', '0.0.38-rc.1')
        Assert-ExitCode 0 'explicit prerelease version should succeed'
        $package = Get-Content (Join-Path $root 'package.json') -Raw | ConvertFrom-Json
        Assert-True ($package.version -eq '0.0.38-rc.1') (
            "package.json should carry 0.0.38-rc.1, got $($package.version)")
        $context = Get-Content (Join-Path $root '.llm/context.md') -Raw
        Assert-True ($context -match [regex]::Escape('**Version**: 0.0.38-rc.1')) (
            "context.md should carry **Version**: 0.0.38-rc.1, got: $context")
        $changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
        Assert-True ($changelog -match [regex]::Escape('## [0.0.38-rc.1] - ')) (
            "changelog should gain the prerelease section, got: $changelog")
        Assert-True ($output -match [regex]::Escape('next-version: 0.0.38-rc.1')) (
            "output should report the prerelease version, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Rotates_Unreleased_IntoDatedSection_And_PreservesSurroundingContent' {
    $changelog = @(
        '# Changelog',
        '',
        'All notable changes to this package are documented in this file.',
        '',
        '## [Unreleased]',
        '',
        '### Added',
        '',
        '- First entry.',
        '- Second entry.',
        '',
        '## [0.0.36] - 2026-07-07',
        '',
        '### Fixed',
        '',
        '- Old entry.',
        '',
        '[keepachangelog]: https://keepachangelog.com/en/1.1.0/'
    ) -join "`n"
    $root = New-ReleaseFixture -Changelog ($changelog + "`n")
    $dateBefore = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
    try {
        $output, $exitCode = Invoke-Prepare -Root $root -Arguments @('--bump', 'patch')
        Assert-ExitCode 0 'rotation should succeed'
        $dateAfter = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
        $changelogResult = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
        $expectedHeadings = @(
            '## [0.0.38] - ', $dateBefore, $dateAfter,
            '## [Unreleased]',
            '### Added',
            '- First entry.',
            '- Second entry.',
            '## [0.0.36] - 2026-07-07',
            '[keepachangelog]: https://keepachangelog.com/en/1.1.0/'
        )
        foreach ($fragment in $expectedHeadings) {
            Assert-True ($changelogResult -match [regex]::Escape($fragment)) (
                "rotated changelog should contain '$fragment', got: $changelogResult")
        }
        $unreleasedIndex = $changelogResult.IndexOf('## [Unreleased]')
        $releasedIndex = $changelogResult.IndexOf('## [0.0.38] - ')
        $previousIndex = $changelogResult.IndexOf('## [0.0.36] - ')
        Assert-True ($unreleasedIndex -lt $releasedIndex -and $releasedIndex -lt $previousIndex) (
            "Unreleased must precede the new section which precedes older sections, got: $changelogResult")
        $tail = $changelogResult.Substring($releasedIndex)
        Assert-True ($tail -match [regex]::Escape("- Second entry.`n`n## [0.0.36]")) (
            "the new section body must be separated from the next heading by a blank line, got: $changelogResult")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_UnreleasedSection_IsMissing' {
    $changelog = @('# Changelog', '', '## [0.0.36] - 2026-07-07', '', '- Old entry.') -join "`n"
    $root = New-ReleaseFixture -Changelog $changelog
    try {
        $before = Get-FileHashText -Root $root
        $output, $exitCode = Invoke-Prepare -Root $root -Arguments @('--bump', 'patch')
        Assert-ExitCode 1 'missing Unreleased should fail'
        Assert-True ($output -match 'Unreleased') "error should name Unreleased, got: $output"
        Assert-True ((Get-FileHashText -Root $root) -eq $before) 'a failed run must not mutate files'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_UnreleasedSection_HasNoNotes' {
    $changelog = @(
        '# Changelog',
        '',
        '## [Unreleased]',
        '',
        '### Added',
        '',
        '## [0.0.36] - 2026-07-07'
    ) -join "`n"
    $root = New-ReleaseFixture -Changelog $changelog
    try {
        $before = Get-FileHashText -Root $root
        $output, $exitCode = Invoke-Prepare -Root $root -Arguments @('--bump', 'patch')
        Assert-ExitCode 1 'empty Unreleased should fail'
        Assert-True ($output -match 'no release notes') (
            "error should explain the missing notes, got: $output")
        Assert-True ((Get-FileHashText -Root $root) -eq $before) 'a failed run must not mutate files'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_TargetSectionAlreadyExists' {
    $changelog = @(
        '# Changelog',
        '',
        '## [Unreleased]',
        '',
        '- Entry.',
        '',
        '## [0.0.38] - 2026-09-15',
        '',
        '- Already released.'
    ) -join "`n"
    $root = New-ReleaseFixture -Changelog $changelog
    try {
        $before = Get-FileHashText -Root $root
        $output, $exitCode = Invoke-Prepare -Root $root -Arguments @('--bump', 'patch')
        Assert-ExitCode 1 'duplicate target section should fail'
        Assert-True ($output -match [regex]::Escape('## [0.0.38]')) (
            "error should name the duplicate section, got: $output")
        Assert-True ((Get-FileHashText -Root $root) -eq $before) 'a failed run must not mutate files'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_ExplicitVersion_NoOps' {
    $root = New-ReleaseFixture
    try {
        $before = Get-FileHashText -Root $root
        $output, $exitCode = Invoke-Prepare -Root $root -Arguments @('--version', '0.0.37')
        Assert-ExitCode 1 'no-op explicit version should fail'
        Assert-True ($output -match 'equals the current version') (
            "error should explain the no-op, got: $output")
        Assert-True ((Get-FileHashText -Root $root) -eq $before) 'a failed run must not mutate files'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Rejects_InvalidArguments' {
    $cases = @(
        @{ Name = 'unknown bump word'; Arguments = @('--bump', 'beta') },
        @{ Name = 'v-prefixed version'; Arguments = @('--version', 'v0.0.38') },
        @{ Name = 'incomplete semver'; Arguments = @('--version', '0.0') },
        @{ Name = 'unsupported prerelease'; Arguments = @('--version', '0.0.38-build.2') },
        @{ Name = 'both bump and version'; Arguments = @('--bump', 'patch', '--version', '0.0.38') },
        @{ Name = 'neither bump nor version'; Arguments = @() }
    )
    foreach ($case in $cases) {
        $root = New-ReleaseFixture
        try {
            $before = Get-FileHashText -Root $root
            $output, $exitCode = Invoke-Prepare -Root $root -Arguments $case.Arguments
            Assert-ExitCode 1 "invalid arguments ($($case.Name)) should fail"
            Assert-True ($output -match 'error: ') (
                "output should carry the error prefix ($($case.Name)), got: $output")
            Assert-True ((Get-FileHashText -Root $root) -eq $before) (
                "a rejected run must not mutate files ($($case.Name))")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'DryRun_ReportsThePlan_WithoutMutating' {
    $root = New-ReleaseFixture
    try {
        $before = Get-FileHashText -Root $root
        $output, $exitCode = Invoke-Prepare -Root $root -Arguments @('--bump', 'patch', '--dry-run')
        Assert-ExitCode 0 'dry-run should succeed'
        Assert-True ($output -match [regex]::Escape('0.0.37 -> 0.0.38')) (
            "dry-run should report the planned bump, got: $output")
        Assert-True ((Get-FileHashText -Root $root) -eq $before) 'dry-run must not mutate files'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_PackageJson_IsMissingVersion' {
    $root = New-ReleaseFixture
    try {
        Write-FixtureFile -Root $root -RelativePath 'package.json' -Content "{`n  `"name`": `"com.test.fixture`"`n}`n"
        $output, $exitCode = Invoke-Prepare -Root $root -Arguments @('--bump', 'patch')
        Assert-ExitCode 1 'missing version field should fail'
        Assert-True ($output -match 'missing a version field') (
            "error should name the missing field, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Write-Host "== prepare-release: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
