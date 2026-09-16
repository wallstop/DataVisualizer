Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$verifyScript = Join-Path $scriptsDirectory 'release/verify-release.mjs'

Write-Host '== verify-release self-tests =='

function New-VerifyFixture {
    param(
        [string]$Version = '0.0.38',
        [string]$PackageJson,
        [switch]$NoGit,
        [switch]$SkipCommit
    )
    $root = New-TempRoot -Prefix 'verify-fixture-'
    if (-not $NoGit) {
        & git -C $root init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'git init failed for fixture' }
        & git -C $root config user.name 'Fixture Tester'
        & git -C $root config user.email 'fixture@example.invalid'
    }
    if (-not $PackageJson) {
        $PackageJson = "{`n" +
            "  `"name`": `"com.test.fixture`",`n" +
            "  `"version`": `"$Version`",`n" +
            "  `"files`": [`n" +
            "    `"Editor`",`n" +
            "    `"Editor.meta`"`n" +
            "  ]`n" +
            "}`n"
    }
    Write-FixtureFile -Root $root -RelativePath 'package.json' -Content $PackageJson
    Write-FixtureFile -Root $root -RelativePath 'Editor/A.cs' -Content '// fixture content'
    Write-FixtureFile -Root $root -RelativePath 'Editor/A.cs.meta' -Content 'meta: A.cs'
    Write-FixtureFile -Root $root -RelativePath 'Editor.meta' -Content 'meta: Editor'
    if (-not $NoGit -and -not $SkipCommit) {
        & git -C $root add -A
        & git -C $root commit --quiet -m 'Prepare release'
        if ($LASTEXITCODE -ne 0) { throw 'git commit failed for fixture' }
    }
    return $root
}

function Invoke-Verify {
    param([string]$Root, [string[]]$Arguments)
    return (& node $verifyScript --root $Root @Arguments *>&1 | Out-String), $LASTEXITCODE
}

function New-HandTarball {
    param([string]$Root, [string]$Name, [string[]]$IncludePaths, [string[]]$ExtraPaths = @())
    $staging = Join-Path $Root 'staging'
    New-Item -ItemType Directory -Path (Join-Path $staging 'package') -Force | Out-Null
    foreach ($relative in $IncludePaths) {
        $source = Join-Path $Root $relative
        $target = Join-Path $staging "package/$relative"
        $directory = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
        Copy-Item -LiteralPath $source -Destination $target
    }
    foreach ($relative in $ExtraPaths) {
        Write-FixtureFile -Root $staging -RelativePath "package/$relative" -Content 'extra'
    }
    $tarball = Join-Path $Root $Name
    tar -czf $tarball -C $staging package
    if ($LASTEXITCODE -ne 0) { throw 'tar failed for handcrafted fixture tarball' }
    return $tarball
}

