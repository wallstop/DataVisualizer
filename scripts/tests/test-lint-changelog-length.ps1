Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$lintScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'lint-changelog-length.js'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Write-Host '== Changelog entry length self-tests =='

function Invoke-ChangelogLengthLint {
    param([string]$ChangelogPath, [string[]]$LintArguments = @())

    $hadOverride = Test-Path Env:CHANGELOG_LENGTH_CHANGELOG
    $previousOverride = $env:CHANGELOG_LENGTH_CHANGELOG
    try {
        $env:CHANGELOG_LENGTH_CHANGELOG = $ChangelogPath
        $output = & node $lintScript @LintArguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        if ($hadOverride) {
            $env:CHANGELOG_LENGTH_CHANGELOG = $previousOverride
        } else {
            Remove-Item Env:CHANGELOG_LENGTH_CHANGELOG -ErrorAction SilentlyContinue
        }
    }
}

# The lint resolves its override against the repository root, so fixtures pass
# absolute paths.
function Invoke-FixtureLint {
    param([string]$Root, [string[]]$LintArguments = @())

    return Invoke-ChangelogLengthLint `
        -ChangelogPath (Join-Path $Root 'CHANGELOG.md') `
        -LintArguments $LintArguments
}

function New-ChangelogFixture {
    param([string]$UnreleasedBody, [string]$ReleasedEntry = 'Released entry that stays unmeasured.')

    $root = New-TempRoot -Prefix 'changelog-length-'
    # A released entry far over the cap sits behind every case: released
    # sections are immutable history and must never be measured.
    $content = @(
        '# Changelog',
        '',
        '## [Unreleased]',
        $UnreleasedBody,
        '## [0.1.0] - 2026-10-07',
        '',
        '### Added',
        '',
        "- $ReleasedEntry",
        ''
    ) -join "`n"
    Write-FixtureFile -Root $root -RelativePath 'CHANGELOG.md' -Content $content
    return $root
}

# The padded entries pin the cap boundary by construction: 'Add ' is four
# characters, so 296 'a' characters render at exactly 300 and 297 at 301.
$atCapEntry = 'Add ' + ('a' * 296)
$overCapEntry = 'Add ' + ('a' * 297)

# Optional columns stay out of the lean table; a missing property reads as its
# default instead of tripping StrictMode.
function Get-CaseValue {
    param([pscustomobject]$Case, [string]$Name, [string]$Default)
    $property = $Case.PSObject.Properties[$Name]
    if ($property) {
        return [string]$property.Value
    }
    return $Default
}

