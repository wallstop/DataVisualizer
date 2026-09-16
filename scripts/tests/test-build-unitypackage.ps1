Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $scriptsDirectory 'release/build-unitypackage.mjs'

Write-Host '== build-unitypackage self-tests =='

$script:LongName = 'L' + ('o' * 76) + 'ng.cs'

# Fixed 32-hex identities carried by the fixture's committed .meta files.
$script:PackageMetaGuid = 'c1a2975a5c0e4fdd9e3f8a7b6c5d4e01'
$script:ReadmeMetaGuid = 'c1a2975a5c0e4fdd9e3f8a7b6c5d4e02'
$script:EditorMetaGuid = 'c1a2975a5c0e4fdd9e3f8a7b6c5d4e03'
$script:ScriptMetaGuid = 'c1a2975a5c0e4fdd9e3f8a7b6c5d4e04'
$script:LongMetaGuid = 'c1a2975a5c0e4fdd9e3f8a7b6c5d4e05'

function New-MetaContent {
    param([string]$Guid)
    return (@(
            'fileFormatVersion: 2',
            "guid: $Guid",
            'DefaultImporter:',
            '  externalObjects: {}',
            '  userData:',
            '  assetBundleName:',
            '  assetBundleVariant:'
        ) -join "`n") + "`n"
}

function New-BuildFixture {
    param(
        [string]$Version = '0.0.38',
        [string]$PackageJson,
        [switch]$NoGit,
        [switch]$SkipCommit
    )
    $root = New-TempRoot -Prefix 'unitypackage-fixture-'
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
            "    `"Editor.meta`",`n" +
            "    `"package.json.meta`",`n" +
            "    `"README.md.meta`"`n" +
            "  ]`n" +
            "}`n"
    }
    Write-FixtureFile -Root $root -RelativePath 'package.json' -Content $PackageJson
    Write-FixtureFile -Root $root -RelativePath 'package.json.meta' -Content (
        New-MetaContent $script:PackageMetaGuid)
    Write-FixtureFile -Root $root -RelativePath 'README.md' -Content '# Fixture readme'
    Write-FixtureFile -Root $root -RelativePath 'README.md.meta' -Content (
        New-MetaContent $script:ReadmeMetaGuid)
    Write-FixtureFile -Root $root -RelativePath 'Editor.meta' -Content (
        New-MetaContent $script:EditorMetaGuid)
    Write-FixtureFile -Root $root -RelativePath 'Editor/A.cs' -Content '// fixture content'
    Write-FixtureFile -Root $root -RelativePath 'Editor/A.cs.meta' -Content (
        New-MetaContent $script:ScriptMetaGuid)
    $longName = $script:LongName
    Write-FixtureFile -Root $root -RelativePath "Editor/Deep/$longName" -Content '// long path content'
    Write-FixtureFile -Root $root -RelativePath "Editor/Deep/$longName.meta" -Content (
        New-MetaContent $script:LongMetaGuid)
    if (-not $NoGit -and -not $SkipCommit) {
        & git -C $root add -A
        & git -C $root commit --quiet -m 'Prepare release'
        if ($LASTEXITCODE -ne 0) { throw 'git commit failed for fixture' }
    }
    return $root
}

function Invoke-Build {
    param([string]$Root, [string[]]$Arguments)
    return (& node $buildScript --root $Root @Arguments *>&1 | Out-String), $LASTEXITCODE
}

function Get-FixtureFileHash {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-ArchiveMembers {
    param([string]$Archive)
    return (& tar -tzf $Archive) |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne '' }
}

