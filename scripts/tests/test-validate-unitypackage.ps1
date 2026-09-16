Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$scriptsDirectory = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $scriptsDirectory 'release/build-unitypackage.mjs'
$validateScript = Join-Path $scriptsDirectory 'release/validate-unitypackage.mjs'

Write-Host '== validate-unitypackage self-tests =='

$script:LongName = 'L' + ('o' * 76) + 'ng.cs'

# Fixed 32-hex identities carried by the fixture's committed .meta files.
$script:PackageMetaGuid = 'c1a2975a5c0e4fdd9e3f8a7b6c5d4e01'
$script:ReadmeMetaGuid = 'c1a2975a5c0e4fdd9e3f8a7b6c5d4e02'
$script:EditorMetaGuid = 'c1a2975a5c0e4fdd9e3f8a7b6c5d4e03'
$script:ScriptMetaGuid = 'c1a2975a5c0e4fdd9e3f8a7b6c5d4e04'
$script:LongMetaGuid = 'c1a2975a5c0e4fdd9e3f8a7b6c5d4e05'
$script:StrayGuid = 'ffffffffffffffffffffffffffffffff'

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

function New-ValidatorFixture {
    param([switch]$NoGit)
    $root = New-TempRoot -Prefix 'unitypackage-validator-'
    if (-not $NoGit) {
        & git -C $root init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'git init failed for fixture' }
        & git -C $root config user.name 'Fixture Tester'
        & git -C $root config user.email 'fixture@example.invalid'
    }
    $packageJson = "{`n" +
        "  `"name`": `"com.test.fixture`",`n" +
        "  `"version`": `"0.0.38`",`n" +
        "  `"files`": [`n" +
        "    `"Editor`",`n" +
        "    `"Editor.meta`",`n" +
        "    `"package.json.meta`",`n" +
        "    `"README.md.meta`"`n" +
        "  ]`n" +
        "}`n"
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
    if (-not $NoGit) {
        & git -C $root add -A
        & git -C $root commit --quiet -m 'Prepare release'
        if ($LASTEXITCODE -ne 0) { throw 'git commit failed for fixture' }
    }
    return $root
}

function Invoke-Validator {
    param([string]$Root, [string]$Archive)
    return (
        & node $validateScript --root $Root --archive $Archive *>&1 | Out-String
    ), $LASTEXITCODE
}

function Invoke-ValidatorRaw {
    param([string[]]$Arguments)
    return (& node $validateScript @Arguments *>&1 | Out-String), $LASTEXITCODE
}

function Build-TestArchive {
    param([string]$Root, [string]$Output)
    & node $buildScript --root $Root --output $Output *> $null
    if ($LASTEXITCODE -ne 0) { throw 'the builder failed while preparing a validator fixture' }
    return (Join-Path $Output 'com.test.fixture-0.0.38.unitypackage')
}

function Set-ChecksumSidecar {
    param([string]$ArchivePath, [string]$Hash, [string]$Name)
    [System.IO.File]::WriteAllText(
        "$ArchivePath.sha256",
        "$Hash  $Name`n",
        [System.Text.UTF8Encoding]::new($false)
    )
}

function Write-ChecksumSidecar {
    param([string]$ArchivePath)
    Set-ChecksumSidecar -ArchivePath $ArchivePath `
        -Hash ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()) `
        -Name ([System.IO.Path]::GetFileName($ArchivePath))
}

function Get-StagedPath {
    param([string]$Relative, [switch]$Folder)
    $staged = "Packages/com.test.fixture/$Relative"
    if ($Folder) {
        $staged = "$staged/"
    }
    return "$staged`n"
}

# The fixture entries the tracked payload derives, in builder order.
$script:FixtureEntries = @(
    @{ Guid = $script:EditorMetaGuid; Relative = 'Editor'; Folder = $true },
    @{ Guid = $script:ScriptMetaGuid; Relative = 'Editor/A.cs'; Folder = $false },
    @{ Guid = $script:LongMetaGuid; Relative = "Editor/Deep/$script:LongName"; Folder = $false },
    @{ Guid = $script:ReadmeMetaGuid; Relative = 'README.md'; Folder = $false },
    @{ Guid = $script:PackageMetaGuid; Relative = 'package.json'; Folder = $false }
)

function Get-FixtureMembers {
    param([string]$Root)
    $members = @{}
    foreach ($entry in $script:FixtureEntries) {
        $guid = $entry.Guid
        $members["$guid/pathname"] = Get-StagedPath -Relative $entry.Relative -Folder:$entry.Folder
        if (-not $entry.Folder) {
            $members["$guid/asset"] = [System.IO.File]::ReadAllText((Join-Path $Root $entry.Relative))
        }
        $members["$guid/asset.meta"] = [System.IO.File]::ReadAllText(
            (Join-Path $Root "$($entry.Relative).meta"))
    }
    return $members
}

