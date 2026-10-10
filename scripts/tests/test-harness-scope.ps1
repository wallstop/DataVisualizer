Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$modulePath = Join-Path $repoRoot 'scripts/harness-scope.psm1'
$wrapperPath = Join-Path $repoRoot 'scripts/should-run-self-tests.ps1'

Import-Module $modulePath

Write-Host '== Harness scope self-tests =='

function New-ScopeRepo {
    $repo = New-TempRoot -Prefix 'harness-scope-'
    & git init -q $repo 2>&1 | Out-Null
    & git -C $repo config user.email 'agent@example.com' 2>&1 | Out-Null
    & git -C $repo config user.name 'Agent' 2>&1 | Out-Null
    Write-FixtureFile -Root $repo -RelativePath 'README.md' -Content "base`n"
    Write-FixtureFile -Root $repo -RelativePath 'scripts/keep.js' -Content "// base`n"
    & git -C $repo add -A 2>&1 | Out-Null
    & git -C $repo commit -q -m 'base' 2>&1 | Out-Null
    return $repo
}

function Invoke-ScopeCommit {
    param([string]$Repo, [hashtable]$Files)

    foreach ($relativePath in $Files.Keys) {
        Write-FixtureFile -Root $Repo -RelativePath $relativePath -Content $Files[$relativePath]
    }
    & git -C $Repo add -A 2>&1 | Out-Null
    & git -C $Repo commit -q -m 'change' 2>&1 | Out-Null
}

function Invoke-ShouldRun {
    param([string]$Repo, [string]$BaseSha)

    $output = (& pwsh -NoProfile -File $wrapperPath -RepoRoot $Repo -BaseSha $BaseSha 2>&1 | Out-String).Trim()
    Assert-True ($LASTEXITCODE -eq 0) "the wrapper must exit 0 (got $LASTEXITCODE): $output"
    return $output
}

