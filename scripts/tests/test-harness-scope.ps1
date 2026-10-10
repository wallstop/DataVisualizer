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
    foreach ($path in @('scripts/lint.js', 'scripts\nested\lint.js', './.llm/context.md', '.github/workflows/ci.yml', 'package.json', 'package-lock.json', 'opencode.json', '.pre-commit-config.yaml')) {
        Assert-True (Test-SurfacePath -Path $path) "$path must match the surface"
    }
    foreach ($path in @('README.md', 'docs/index.md', 'Editor/Foo.cs', 'Tests/Editor/Foo.cs', 'sub/package.json', 'docs/images/logo.png', '')) {
        Assert-True (-not (Test-SurfacePath -Path $path)) "'$path' must not match the surface"
    }
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "harness-scope self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'harness-scope self-tests: all passed'
exit 0