function Write-MemberFile {
    param([string]$Stage, [string]$Name, [string]$Content)
    $fullPath = Join-Path $Stage $Name
    $directory = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($fullPath, $Content, [System.Text.UTF8Encoding]::new($false))
}

function New-HandBuiltArchive {
    param([string]$Stage, [string]$ArchivePath, [hashtable]$Members, [string[]]$MemberOrder)
    New-Item -ItemType Directory -Path $Stage -Force | Out-Null
    foreach ($name in $MemberOrder) {
        Write-MemberFile -Stage $Stage -Name $name -Content $Members[$name]
    }
    & tar -czf $ArchivePath -C $Stage @MemberOrder
    if ($LASTEXITCODE -ne 0) { throw 'tar staging failed for the hand-built fixture archive' }
}

function New-EmptyTarBytes {
    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    $memory = [System.IO.MemoryStream]::new()
    $gzip = [System.IO.Compression.GZipStream]::new(
        $memory,
        [System.IO.Compression.CompressionLevel]::Fastest
    )
    $gzip.Write([byte[]]::new(1024), 0, 1024)
    $gzip.Dispose()
    return $memory.ToArray()
}

function Invoke-HandBuiltFailureCase {
    param([string]$Root, [hashtable]$Members, [string]$Name, [string]$Pattern)
    $work = Join-Path $Root 'work'
    $fileName = 'hand-' + ($Name -replace '[^a-z0-9]+', '-') + '.unitypackage'
    $archive = Join-Path $work $fileName
    New-HandBuiltArchive -Stage (Join-Path $work 'stage') -ArchivePath $archive `
        -Members $Members -MemberOrder (@($Members.Keys | Sort-Object))
    Write-ChecksumSidecar -ArchivePath $archive
    $output, $exitCode = Invoke-Validator -Root $Root -Archive $archive
    Assert-ExitCode 1 "the hand-built archive ($Name) should fail closed"
    Assert-True ($output -match $Pattern) (
        "error should explain the failure ($Name) with '$Pattern', got: $output")
}

Invoke-TestCase 'Validates_A_BuilderArchive_AgainstTheTrackedTree' {
    $root = New-ValidatorFixture
    try {
        $archive = Build-TestArchive -Root $root -Output (Join-Path $root 'dist')
        $output, $exitCode = Invoke-Validator -Root $root -Archive $archive
        Assert-ExitCode 0 'a builder-produced archive should validate'
        Assert-True ($output -match 'pkg: com\.test\.fixture') (
            "output should report the package name, got: $output")
        Assert-True ($output -match 'version: 0\.0\.38') (
            "output should report the package version, got: $output")
        Assert-True ($output -match 'package_file: com\.test\.fixture-0\.0\.38\.unitypackage') (
            "output should report the archive name, got: $output")
        Assert-True ($output -match 'entries: 5') (
            "output should report the four files plus the Editor folder, got: $output")
        $expectedSha = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-True ($output -match "sha256: $expectedSha") (
            "output should report the archive hash, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Validates_A_HandBuiltStandardTar' {
    $root = New-ValidatorFixture
    try {
        $work = Join-Path $root 'work'
        $members = Get-FixtureMembers -Root $root
        $archive = Join-Path $work 'hand-built.unitypackage'
        New-HandBuiltArchive -Stage (Join-Path $work 'stage') -ArchivePath $archive `
            -Members $members -MemberOrder (@($members.Keys | Sort-Object))
        Write-ChecksumSidecar -ArchivePath $archive
        $output, $exitCode = Invoke-Validator -Root $root -Archive $archive
        Assert-ExitCode 0 'a standards-conformant hand-built tar should validate'
        Assert-True ($output -match 'entries: 5') (
            "output should report the same entry count as the builder, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_TheChecksumSidecarDisagrees' {
    $cases = @(
        @{
            Name = 'stale hash'
            Mutate = {
                param([string]$ArchivePath)
                Set-ChecksumSidecar -ArchivePath $ArchivePath -Hash ('f' * 64) `
                    -Name ([System.IO.Path]::GetFileName($ArchivePath))
            }
            Pattern = 'hashes to'
        },
        @{
            Name = 'missing sidecar'
            Mutate = { param([string]$ArchivePath) Remove-Item -LiteralPath "$ArchivePath.sha256" -Force }
            Pattern = 'checksum sidecar'
        },
        @{
            Name = 'renamed archive'
            Mutate = {
                param([string]$ArchivePath)
                Set-ChecksumSidecar -ArchivePath $ArchivePath `
                    -Hash ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()) `
                    -Name 'other.unitypackage'
            }
            Pattern = "names 'other\.unitypackage'"
        }
    )
    foreach ($case in $cases) {
        $root = New-ValidatorFixture
        try {
            $archive = Build-TestArchive -Root $root -Output (Join-Path $root 'dist')
            & $case.Mutate $archive
            $output, $exitCode = Invoke-Validator -Root $root -Archive $archive
            Assert-ExitCode 1 "a disagreeing checksum sidecar ($($case.Name)) should fail closed"
            Assert-True ($output -match $case.Pattern) (
                "error should explain the sidecar failure ($($case.Name)) with '$($case.Pattern)', got: $output")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_TheArchiveIsCorruptOrEmpty' {
    $cases = @(
        @{
            Name = 'zero-byte file'
            Bytes = [byte[]]@()
            Pattern = 'Cannot read a non-empty archive'
        },
        @{
            Name = 'non-tar garbage'
            Bytes = [System.Text.Encoding]::UTF8.GetBytes('this is not a tarball')
            Pattern = 'Cannot list the archive'
        },
        @{
            Name = 'tar with no members'
            Bytes = New-EmptyTarBytes
            Pattern = 'no GUID-directory entries'
        }
    )
    foreach ($case in $cases) {
        $root = New-ValidatorFixture
        try {
            $fileName = 'bad-' + ($case.Name -replace '[^a-z0-9]+', '-') + '.unitypackage'
            $archive = Join-Path $root $fileName
            [System.IO.File]::WriteAllBytes($archive, $case.Bytes)
            Write-ChecksumSidecar -ArchivePath $archive
            $output, $exitCode = Invoke-Validator -Root $root -Archive $archive
            Assert-ExitCode 1 "a corrupt or empty archive ($($case.Name)) should fail closed"
            Assert-True ($output -match $case.Pattern) (
                "error should explain the archive failure ($($case.Name)) with '$($case.Pattern)', got: $output")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_TheArchiveCarriesMalformedMembers' {
    $cases = @(
        @{
            Name = 'member outside guid directories'
            Mutate = { param([hashtable]$M) $M['Packages/com.test.fixture/README.md'] = 'staged file content' }
            Pattern = 'unexpected member'
        },
        @{
            Name = 'unknown member inside guid directory'
            Mutate = { param([hashtable]$M) $M["$($script:ScriptMetaGuid)/extra"] = 'junk' }
            Pattern = 'unexpected member'
        },
        @{
            Name = 'folder entry with asset'
            Mutate = { param([hashtable]$M) $M["$($script:EditorMetaGuid)/asset"] = 'folder junk' }
            Pattern = 'carries an .asset. member'
        },
        @{
            Name = 'file entry without asset'
            Mutate = { param([hashtable]$M) $M.Remove("$($script:ScriptMetaGuid)/asset") }
            Pattern = 'missing its .asset. member'
        },
        @{
            Name = 'missing asset meta'
            Mutate = { param([hashtable]$M) $M.Remove("$($script:PackageMetaGuid)/asset.meta") }
            Pattern = 'is incomplete'
        }
    )
    foreach ($case in $cases) {
        $root = New-ValidatorFixture
        try {
            $members = Get-FixtureMembers -Root $root
            & $case.Mutate $members
            Invoke-HandBuiltFailureCase -Root $root -Members $members `
                -Name $case.Name -Pattern $case.Pattern
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_TheArchiveCarriesDuplicateMembers' {
    $root = New-ValidatorFixture
    try {
        $members = Get-FixtureMembers -Root $root
        $order = @($members.Keys | Sort-Object)
        $order += "$($script:ScriptMetaGuid)/asset"
        $work = Join-Path $root 'work'
        $archive = Join-Path $work 'hand-duplicate.unitypackage'
        New-HandBuiltArchive -Stage (Join-Path $work 'stage') -ArchivePath $archive `
            -Members $members -MemberOrder $order
        Write-ChecksumSidecar -ArchivePath $archive
        $output, $exitCode = Invoke-Validator -Root $root -Archive $archive
        Assert-ExitCode 1 'a duplicated member should fail closed'
        Assert-True ($output -match 'duplicate member') (
            "error should explain the duplicate member, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_When_TheArchiveDisagreesWithTheRepository' {
    $cases = @(
        @{
            Name = 'wrong guid directory name'
            Mutate = {
                param([hashtable]$M)
                foreach ($suffix in @('pathname', 'asset', 'asset.meta')) {
                    $M["$($script:StrayGuid)/$suffix"] = $M["$($script:ScriptMetaGuid)/$suffix"]
                    $M.Remove("$($script:ScriptMetaGuid)/$suffix")
                }
            }
            Pattern = 'sits in guid directory'
        },
        @{
            Name = 'tampered asset bytes'
            Mutate = { param([hashtable]$M) $M["$($script:ScriptMetaGuid)/asset"] = '// tampered' }
            Pattern = 'asset does not byte-match'
        },
        @{
            Name = 'tampered asset meta bytes'
            Mutate = { param([hashtable]$M) $M["$($script:ScriptMetaGuid)/asset.meta"] = 'fileFormatVersion: 2' }
            Pattern = 'asset\.meta does not byte-match'
        },
        @{
            Name = 'wrong staged path'
            Mutate = { param([hashtable]$M) $M["$($script:ScriptMetaGuid)/pathname"] = (Get-StagedPath 'Editor/B.cs') }
            Pattern = 'outside the tracked payload'
        },
        @{
            Name = 'unsafe staged path'
            Mutate = { param([hashtable]$M) $M["$($script:ScriptMetaGuid)/pathname"] = "Packages/../evil`n" }
            Pattern = 'unsafe path'
        },
        @{
            Name = 'duplicate staged path claim'
            Mutate = {
                param([hashtable]$M)
                foreach ($suffix in @('pathname', 'asset', 'asset.meta')) {
                    $M["$($script:StrayGuid)/$suffix"] = $M["$($script:ScriptMetaGuid)/$suffix"]
                }
            }
            Pattern = 'both stage'
        }
    )
    foreach ($case in $cases) {
        $root = New-ValidatorFixture
        try {
            $members = Get-FixtureMembers -Root $root
            & $case.Mutate $members
            Invoke-HandBuiltFailureCase -Root $root -Members $members `
                -Name $case.Name -Pattern $case.Pattern
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_TheTreeDriftsAfterBuild' {
    $cases = @(
        @{
            Name = 'tracked file without meta'
            RelativePath = 'Editor/B.cs'
            Content = '// drift'
            Pattern = "no committed '\.meta' companion"
        },
        @{
            Name = 'orphan meta'
            RelativePath = 'Editor/Orphan.meta'
            Content = 'meta: orphan'
            Pattern = 'target neither a tracked file nor a directory'
        },
        @{
            Name = 'dropped guid field'
            RelativePath = 'Editor/A.cs.meta'
            Content = 'meta without guid'
            Pattern = "no well-formed 'guid:' field"
        },
        @{
            Name = 'duplicated guid'
            RelativePath = 'Editor/A.cs.meta'
            Content = (New-MetaContent $script:ReadmeMetaGuid)
            Pattern = 'reuses guid'
        }
    )
    foreach ($case in $cases) {
        $root = New-ValidatorFixture
        try {
            $archive = Build-TestArchive -Root $root -Output (Join-Path $root 'dist')
            Write-FixtureFile -Root $root -RelativePath $case.RelativePath -Content $case.Content
            & git -C $root add -A
            & git -C $root commit --quiet -m 'Drift the tree'
            if ($LASTEXITCODE -ne 0) { throw 'git commit failed for drift fixture' }
            $output, $exitCode = Invoke-Validator -Root $root -Archive $archive
            Assert-ExitCode 1 "tree drift ($($case.Name)) should fail closed"
            Assert-True ($output -match $case.Pattern) (
                "error should explain the drift ($($case.Name)) with '$($case.Pattern)', got: $output")
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_When_TheRootIsNotARepository' {
    $root = New-ValidatorFixture -NoGit
    try {
        $members = Get-FixtureMembers -Root $root
        $work = Join-Path $root 'work'
        $archive = Join-Path $work 'hand-built.unitypackage'
        New-HandBuiltArchive -Stage (Join-Path $work 'stage') -ArchivePath $archive `
            -Members $members -MemberOrder (@($members.Keys | Sort-Object))
        Write-ChecksumSidecar -ArchivePath $archive
        $output, $exitCode = Invoke-Validator -Root $root -Archive $archive
        Assert-ExitCode 1 'a non-git root should fail closed'
        Assert-True ($output -match 'not a git work tree') (
            "error should explain the non-git root, got: $output")
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Rejects_InvalidArguments' {
    $root = New-ValidatorFixture
    try {
        $cases = @(
            @{ Name = 'unknown flag'; Arguments = @('--bogus') },
            @{ Name = 'missing required archive flag'; Arguments = @('--root', $root) }
        )
        foreach ($case in $cases) {
            $output, $exitCode = Invoke-ValidatorRaw -Arguments $case.Arguments
            Assert-ExitCode 1 "invalid arguments ($($case.Name)) should fail"
            Assert-True ($output -match 'error: ') (
                "output should carry the error prefix ($($case.Name)), got: $output")
        }
    } finally {
        Remove-TempRoot $root
    }
}

Write-Host "== validate-unitypackage: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