# The verdict must fail safe: an unusable base always runs the suite, while a
# resolvable base skips only when every changed path sits outside the surface
# the self-tests verify.
Invoke-TestCase 'Skips_ADocsOnlyCommit' {
    $repo = New-ScopeRepo
    try {
        $base = (& git -C $repo rev-parse HEAD)
        Invoke-ScopeCommit -Repo $repo -Files @{ 'README.md' = "changed`n" }
        Assert-True ((Invoke-ShouldRun -Repo $repo -BaseSha $base) -eq 'skip') 'a docs-only commit must skip the suite'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Skips_ACSharpOnlyCommit' {
    $repo = New-ScopeRepo
    try {
        $base = (& git -C $repo rev-parse HEAD)
        Invoke-ScopeCommit -Repo $repo -Files @{ 'Editor/Foo.cs' = "class Foo { }`n" }
        Assert-True ((Invoke-ShouldRun -Repo $repo -BaseSha $base) -eq 'skip') 'a C#-only commit must skip the suite'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Runs_ForAScriptsCommit' {
    $repo = New-ScopeRepo
    try {
        $base = (& git -C $repo rev-parse HEAD)
        Invoke-ScopeCommit -Repo $repo -Files @{ 'scripts/keep.js' = "// changed`n" }
        Assert-True ((Invoke-ShouldRun -Repo $repo -BaseSha $base) -eq 'run') 'a scripts commit must run the suite'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Runs_ForHarnessDirectoryCommits' {
    $repo = New-ScopeRepo
    try {
        $base = (& git -C $repo rev-parse HEAD)
        foreach ($path in @('.llm/context.md', '.github/workflows/pipeline.yml', '.devcontainer/install-npm-tools.sh', '.config/dotnet-tools.json')) {
            Write-FixtureFile -Root $repo -RelativePath $path -Content "changed`n"
            & git -C $repo add -A 2>&1 | Out-Null
            & git -C $repo commit -q -m 'surface change' 2>&1 | Out-Null
            Assert-True ((Invoke-ShouldRun -Repo $repo -BaseSha $base) -eq 'run') "a change under $path must run the suite"
        }
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Runs_ForARootManifestCommit' {
    $repo = New-ScopeRepo
    try {
        $base = (& git -C $repo rev-parse HEAD)
        Invoke-ScopeCommit -Repo $repo -Files @{ 'package.json' = "{}`n" }
        Assert-True ((Invoke-ShouldRun -Repo $repo -BaseSha $base) -eq 'run') 'a package.json commit must run the suite'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Runs_ForAnUntrackedSurfaceFile' {
    $repo = New-ScopeRepo
    try {
        $base = (& git -C $repo rev-parse HEAD)
        Invoke-ScopeCommit -Repo $repo -Files @{ 'README.md' = "changed`n" }
        Write-FixtureFile -Root $repo -RelativePath 'scripts/new.js' -Content "// new`n"
        Assert-True ((Invoke-ShouldRun -Repo $repo -BaseSha $base) -eq 'run') 'an untracked scripts file must run the suite'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Runs_ForAWorkingTreeSurfaceEdit' {
    $repo = New-ScopeRepo
    try {
        $base = (& git -C $repo rev-parse HEAD)
        Invoke-ScopeCommit -Repo $repo -Files @{ 'README.md' = "changed`n" }
        Write-FixtureFile -Root $repo -RelativePath 'scripts/keep.js' -Content "// dirty`n"
        Assert-True ((Invoke-ShouldRun -Repo $repo -BaseSha $base) -eq 'run') 'an uncommitted scripts edit must run the suite'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Runs_WhenTheBaseCannotBeResolved' {
    $repo = New-ScopeRepo
    try {
        foreach ($base in @('deadbeefdeadbeefdeadbeefdeadbeefdeadbeef', '0000000000000000000000000000000000000000', '')) {
            Assert-True ((Invoke-ShouldRun -Repo $repo -BaseSha $base) -eq 'run') "an unusable base '$base' must run the suite"
        }
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'SurfacePath_MatchesTheDeclaredSurfaceOnly' {
    foreach ($path in @('scripts/lint.js', 'scripts\nested\lint.js', './.llm/context.md', '.github/workflows/ci.yml', 'package.json', 'package-lock.json', 'opencode.json', '.pre-commit-config.yaml', '.mcp.json', '.env.local.example')) {
        Assert-True (Test-SurfacePath -Path $path) "$path must match the surface"
    }
    foreach ($path in @('README.md', 'docs/index.md', 'Editor/Foo.cs', 'Tests/Editor/Foo.cs', 'sub/package.json', 'docs/images/logo.png', '')) {
        Assert-True (-not (Test-SurfacePath -Path $path)) "'$path' must not match the surface"
    }
}

# The selection maps a changed surface path onto the test files that verify
# it. Unmapped, shared, or stale names fail safe to the full suite.
Invoke-TestCase 'Selection_SelectsTheMappedSubjectTests' {
    $selection = Get-SelfTestSelection -Paths @('scripts/fast-check.ps1')
    Assert-True (-not $selection.All) 'a mapped subject must not select the full suite'
    Assert-True (($selection.Names -join ',') -eq 'test-fast-check.ps1') (
        "the fast-check subject must select its test file, got: $($selection.Names -join ',')")
}

Invoke-TestCase 'Selection_UnionsSubjectsAcrossPaths' {
    $selection = Get-SelfTestSelection -Paths @(
        'scripts/release/verify-release.mjs',
        'scripts/release/validate-unitypackage.mjs'
    )
    Assert-True (-not $selection.All) 'mapped subjects must not select the full suite'
    Assert-True (($selection.Names -join ',') -eq 'test-validate-unitypackage.ps1,test-verify-release.ps1') (
        "the release subjects must union their test files sorted, got: $($selection.Names -join ',')")
}

Invoke-TestCase 'Selection_PrefersTheSpecificMcpSubjectOverTheCoarseTree' {
    $selection = Get-SelfTestSelection -Paths @('.llm/mcp/configure.mjs')
    Assert-True (-not $selection.All) 'the mcp subject must not select the full suite'
    Assert-True (($selection.Names -join ',') -eq 'test-mcp-credentials.ps1,test-mcp-sync.ps1') (
        "the mcp subject must select the mcp test files, got: $($selection.Names -join ',')")
}

Invoke-TestCase 'Selection_MapsATestFileToItself' {
    $selection = Get-SelfTestSelection -Paths @('scripts/tests/test-harness-scope.ps1')
    Assert-True (-not $selection.All) 'a test file edit must not select the full suite'
    Assert-True (($selection.Names -join ',') -eq 'test-harness-scope.ps1') (
        'a test file edit must select itself for immediate feedback')
}

Invoke-TestCase 'Selection_FallsBackToAllForSharedSuitePaths' {
    foreach ($path in @('scripts/tests/TestHelpers.ps1', 'scripts/tests/run-all.ps1', 'package.json', '.config/dotnet-tools.json')) {
        $selection = Get-SelfTestSelection -Paths @($path)
        Assert-True ($selection.All) "the shared path $path must select the full suite"
    }
}

Invoke-TestCase 'Selection_FallsBackToAllForUnmappedSubjects' {
    $selection = Get-SelfTestSelection -Paths @('scripts/build-docs.ps1')
    Assert-True ($selection.All) 'an unmapped surface path must select the full suite'
}

Invoke-TestCase 'Selection_IgnoresPathsOutsideTheSurface' {
    $selection = Get-SelfTestSelection -Paths @('scripts/fast-check.ps1', 'docs/index.md', 'node_modules.meta')
    Assert-True (-not $selection.All) 'non-surface paths must not broaden the selection'
    Assert-True (($selection.Names -join ',') -eq 'test-fast-check.ps1') (
        "the selection must keep only the subject test, got: $($selection.Names -join ',')")
}

Invoke-TestCase 'Selection_FailsSafeWhenAMappedNameIsUnavailable' {
    $selection = Get-SelfTestSelection -Paths @('scripts/fast-check.ps1') -AvailableTestFiles @('test-other.ps1')
    Assert-True ($selection.All) 'a mapped name missing from the suite must select the full suite'
}

Invoke-TestCase 'Selection_AcceptsTheAvailableNames' {
    $selection = Get-SelfTestSelection -Paths @('scripts/fast-check.ps1') -AvailableTestFiles @('test-fast-check.ps1', 'test-zulu.ps1')
    Assert-True (-not $selection.All) 'a mapped name present in the suite must not select the full suite'
    Assert-True (($selection.Names -join ',') -eq 'test-fast-check.ps1') (
        'the selection must keep the mapped name only')
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "harness-scope self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'harness-scope self-tests: all passed'
exit 0