Invoke-TestCase 'Builds_DeterministicArchive_WithStandardGuidEntries' {
    $root = New-BuildFixture
    try {
        $firstOutput = Join-Path $root 'out-first'
        $secondOutput = Join-Path $root 'out-second'
        $output, $exitCode = Invoke-Build -Root $root -Arguments @('--output', $firstOutput)
        Assert-ExitCode 0 'a valid fixture should build'
        Assert-True ($output -match 'package_file: com\.test\.fixture-0\.0\.38\.unitypackage') (
            "output should report the archive name, got: $output")
        Assert-True ($output -match 'entries: 5') (
            "output should report the four non-meta files plus the Editor folder, got: $output")
        Assert-True ($output -match 'output: ') "output should report the output directory, got: $output"

        $archive = Join-Path $firstOutput 'com.test.fixture-0.0.38.unitypackage'
        $longStaged = "Packages/com.test.fixture/Editor/Deep/$script:LongName"
        # GUID directory names must equal the committed .meta guid fields.
        $expectedGuids = @(
            @{ Guid = $script:EditorMetaGuid; Staged = 'Packages/com.test.fixture/Editor/'; Kind = 'folder'; MetaContent = (New-MetaContent $script:EditorMetaGuid); Relative = 'Editor' },
            @{ Guid = $script:ScriptMetaGuid; Staged = 'Packages/com.test.fixture/Editor/A.cs'; Kind = 'file'; MetaContent = (New-MetaContent $script:ScriptMetaGuid); Relative = 'Editor/A.cs' },
            @{ Guid = $script:LongMetaGuid; Staged = $longStaged; Kind = 'file'; MetaContent = (New-MetaContent $script:LongMetaGuid); Relative = "Editor/Deep/$script:LongName" },
            @{ Guid = $script:ReadmeMetaGuid; Staged = 'Packages/com.test.fixture/README.md'; Kind = 'file'; MetaContent = (New-MetaContent $script:ReadmeMetaGuid); Relative = 'README.md' },
            @{ Guid = $script:PackageMetaGuid; Staged = 'Packages/com.test.fixture/package.json'; Kind = 'file'; MetaContent = (New-MetaContent $script:PackageMetaGuid); Relative = 'package.json' }
        ) | ForEach-Object { [pscustomobject]@{
                Guid = $_.Guid
                Kind = $_.Kind
                Staged = $_.Staged
                MetaContent = $_.MetaContent
                Relative = $_.Relative
            } }

        $members = Get-ArchiveMembers -Archive $archive
        foreach ($expected in $expectedGuids) {
            $group = @($members | Where-Object { $_.StartsWith("$($expected.Guid)/") })
            if ($expected.Kind -eq 'folder') {
                Assert-True ($group.Count -eq 2 -and $group[0] -eq "$($expected.Guid)/pathname" -and
                    $group[1] -eq "$($expected.Guid)/asset.meta") (
                    "folder entry $($expected.Staged) should carry pathname + asset.meta only, got: $($group -join ', ')")
            } else {
                Assert-True ($group.Count -eq 3 -and $group[0] -eq "$($expected.Guid)/pathname" -and
                    $group[1] -eq "$($expected.Guid)/asset" -and $group[2] -eq "$($expected.Guid)/asset.meta") (
                    "file entry $($expected.Staged) should carry pathname + asset + asset.meta, got: $($group -join ', ')")
            }
        }
        Assert-True (@($members).Count -eq 14) (
            "the archive should carry 4x3 + 1x2 members, got $(@($members).Count): $($members -join ', ')")

        $extract = Join-Path $root 'extract'
        New-Item -ItemType Directory -Path $extract -Force | Out-Null
        tar -xzf $archive -C $extract
        if ($LASTEXITCODE -ne 0) { throw 'tar extraction failed' }
        foreach ($expected in $expectedGuids) {
            $pathnameContent = [System.IO.File]::ReadAllText((Join-Path $extract "$($expected.Guid)/pathname"))
            Assert-True ($pathnameContent -eq "$($expected.Staged)`n") (
                "pathname should carry the staged path, got: $pathnameContent")
            $metaContent = [System.IO.File]::ReadAllText((Join-Path $extract "$($expected.Guid)/asset.meta"))
            Assert-True ($metaContent -eq $expected.MetaContent) (
                "asset.meta for $($expected.Staged) should byte-match the committed meta, got: $metaContent")
            if ($expected.Kind -eq 'folder') {
                continue
            }
            $assetHash = Get-FixtureFileHash (Join-Path $extract "$($expected.Guid)/asset")
            Assert-True ($assetHash -eq (Get-FixtureFileHash (Join-Path $root $expected.Relative))) (
                "asset for $($expected.Staged) should byte-match the tracked file")
        }

        $secondOutputArchive = Join-Path $secondOutput 'com.test.fixture-0.0.38.unitypackage'
        $null, $exitCode = Invoke-Build -Root $root -Arguments @('--output', $secondOutput)
        Assert-ExitCode 0 'a second build should succeed'
        Assert-True ((Get-FixtureFileHash $archive) -eq (Get-FixtureFileHash $secondOutputArchive)) (
            'two builds of the same tree must be byte-identical')

        $checksumLine = (Get-Content -LiteralPath "$archive.sha256" -Raw).Trim()
        Assert-True ($checksumLine -match ('^' + (Get-FixtureFileHash $archive) + '  com\.test\.fixture-0\.0\.38\.unitypackage$')) (
            "the .sha256 sidecar should carry the archive hash in sha256sum format, got: $checksumLine")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Writes_The_Default_Output_Under_Root_Dist' {
    $root = New-BuildFixture
    try {
        $output, $exitCode = Invoke-Build -Root $root -Arguments @()
        Assert-ExitCode 0 'the default invocation should build'
        Assert-True (Test-Path -LiteralPath (Join-Path $root 'dist/com.test.fixture-0.0.38.unitypackage')) (
            "the archive should land under <root>/dist by default, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_PayloadFileHasNoMetaCompanion' {
    $root = New-BuildFixture
    try {
        Write-FixtureFile -Root $root -RelativePath 'Editor/B.cs' -Content '// missing meta'
        & git -C $root add -A
        & git -C $root commit --quiet -m 'Add unmetaed file'
        $output, $exitCode = Invoke-Build -Root $root -Arguments @()
        Assert-ExitCode 1 'a payload file without .meta should fail closed'
        Assert-True ($output -match "no committed '\.meta' companion") (
            "error should explain the missing meta companion, got: $output")
        Assert-True ($output -match 'B\.cs') "error should name the offending file, got: $output"
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'dist'))) (
            'a failed build must not write any archive')
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_MetaTargetsNoTrackedFileOrDirectory' {
    $cases = @(
        @{ Name = 'orphan file meta'; RelativePath = 'Editor/Orphan.meta' },
        @{ Name = 'orphan deep meta'; RelativePath = 'Editor/Orphan/Child.meta' }
    )
    foreach ($case in $cases) {
        $root = New-BuildFixture
        try {
            Write-FixtureFile -Root $root -RelativePath $case.RelativePath -Content 'meta: orphan'
            & git -C $root add -A
            & git -C $root commit --quiet -m 'Add orphan meta'
            $output, $exitCode = Invoke-Build -Root $root -Arguments @()
            Assert-ExitCode 1 "an orphan meta ($($case.Name)) should fail closed"
            Assert-True ($output -match 'target neither a tracked file nor a directory') (
                "error should explain the orphan meta ($($case.Name)), got: $output")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_PackageJsonIsInvalidOrSelectsNothing' {
    $cases = @(
        @{ Name = 'empty files'; PackageJson = "{`n  `"name`": `"com.test.fixture`",`n  `"version`": `"0.0.38`",`n  `"files`": []`n}`n"; Pattern = "non-empty 'files' publish allowlist" },
        @{ Name = 'unsupported semver'; PackageJson = "{`n  `"name`": `"com.test.fixture`",`n  `"version`": `"0.0.38-build.2`",`n  `"files`": [`"Editor`"]`n}`n"; Pattern = 'not supported semver' }
    )
    foreach ($case in $cases) {
        $root = New-BuildFixture -PackageJson $case.PackageJson
        try {
            $output, $exitCode = Invoke-Build -Root $root -Arguments @()
            Assert-ExitCode 1 "invalid package.json ($($case.Name)) should fail"
            Assert-True ($output -match [regex]::Escape($case.Pattern)) (
                "error should explain the failure ($($case.Name)) with '$($case.Pattern)', got: $output")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_RootIsNotARepository' {
    $root = New-BuildFixture -NoGit
    try {
        $output, $exitCode = Invoke-Build -Root $root -Arguments @()
        Assert-ExitCode 1 'a non-git root should fail closed'
        Assert-True ($output -match 'not a git work tree') (
            "error should explain the non-git root, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Rejects_InvalidArguments' {
    $cases = @(
        @{ Name = 'unknown flag'; Arguments = @('--bump', 'patch') },
        @{ Name = 'flag without value'; Arguments = @('--output') }
    )
    foreach ($case in $cases) {
        $root = New-BuildFixture
        try {
            $output, $exitCode = Invoke-Build -Root $root -Arguments $case.Arguments
            Assert-ExitCode 1 "invalid arguments ($($case.Name)) should fail"
            Assert-True ($output -match 'error: ') (
                "output should carry the error prefix ($($case.Name)), got: $output")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_UsedMetaCarriesNoGuidField' {
    $root = New-BuildFixture
    try {
        Write-FixtureFile -Root $root -RelativePath 'Editor/A.cs.meta' -Content 'meta without guid'
        & git -C $root add -A
        & git -C $root commit --quiet -m 'Drop guid field'
        $output, $exitCode = Invoke-Build -Root $root -Arguments @()
        Assert-ExitCode 1 'a used meta without a guid field should fail closed'
        Assert-True ($output -match "no well-formed 'guid:' field") (
            "error should explain the missing guid field, got: $output")
        Assert-True ($output -match 'A\.cs\.meta') "error should name the offending meta, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_TwoMetasCarryTheSameGuid' {
    $root = New-BuildFixture
    try {
        Write-FixtureFile -Root $root -RelativePath 'Editor/A.cs.meta' -Content (
            New-MetaContent $script:ReadmeMetaGuid)
        & git -C $root add -A
        & git -C $root commit --quiet -m 'Duplicate a guid'
        $output, $exitCode = Invoke-Build -Root $root -Arguments @()
        Assert-ExitCode 1 'a duplicated guid should fail closed'
        Assert-True ($output -match 'reuses guid') (
            "error should explain the duplicate guid, got: $output")
        Assert-True ($output -match $script:ReadmeMetaGuid) (
            "error should name the duplicated guid, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Write-Host "== build-unitypackage: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
