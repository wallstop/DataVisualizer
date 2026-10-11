Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scopePath = Join-Path $repoRoot 'scripts/harness_scope.py'
$wrapperPath = Join-Path $repoRoot 'scripts/should_run_self_tests.py'

Write-Host '== Harness scope self-tests =='

# The scope module and the verdict wrapper are typed Python; the self-tests
# only need a working interpreter because the probes execute nothing.
$python = Find-Python

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
    param([string]$Repo, [string]$BaseSha, [switch]$Selection)

    $arguments = @($wrapperPath, '--repo-root', $Repo, '--base-sha', $BaseSha)
    if ($Selection) {
        $arguments += '--selection'
    }
    $output = (& $python @arguments 2>&1 | Out-String).Trim()
    Assert-True ($LASTEXITCODE -eq 0) "the wrapper must exit 0 (got $LASTEXITCODE): $output"
    return $output
}

function Invoke-ScopeProbe {
    param([string]$Paths, [string]$Available = '')

    $arguments = @($scopePath, 'probe', '--paths', $Paths)
    if ($Available) {
        $arguments += @('--available', $Available)
    }
    $output = (& $python @arguments 2>&1 | Out-String)
    Assert-True ($LASTEXITCODE -eq 0) "the probe must exit 0 (got $LASTEXITCODE): $output"
    return ($output | ConvertFrom-Json)
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

# The surface match list pins the prefixes and the root-level manifest files;
# everything else, including nested manifests, stays outside the surface.
Invoke-TestCase 'SurfacePath_MatchesTheDeclaredSurfaceOnly' {
    $surfacePaths = @('scripts/lint.js', 'scripts\nested\lint.js', './.llm/context.md', '.github/workflows/ci.yml', 'package.json', 'package-lock.json', 'opencode.json', '.pre-commit-config.yaml', '.mcp.json', '.env.local.example')
    $probe = Invoke-ScopeProbe -Paths ($surfacePaths -join ',')
    foreach ($path in $surfacePaths) {
        Assert-True ($probe.surface.$path) "$path must match the surface"
    }
    $outsidePaths = @('README.md', 'docs/index.md', 'Editor/Foo.cs', 'Tests/Editor/Foo.cs', 'sub/package.json', 'docs/images/logo.png', '')
    $probe = Invoke-ScopeProbe -Paths ($outsidePaths -join ',')
    foreach ($path in $outsidePaths) {
        Assert-True (-not $probe.surface.$path) "'$path' must not match the surface"
    }
}

# The selection maps a changed surface path onto the test files that verify
# it. Unmapped, shared, or stale names fail safe to the full suite.
Invoke-TestCase 'Selection_SelectsTheMappedSubjectTests' {
    $probe = Invoke-ScopeProbe -Paths 'scripts/fast_check.py'
    Assert-True (-not $probe.selection.all) 'a mapped subject must not select the full suite'
    Assert-True (($probe.selection.names -join ',') -eq 'test-fast-check.ps1') (
        "the fast-check subject must select its test file, got: $($probe.selection.names -join ',')")
}

Invoke-TestCase 'Selection_UnionsSubjectsAcrossPaths' {
    $probe = Invoke-ScopeProbe -Paths 'scripts/release/verify-release.mjs,scripts/release/validate-unitypackage.mjs'
    Assert-True (-not $probe.selection.all) 'mapped subjects must not select the full suite'
    Assert-True (($probe.selection.names -join ',') -eq 'test-validate-unitypackage.ps1,test-verify-release.ps1') (
        "the release subjects must union their test files sorted, got: $($probe.selection.names -join ',')")
}

Invoke-TestCase 'Selection_PrefersTheSpecificMcpSubjectOverTheCoarseTree' {
    $probe = Invoke-ScopeProbe -Paths '.llm/mcp/configure.mjs'
    Assert-True (-not $probe.selection.all) 'the mcp subject must not select the full suite'
    Assert-True (($probe.selection.names -join ',') -eq 'test-mcp-credentials.ps1,test-mcp-sync.ps1') (
        "the mcp subject must select the mcp test files, got: $($probe.selection.names -join ',')")
}

Invoke-TestCase 'Selection_MapsATestFileToItself' {
    $probe = Invoke-ScopeProbe -Paths 'scripts/tests/test-harness-scope.ps1'
    Assert-True (-not $probe.selection.all) 'a test file edit must not select the full suite'
    Assert-True (($probe.selection.names -join ',') -eq 'test-harness-scope.ps1') (
        'a test file edit must select itself for immediate feedback')
}

Invoke-TestCase 'Selection_MapsTheGateSurface' {
    $probe = Invoke-ScopeProbe -Paths 'scripts/gate.py,pyproject.toml,uv.lock'
    Assert-True (-not $probe.selection.all) 'the gate surface must not select the full suite'
    Assert-True (($probe.selection.names -join ',') -eq 'test-gate.ps1') (
        "the gate surface must select its test file, got: $($probe.selection.names -join ',')")
}

Invoke-TestCase 'Selection_MapsTheMigratedTrioSurface' {
    $probe = Invoke-ScopeProbe -Paths 'scripts/harness_scope.py'
    Assert-True (-not $probe.selection.all) 'the scope module must not select the full suite'
    Assert-True (($probe.selection.names -join ',') -eq 'test-fast-check.ps1,test-harness-scope.ps1') (
        "the scope module must select both trio test files, got: $($probe.selection.names -join ',')")
    $probe = Invoke-ScopeProbe -Paths 'scripts/should_run_self_tests.py'
    Assert-True (($probe.selection.names -join ',') -eq 'test-harness-scope.ps1') (
        "the verdict wrapper must select its test file, got: $($probe.selection.names -join ',')")
}

Invoke-TestCase 'Selection_FallsBackToAllForSharedSuitePaths' {
    foreach ($path in @('scripts/tests/TestHelpers.ps1', 'scripts/tests/run_all.py', 'package.json', '.config/dotnet-tools.json')) {
        $probe = Invoke-ScopeProbe -Paths $path
        Assert-True ($probe.selection.all) "the shared path $path must select the full suite"
    }
}

Invoke-TestCase 'Selection_FallsBackToAllForUnmappedSubjects' {
    $probe = Invoke-ScopeProbe -Paths 'scripts/build-docs.ps1'
    Assert-True ($probe.selection.all) 'an unmapped surface path must select the full suite'
}

Invoke-TestCase 'Selection_IgnoresPathsOutsideTheSurface' {
    $probe = Invoke-ScopeProbe -Paths 'scripts/fast_check.py,docs/index.md,node_modules.meta'
    Assert-True (-not $probe.selection.all) 'non-surface paths must not broaden the selection'
    Assert-True (($probe.selection.names -join ',') -eq 'test-fast-check.ps1') (
        "the selection must keep only the subject test, got: $($probe.selection.names -join ',')")
}

Invoke-TestCase 'Selection_FailsSafeWhenAMappedNameIsUnavailable' {
    $probe = Invoke-ScopeProbe -Paths 'scripts/fast_check.py' -Available 'test-other.ps1'
    Assert-True ($probe.selection.all) 'a mapped name missing from the suite must select the full suite'
}

Invoke-TestCase 'Selection_AcceptsTheAvailableNames' {
    $probe = Invoke-ScopeProbe -Paths 'scripts/fast_check.py' -Available 'test-fast-check.ps1,test-zulu.ps1'
    Assert-True (-not $probe.selection.all) 'a mapped name present in the suite must not select the full suite'
    Assert-True (($probe.selection.names -join ',') -eq 'test-fast-check.ps1') (
        'the selection must keep the mapped name only')
}

# The --selection output feeds CI's scoped self-test run: a mapped subject
# prints its test file names, everything else prints 'all'.
Invoke-TestCase 'SelectionOutput_NamesTheSubjectForAMappedCommit' {
    $repo = New-ScopeRepo
    try {
        $base = (& git -C $repo rev-parse HEAD)
        Invoke-ScopeCommit -Repo $repo -Files @{ 'scripts/fast_check.py' = "# changed`n" }
        $output = Invoke-ShouldRun -Repo $repo -BaseSha $base -Selection
        Assert-True ($output -eq 'test-fast-check.ps1') "the selection must name the subject test, got: $output"
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'SelectionOutput_FallsBackToAllForAnUnmappedCommit' {
    $repo = New-ScopeRepo
    try {
        $base = (& git -C $repo rev-parse HEAD)
        Invoke-ScopeCommit -Repo $repo -Files @{ 'scripts/build-docs.ps1' = "# changed`n" }
        $output = Invoke-ShouldRun -Repo $repo -BaseSha $base -Selection
        Assert-True ($output -eq 'all') "an unmapped subject must select the full suite, got: $output"
    } finally {
        Remove-TempRoot -Path $repo
    }
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "harness-scope self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'harness-scope self-tests: all passed'
exit 0