Invoke-TestCase 'Verifies_MatchingTag_AndReportsPublishOutputs' {
    $root = New-VerifyFixture
    try {
        $output, $exitCode = Invoke-Verify -Root $root -Arguments @('--tag', 'v0.0.38')
        Assert-ExitCode 0 'a matching tag and payload should verify'
        Assert-True ($output -match 'pkg: com\.test\.fixture') "output should report the package name, got: $output"
        Assert-True ($output -match 'version: 0\.0\.38') "output should report the version, got: $output"
        Assert-True ($output -match 'npm_tag: latest') "stable version should resolve to the latest dist-tag, got: $output"
        Assert-True ($output -match 'package_file: com\.test\.fixture-0\.0\.38\.tgz') (
            "output should report the packed tarball, got: $output")
        Assert-True ($output -match 'entries: 4') (
            "output should report all four allowlisted entries, got: $output")
        Assert-True (Test-Path -LiteralPath (Join-Path $root 'com.test.fixture-0.0.38.tgz')) (
            'the packed tarball should exist for the publish step')
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Resolves_Next_DistTag_For_RestrictedPrereleases' {
    $cases = @(
        @{ Version = '0.0.38-rc.1' },
        @{ Version = '0.0.38-alpha' },
        @{ Version = '0.0.39-beta.2' },
        @{ Version = '1.0.0-preview' }
    )
    foreach ($case in $cases) {
        $root = New-VerifyFixture -Version $case.Version
        try {
            $output, $exitCode = Invoke-Verify -Root $root -Arguments @('--tag', "v$($case.Version)")
            Assert-ExitCode 0 "prerelease $($case.Version) should verify"
            Assert-True ($output -match 'npm_tag: next') (
                "prerelease $($case.Version) should resolve to the next dist-tag, got: $output")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Verifies_An_ExplicitPackageFile_WithoutPacking' {
    $root = New-VerifyFixture
    try {
        $output, $exitCode = Invoke-Verify -Root $root -Arguments @('--tag', 'v0.0.38')
        Assert-ExitCode 0 'the initial pack should verify'
        $tarball = Join-Path $root 'com.test.fixture-0.0.38.tgz'
        $before = (Get-Item -LiteralPath $tarball).LastWriteTimeUtc.Ticks
        Start-Sleep -Milliseconds 10
        $output, $exitCode = Invoke-Verify -Root $root -Arguments @(
            '--tag', 'v0.0.38', '--package-file', 'com.test.fixture-0.0.38.tgz')
        Assert-ExitCode 0 'an explicit npm-produced tarball should verify'
        Assert-True ((Get-Item -LiteralPath $tarball).LastWriteTimeUtc.Ticks -eq $before) (
            'verify mode with --package-file must not repack the tree')
        Assert-True ($output -match 'entries: 4') "the tarball entry count should be reported, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_TagDoesNotMatchPackageVersion' {
    $cases = @(
        @{ Name = 'older tag'; Tag = 'v0.0.37' },
        @{ Name = 'newer tag'; Tag = 'v0.0.39' },
        @{ Name = 'bare tag'; Tag = '0.0.38' },
        @{ Name = 'main ref'; Tag = 'main' }
    )
    foreach ($case in $cases) {
        $root = New-VerifyFixture
        try {
            $output, $exitCode = Invoke-Verify -Root $root -Arguments @('--tag', $case.Tag)
            Assert-ExitCode 1 "tag mismatch ($($case.Name)) should fail"
            Assert-True ($output -match 'does not match the package version') (
                "error should explain the tag mismatch ($($case.Name)), got: $output")
            Assert-True ($output -match 'error: ') (
                "output should carry the error prefix ($($case.Name)), got: $output")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_TarballCarriesUnallowlistedFile' {
    $root = New-VerifyFixture
    try {
        $tarball = New-HandTarball -Root $root -Name 'junk.tgz' -IncludePaths @(
            'package.json', 'Editor.meta', 'Editor/A.cs', 'Editor/A.cs.meta'
        ) -ExtraPaths @('Extra.txt')
        $output, $exitCode = Invoke-Verify -Root $root -Arguments @(
            '--tag', 'v0.0.38', '--package-file', $tarball)
        Assert-ExitCode 1 'an unallowlisted tarball entry should fail closed'
        Assert-True ($output -match 'outside the publish allowlist') (
            "error should explain the allowlist violation, got: $output")
        Assert-True ($output -match 'Extra\.txt') "error should name the offending file, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_AllowlistedPayloadFileIsMissing' {
    $root = New-VerifyFixture
    try {
        $tarball = New-HandTarball -Root $root -Name 'shrunk.tgz' -IncludePaths @(
            'package.json', 'Editor/A.cs', 'Editor/A.cs.meta'
        )
        $output, $exitCode = Invoke-Verify -Root $root -Arguments @(
            '--tag', 'v0.0.38', '--package-file', $tarball)
        Assert-ExitCode 1 'a missing allowlisted payload file should fail closed'
        Assert-True ($output -match 'silently shrink') (
            "error should explain the shrunk payload, got: $output")
        Assert-True ($output -match 'Editor\.meta') "error should name the missing file, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_PackageVersionIsUnsupportedSemver' {
    $root = New-VerifyFixture -PackageJson (
        "{`n  `"name`": `"com.test.fixture`",`n  `"version`": `"0.0.38-build.2`",`n" +
        "  `"files`": [`"Editor`"]`n}`n")
    try {
        $output, $exitCode = Invoke-Verify -Root $root -Arguments @('--tag', 'v0.0.38-build.2')
        Assert-ExitCode 1 'unsupported semver should fail'
        Assert-True ($output -match 'not supported semver') (
            "error should explain the unsupported semver, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_PackageJsonIsMissingOrIncomplete' {
    $cases = @(
        @{ Name = 'missing package.json'; PackageJson = $null; Removed = 'package.json'; Pattern = 'Cannot read required release file' },
        @{ Name = 'invalid JSON'; PackageJson = '{not json'; Removed = $null; Pattern = 'not valid JSON' },
        @{ Name = 'missing name'; PackageJson = "{`n  `"version`": `"0.0.38`",`n  `"files`": [`"Editor`"]`n}`n"; Removed = $null; Pattern = 'missing a name field' },
        @{ Name = 'missing version'; PackageJson = "{`n  `"name`": `"com.test.fixture`",`n  `"files`": [`"Editor`"]`n}`n"; Removed = $null; Pattern = 'missing a version field' },
        @{ Name = 'empty files'; PackageJson = "{`n  `"name`": `"com.test.fixture`",`n  `"version`": `"0.0.38`",`n  `"files`": []`n}`n"; Removed = $null; Pattern = "non-empty 'files' publish allowlist" }
    )
    foreach ($case in $cases) {
        $root = New-VerifyFixture -PackageJson $case.PackageJson
        try {
            if ($case.Removed) {
                Remove-Item -LiteralPath (Join-Path $root $case.Removed) -Force
            }
            $output, $exitCode = Invoke-Verify -Root $root -Arguments @('--tag', 'v0.0.38')
            Assert-ExitCode 1 "invalid package.json ($($case.Name)) should fail"
            Assert-True ($output -match [regex]::Escape($case.Pattern)) (
                "error should explain the failure ($($case.Name)) with '$($case.Pattern)', got: $output")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_PackageFileDoesNotExist' {
    $root = New-VerifyFixture
    try {
        $output, $exitCode = Invoke-Verify -Root $root -Arguments @(
            '--tag', 'v0.0.38', '--package-file', 'does-not-exist.tgz')
        Assert-ExitCode 1 'a missing package file should fail'
        Assert-True ($output -match 'Cannot read package file') (
            "error should name the missing package file, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_RootIsNotARepository' {
    $root = New-VerifyFixture -NoGit
    try {
        $output, $exitCode = Invoke-Verify -Root $root -Arguments @('--tag', 'v0.0.38')
        Assert-ExitCode 1 'a non-git root should fail closed'
        Assert-True ($output -match 'not a git work tree') (
            "error should explain the non-git root, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Rejects_InvalidArguments' {
    $cases = @(
        @{ Name = 'missing tag'; Arguments = @() },
        @{ Name = 'unknown flag'; Arguments = @('--tag', 'v0.0.38', '--bump', 'patch') },
        @{ Name = 'flag without value'; Arguments = @('--tag') }
    )
    foreach ($case in $cases) {
        $root = New-VerifyFixture
        try {
            $output, $exitCode = Invoke-Verify -Root $root -Arguments $case.Arguments
            Assert-ExitCode 1 "invalid arguments ($($case.Name)) should fail"
            Assert-True ($output -match 'error: ') (
                "output should carry the error prefix ($($case.Name)), got: $output")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Write-Host "== verify-release: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
