Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$extractScript = Join-Path $scriptsDirectory 'release/extract-release-notes.mjs'

Write-Host '== extract-release-notes self-tests =='

function New-NotesFixture {
    param([string]$Changelog)
    $root = New-TempRoot -Prefix 'notes-fixture-'
    Write-FixtureFile -Root $root -RelativePath 'CHANGELOG.md' -Content $Changelog
    return $root
}

function Invoke-Extract {
    param([string]$Root, [string[]]$Arguments)
    return (& node $extractScript --root $Root @Arguments *>&1 | Out-String), $LASTEXITCODE
}

$validChangelog = @"
# Changelog

## [Unreleased]

### Added

- Pending work.

## [0.0.38] - 2026-10-02

### Added

- The released feature.

### Fixed

- A repair.

## [0.0.37] - 2026-09-10

### Changed

- Older entry.
"@

Invoke-TestCase 'Extracts_ExactVersionBody' {
    $root = New-NotesFixture -Changelog $validChangelog
    try {
        $output, $exitCode = Invoke-Extract -Root $root -Arguments @('--version', '0.0.38')
        Assert-ExitCode 0 'a present version section should extract'
        Assert-True ($output -match 'The released feature\.') "should include the added bullet, got: $output"
        Assert-True ($output -match 'A repair\.') "should include the fixed bullet, got: $output"
        Assert-True (-not ($output -match 'Older entry\.')) "should stop at the next section, got: $output"
        Assert-True (-not ($output -match 'Unreleased')) "should not bleed into Unreleased, got: $output"
    } finally {
        Remove-TempRoot -Path $root
    }
}

Invoke-TestCase 'FailsClosed_OnMissingSection' {
    $root = New-NotesFixture -Changelog $validChangelog
    try {
        $output, $exitCode = Invoke-Extract -Root $root -Arguments @('--version', '9.9.9')
        Assert-ExitCode 1 'a missing section should fail'
        Assert-True ($output -match "no '## \[9\.9\.9\]' section") "should name the missing section, got: $output"
    } finally {
        Remove-TempRoot -Path $root
    }
}

Invoke-TestCase 'FailsClosed_OnDuplicateSection' {
    $root = New-NotesFixture -Changelog ($validChangelog + "`n## [0.0.38] - 2026-10-03`n`nDuplicate.`n")
    try {
        $output, $exitCode = Invoke-Extract -Root $root -Arguments @('--version', '0.0.38')
        Assert-ExitCode 1 'a duplicated section should fail'
        Assert-True ($output -match '2 .## \[0\.0\.38\]. sections') "should report the duplicate count, got: $output"
    } finally {
        Remove-TempRoot -Path $root
    }
}

Invoke-TestCase 'FailsClosed_OnEmptySection' {
    $root = New-NotesFixture -Changelog "## [0.0.38] - 2026-10-02`n`n## [0.0.37] - 2026-09-10`n"
    try {
        $output, $exitCode = Invoke-Extract -Root $root -Arguments @('--version', '0.0.38')
        Assert-ExitCode 1 'an empty section should fail'
        Assert-True ($output -match 'is empty') "should report the empty section, got: $output"
    } finally {
        Remove-TempRoot -Path $root
    }
}

Invoke-TestCase 'Rejects_LeadingV' {
    $root = New-NotesFixture -Changelog $validChangelog
    try {
        $output, $exitCode = Invoke-Extract -Root $root -Arguments @('--version', 'v0.0.38')
        Assert-ExitCode 1 'a v-prefixed version should fail'
        Assert-True ($output -match 'bare package version') "should explain the expected shape, got: $output"
    } finally {
        Remove-TempRoot -Path $root
    }
}

Invoke-TestCase 'WritesNotesToOutputFile' {
    $root = New-NotesFixture -Changelog $validChangelog
    try {
        $output, $exitCode = Invoke-Extract -Root $root -Arguments @('--version', '0.0.38', '--output', 'release-notes.md')
        Assert-ExitCode 0 'an output path should write successfully'
        $notesPath = Join-Path $root 'release-notes.md'
        Assert-True (Test-Path -LiteralPath $notesPath) 'the notes file should exist'
        $content = Get-Content -LiteralPath $notesPath -Raw
        Assert-True ($content -match 'The released feature\.') "the notes file should carry the body, got: $content"
    } finally {
        Remove-TempRoot -Path $root
    }
}

exit $script:TestFailureCount
