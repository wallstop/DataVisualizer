Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fastCheckPath = Join-Path $repoRoot 'scripts/fast-check.ps1'

Write-Host '== Fast-check plan self-tests =='

function New-PlanRepo {
    $repo = New-TempRoot -Prefix 'fast-check-'
    & git init -q $repo 2>&1 | Out-Null
    & git -C $repo config user.email 'agent@example.com' 2>&1 | Out-Null
    & git -C $repo config user.name 'Agent' 2>&1 | Out-Null
    Write-FixtureFile -Root $repo -RelativePath 'README.md' -Content "base`n"
    Write-FixtureFile -Root $repo -RelativePath 'scripts/keep.js' -Content "// base`n"
    & git -C $repo add -A 2>&1 | Out-Null
    & git -C $repo commit -q -m 'base' 2>&1 | Out-Null
    return $repo
}

function Invoke-PlanOnly {
    param([string]$Repo, [string]$Base = '')

    if ($Base) {
        $output = (& pwsh -NoProfile -File $fastCheckPath -RepoRoot $Repo -Base $Base -PlanOnly 2>&1 | Out-String)
    } else {
        $output = (& pwsh -NoProfile -File $fastCheckPath -RepoRoot $Repo -PlanOnly 2>&1 | Out-String)
    }
    Assert-True ($LASTEXITCODE -eq 0) "the plan must exit 0 (got $LASTEXITCODE): $output"
    return $output
}

# The plan drives every group the fast check can run, so each case pins one
# classification against a fixture tree.
Invoke-TestCase 'Plan_SplitsGroupsByChangedPath' {
    $repo = New-PlanRepo
    try {
        Write-FixtureFile -Root $repo -RelativePath 'Editor/Foo.cs' -Content "class Foo { }`n"
        Write-FixtureFile -Root $repo -RelativePath 'docs/guide.md' -Content "guide`n"
        Write-FixtureFile -Root $repo -RelativePath 'scripts/new.js' -Content "// new`n"
        Write-FixtureFile -Root $repo -RelativePath 'package.json' -Content "{}`n"
        $output = Invoke-PlanOnly -Repo $repo
        Assert-True ($output -match 'csharp: Editor/Foo\.cs') "the plan must list the C# file: $output"
        Assert-True ($output -match 'markdown: docs/guide\.md') "the plan must list the Markdown file: $output"
        Assert-True ($output -match 'Harness : True') 'a scripts change must plan the harness group'
        Assert-True ($output -match 'Packaging : True') 'an Editor change must plan the packaging group'
        Assert-True ($output -match 'AssemblyConfig : False') 'an unrelated tree must not plan the assembly group'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Plan_IgnoresDocsOnlyTrees' {
    $repo = New-PlanRepo
    try {
        Write-FixtureFile -Root $repo -RelativePath 'docs/guide.md' -Content "guide`n"
        Write-FixtureFile -Root $repo -RelativePath 'README.md' -Content "changed`n"
        $output = Invoke-PlanOnly -Repo $repo
        Assert-True ($output -match 'Harness : False') 'a docs-only tree must skip the harness group'
        Assert-True ($output -match 'Packaging : False') 'a docs-only tree must skip the packaging group'
        Assert-True ($output -match 'csharp: \r?\n') 'a docs-only tree must list no C# files'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Plan_CoversUntrackedAndWorkingTreeFiles' {
    $repo = New-PlanRepo
    try {
        Write-FixtureFile -Root $repo -RelativePath 'Runtime/Thing.cs' -Content "class Thing { }`n"
        $output = Invoke-PlanOnly -Repo $repo
        Assert-True ($output -match 'csharp: Runtime/Thing\.cs') 'an untracked C# file must be planned'
        Assert-True ($output -match 'Packaging : True') 'an untracked Runtime file must plan packaging'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Plan_FallsBackToHeadWhenNoBaseExists' {
    $repo = New-PlanRepo
    try {
        Write-FixtureFile -Root $repo -RelativePath 'CHANGELOG.md' -Content "## [Unreleased]`n`n### Added`n`n- Something. (#1)`n"
        $output = Invoke-PlanOnly -Repo $repo
        Assert-True ($output -match 'markdown: CHANGELOG\.md') 'a fixture without origin/main must still plan the working tree'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

# Deletions and rename old-paths show up in a git diff but no longer exist on
# disk; the plan must hand formatters only files that are actually there.
Invoke-TestCase 'Plan_ExcludesWorkingTreeDeletionsFromFormatGroups' {
    $repo = New-PlanRepo
    try {
        Write-FixtureFile -Root $repo -RelativePath 'Editor/Foo.cs' -Content "class Foo { }`n"
        Write-FixtureFile -Root $repo -RelativePath 'docs/guide.md' -Content "guide`n"
        & git -C $repo add -A 2>&1 | Out-Null
        & git -C $repo commit -q -m 'add fixtures' 2>&1 | Out-Null
        Remove-Item -LiteralPath (Join-Path $repo 'Editor/Foo.cs')
        Remove-Item -LiteralPath (Join-Path $repo 'docs/guide.md')
        $output = Invoke-PlanOnly -Repo $repo
        Assert-True ($output -match 'csharp: \r?\n') 'a deleted C# file must not be a format target'
        Assert-True ($output -match 'markdown: \r?\n') 'a deleted Markdown file must not be a format target'
        Assert-True ($output -match 'Packaging : True') 'a deleted Editor file must still plan packaging'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Plan_ExcludesCommittedDeletionsFromFormatGroups' {
    $repo = New-PlanRepo
    try {
        Write-FixtureFile -Root $repo -RelativePath 'Editor/Foo.cs' -Content "class Foo { }`n"
        Write-FixtureFile -Root $repo -RelativePath 'docs/guide.md' -Content "guide`n"
        & git -C $repo add -A 2>&1 | Out-Null
        & git -C $repo commit -q -m 'add fixtures' 2>&1 | Out-Null
        Remove-Item -LiteralPath (Join-Path $repo 'Editor/Foo.cs')
        Remove-Item -LiteralPath (Join-Path $repo 'docs/guide.md')
        & git -C $repo add -A 2>&1 | Out-Null
        & git -C $repo commit -q -m 'delete fixtures' 2>&1 | Out-Null
        $output = Invoke-PlanOnly -Repo $repo -Base (& git -C $repo rev-parse HEAD~1)
        Assert-True ($output -match 'csharp: \r?\n') 'a committed C# deletion must not be a format target'
        Assert-True ($output -match 'markdown: \r?\n') 'a committed Markdown deletion must not be a format target'
        Assert-True ($output -match 'Packaging : True') 'a committed Editor deletion must still plan packaging'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Plan_FormatsOnlyTheExistingSideOfARename' {
    $repo = New-PlanRepo
    try {
        Write-FixtureFile -Root $repo -RelativePath 'docs/old.md' -Content "guide`n"
        & git -C $repo add -A 2>&1 | Out-Null
        & git -C $repo commit -q -m 'add doc' 2>&1 | Out-Null
        & git -C $repo mv docs/old.md docs/new.md 2>&1 | Out-Null
        $output = Invoke-PlanOnly -Repo $repo
        Assert-True ($output -match 'markdown: docs/new\.md') 'the new path of a rename must be a format target'
        Assert-True ($output -notmatch 'docs/old\.md') 'the old path of a rename must not be a format target'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "fast-check self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'fast-check self-tests: all passed'
exit 0