# Each row is one changelog shape and the outcome the guard owes it. The
# verbose patterns pin the rendered lengths by hand count, so the exclusions
# are verified against known values instead of the implementation.
$cases = @(
    [pscustomobject]@{
        Name = 'Passes_WhenAnEntryIsExactlyAtTheCap'
        UnreleasedBody = "- $atCapEntry"
        ExpectPass = $true
        ExpectFailure = ''
        VerbosePattern = 'entry 1: 300'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenAnEntryExceedsTheCapByOne'
        UnreleasedBody = "- $overCapEntry"
        ExpectPass = $false
        ExpectFailure = 'measures 301 rendered characters, over the 300 cap'
        VerbosePattern = ''
    }
    [pscustomobject]@{
        Name = 'Fails_WhenAnOverCapEntryReportsItsRenderedText'
        UnreleasedBody = "- $overCapEntry (#123)."
        ExpectPass = $false
        ExpectFailure = 'over the 300 cap'
        VerbosePattern = ''
        ExtraPattern = 'Add a{297}\.'
    }
    [pscustomobject]@{
        # The parenthesized reference drops together with the space that
        # separates it, so 'save (#148).' renders as 'save.' and counts 40.
        Name = 'ExcludesParenthesizedIssueReferences'
        UnreleasedBody = '- Fix the dirty-state drift on every save (#148).'
        ExpectPass = $true
        ExpectFailure = ''
        VerbosePattern = 'entry 1: 40'
    }
    [pscustomobject]@{
        Name = 'ExcludesBareIssueReferences'
        UnreleasedBody = '- Fix the dirty-state drift reported in #148 today.'
        ExpectPass = $true
        ExpectFailure = ''
        VerbosePattern = 'entry 1: 44'
    }
    [pscustomobject]@{
        # The long target must not count: only the link text 'docs' renders.
        Name = 'ExcludesInlineLinkTargets'
        UnreleasedBody = '- See the [docs](https://example.com/very/long/target/path/that/is/much/longer/than/the/text) page.'
        ExpectPass = $true
        ExpectFailure = ''
        VerbosePattern = 'entry 1: 18'
    }
    [pscustomobject]@{
        # The markers delimit rendered text; their contents count, 38 here.
        Name = 'CountsEmphasisAndCodeMarkersAsRenderedText'
        UnreleasedBody = '- **Add** the `theme` picker and `style` tokens.'
        ExpectPass = $true
        ExpectFailure = ''
        VerbosePattern = 'entry 1: 38'
    }
    [pscustomobject]@{
        # The soft wrap joins into one rendered line: 45 characters, not two
        # entries or a longer count.
        Name = 'JoinsSoftWrappedContinuationLines'
        UnreleasedBody = "- Add the theme picker with`n  hot reload support."
        ExpectPass = $true
        ExpectFailure = ''
        VerbosePattern = 'entry 1: 45'
    }
    [pscustomobject]@{
        # Every Markdown list marker starts a measured entry; a marker the
        # lint did not recognize would sit outside the cap instead of
        # failing it. Dash and star render at 19, digit at 20 ('digit' has
        # one more letter).
        Name = 'MeasuresEveryBulletMarkerShape'
        UnreleasedBody = @(
            '- Add the dash entry.',
            '* Add the star entry.',
            '1. Add the digit entry.'
        ) -join "`n"
        ExpectPass = $true
        ExpectFailure = ''
        VerbosePattern = '3 entry\(ies\)'
        ExtraPattern = 'entry 3: 20'
    }
    [pscustomobject]@{
        # The vacuous-pass shape: an ordered entry as the only content must
        # still be measured and fail, not be ignored.
        Name = 'Fails_WhenAnOrderedEntryIsTheOnlyContent'
        UnreleasedBody = "1. $overCapEntry"
        ExpectPass = $false
        ExpectFailure = 'measures 301 rendered characters, over the 300 cap'
        VerbosePattern = ''
    }
    [pscustomobject]@{
        # Every exclusion together, the shape the shipped entries use.
        Name = 'Passes_WhenARealisticEntryUsesEveryExclusion'
        # Single-quoted so the backticks stay literal in the fixture.
        UnreleasedBody = '- **Add** the `theme` picker: an open window reloads the selected theme' + "`n" + '  immediately ([docs](https://example.com/guide)) (#148).'
        ExpectPass = $true
        ExpectFailure = ''
        VerbosePattern = 'entry 1: 83'
    }
    [pscustomobject]@{
        # Only '[Unreleased]' is measured, so the released over-cap entry
        # stays invisible to the count.
        Name = 'IgnoresReleasedSections'
        UnreleasedBody = "- $atCapEntry"
        ReleasedEntry = 'Released ' + ('b' * 400)
        ExpectPass = $true
        ExpectFailure = ''
        VerbosePattern = '1 entry\(ies\)'
    }
    [pscustomobject]@{
        # The shape prepare-release.mjs leaves right after a rotation.
        Name = 'Passes_WhenUnreleasedIsEmpty'
        UnreleasedBody = ''
        ExpectPass = $true
        ExpectFailure = ''
        VerbosePattern = '0 entry\(ies\)'
    }
    [pscustomobject]@{
        # The report must name the offending entry, not just the file.
        Name = 'Fails_AndNamesTheSubsectionAndEntryNumber'
        UnreleasedBody = @(
            '### Added',
            '',
            '- Add the short first entry.',
            '',
            '### Changed',
            '',
            "- $overCapEntry"
        ) -join "`n"
        ExpectPass = $false
        ExpectFailure = "### Changed entry 2 measures 301 rendered characters"
        VerbosePattern = ''
    }
)

foreach ($script:case in $cases) {
    Invoke-TestCase $script:case.Name {
        $releasedEntry = Get-CaseValue -Case $script:case -Name 'ReleasedEntry' -Default 'Released entry that stays unmeasured.'
        $extraPattern = Get-CaseValue -Case $script:case -Name 'ExtraPattern' -Default ''
        $root = New-ChangelogFixture -UnreleasedBody $script:case.UnreleasedBody -ReleasedEntry $releasedEntry
        try {
            $arguments = @()
            if ($script:case.VerbosePattern -ne '') {
                $arguments = @('--verbose')
            }
            $result = Invoke-FixtureLint -Root $root -LintArguments $arguments
            if ($script:case.ExpectPass) {
                Assert-True ($result.ExitCode -eq 0) "expected the cap to pass: $($result.Output)"
            } else {
                Assert-True ($result.ExitCode -eq 1) "expected the cap to fail: $($result.Output)"
            }
            if ($script:case.ExpectFailure -ne '') {
                Assert-True (
                    $result.Output -match $script:case.ExpectFailure
                ) "expected '$($script:case.ExpectFailure)' in: $($result.Output)"
            }
            if ($script:case.VerbosePattern -ne '') {
                Assert-True (
                    $result.Output -match $script:case.VerbosePattern
                ) "expected '$($script:case.VerbosePattern)' in: $($result.Output)"
            }
            if ($extraPattern -ne '') {
                Assert-True (
                    $result.Output -match $extraPattern
                ) "expected '$($extraPattern)' in: $($result.Output)"
            }
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_WhenUnreleasedIsMissing' {
    $root = New-TempRoot -Prefix 'changelog-length-'
    try {
        $content = @('# Changelog', '', '## [0.1.0] - 2026-10-07', '', '- Released entry.', '') -join "`n"
        Write-FixtureFile -Root $root -RelativePath 'CHANGELOG.md' -Content $content
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "a missing section must fail closed: $($result.Output)"
        Assert-True (
            $result.Output -match "has no '## \[Unreleased\]' section"
        ) "must explain the missing section: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenUnreleasedIsDuplicated' {
    $root = New-TempRoot -Prefix 'changelog-length-'
    try {
        $content = @(
            '# Changelog',
            '',
            '## [Unreleased]',
            '',
            '- Add the first entry.',
            '',
            '## [Unreleased]',
            '',
            '- Add the second entry.',
            ''
        ) -join "`n"
        Write-FixtureFile -Root $root -RelativePath 'CHANGELOG.md' -Content $content
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "a duplicated section must fail closed: $($result.Output)"
        Assert-True (
            $result.Output -match "carries 2 '## \[Unreleased\]' sections"
        ) "must explain the duplicated section: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenTheChangelogIsMissing' {
    $root = New-TempRoot -Prefix 'changelog-length-'
    try {
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "a missing file must fail closed: $($result.Output)"
        Assert-True (
            $result.Output -match 'missing or unreadable'
        ) "must explain the unreadable input: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Passes_AgainstTheRepositoryItself' {
    $result = Invoke-ChangelogLengthLint `
        -ChangelogPath (Join-Path $repoRoot 'CHANGELOG.md') `
        -LintArguments @('--verbose')
    Assert-True ($result.ExitCode -eq 0) "the repository must satisfy its own cap: $($result.Output)"
    Assert-True (
        $result.Output -match 'longest \d+ of 300 characters'
    ) "verbose output must report the longest entry: $($result.Output)"
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "changelog-length self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'changelog-length self-tests: all passed'
exit 0
