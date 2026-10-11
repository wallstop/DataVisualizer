Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fastCheckPath = Join-Path $repoRoot 'scripts/fast_check.py'

Write-Host '== Fast-check plan self-tests =='

# The fast check is typed Python executed through uv locally; the self-tests
# only need a working interpreter because --plan executes nothing.
$python = Find-Python

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

function Invoke-FastCheckPlan {
    param([string]$Repo, [string]$Base = '')

    $arguments = @($fastCheckPath, '--plan', '--repo-root', $Repo)
    if ($Base) {
        $arguments += @('--base', $Base)
    }
    $output = (& $python @arguments 2>&1 | Out-String)
    Assert-True ($LASTEXITCODE -eq 0) "the plan must exit 0 (got $LASTEXITCODE): $output"
    return ($output | ConvertFrom-Json)
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
        $plan = Invoke-FastCheckPlan -Repo $repo
        Assert-True ($plan.schema -eq 1) "the plan must carry schema 1, got $($plan.schema)"
        Assert-True ($plan.csharp -contains 'Editor/Foo.cs') "the plan must list the C# file: $($plan.csharp -join ',')"
        Assert-True ($plan.markdown -contains 'docs/guide.md') "the plan must list the Markdown file: $($plan.markdown -join ',')"
        Assert-True ($plan.harness) 'a scripts change must plan the harness group'
        Assert-True ($plan.packaging) 'an Editor change must plan the packaging group'
        Assert-True (-not $plan.assembly_config) 'an unrelated tree must not plan the assembly group'
        # An unmapped scripts path and the shared manifest fail safe to the
        # full suite.
        Assert-True ($plan.self_tests.all) 'an unmapped surface change must select the full suite'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Plan_IgnoresDocsOnlyTrees' {
    $repo = New-PlanRepo
    try {
        Write-FixtureFile -Root $repo -RelativePath 'docs/guide.md' -Content "guide`n"
        Write-FixtureFile -Root $repo -RelativePath 'README.md' -Content "changed`n"
        $plan = Invoke-FastCheckPlan -Repo $repo
        Assert-True (-not $plan.harness) 'a docs-only tree must skip the harness group'
        Assert-True (-not $plan.packaging) 'a docs-only tree must skip the packaging group'
        Assert-True ($plan.csharp.Count -eq 0) 'a docs-only tree must list no C# files'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Plan_CoversUntrackedAndWorkingTreeFiles' {
    $repo = New-PlanRepo
    try {
        Write-FixtureFile -Root $repo -RelativePath 'Runtime/Thing.cs' -Content "class Thing { }`n"
        $plan = Invoke-FastCheckPlan -Repo $repo
        Assert-True ($plan.csharp -contains 'Runtime/Thing.cs') "an untracked C# file must be planned: $($plan.csharp -join ',')"
        Assert-True ($plan.packaging) 'an untracked Runtime file must plan packaging'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Plan_FallsBackToHeadWhenNoBaseExists' {
    $repo = New-PlanRepo
    try {
        Write-FixtureFile -Root $repo -RelativePath 'CHANGELOG.md' -Content "## [Unreleased]`n`n### Added`n`n- Something. (#1)`n"
        $plan = Invoke-FastCheckPlan -Repo $repo
        Assert-True ($plan.markdown -contains 'CHANGELOG.md') 'a fixture without origin/main must still plan the working tree'
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
        $plan = Invoke-FastCheckPlan -Repo $repo
        Assert-True ($plan.csharp.Count -eq 0) 'a deleted C# file must not be a format target'
        Assert-True ($plan.markdown.Count -eq 0) 'a deleted Markdown file must not be a format target'
        Assert-True ($plan.packaging) 'a deleted Editor file must still plan packaging'
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
        $plan = Invoke-FastCheckPlan -Repo $repo -Base (& git -C $repo rev-parse HEAD~1)
        Assert-True ($plan.csharp.Count -eq 0) 'a committed C# deletion must not be a format target'
        Assert-True ($plan.markdown.Count -eq 0) 'a committed Markdown deletion must not be a format target'
        Assert-True ($plan.packaging) 'a committed Editor deletion must still plan packaging'
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
        $plan = Invoke-FastCheckPlan -Repo $repo
        Assert-True ($plan.markdown -contains 'docs/new.md') "the new path of a rename must be a format target: $($plan.markdown -join ',')"
        Assert-True ($plan.markdown -notcontains 'docs/old.md') 'the old path of a rename must not be a format target'
    } finally {
        Remove-TempRoot -Path $repo
    }
}

# The migrated subject map must select the fast-check test for its own driver
# and fail safe whenever a mapped name is not part of the suite.
Invoke-TestCase 'Plan_SelectsTheMappedSelfTestSubject' {
    $repo = New-PlanRepo
    try {
        Write-FixtureFile -Root $repo -RelativePath 'scripts/tests/test-fast-check.ps1' -Content "Set-StrictMode -Version 2.0`n"
        Write-FixtureFile -Root $repo -RelativePath 'scripts/fast_check.py' -Content "# changed`n"
        $plan = Invoke-FastCheckPlan -Repo $repo
        Assert-True (-not $plan.self_tests.all) 'a mapped subject must not select the full suite'
        Assert-True (($plan.self_tests.names -join ',') -eq 'test-fast-check.ps1') (
            "the fast-check subject must select its test file, got: $($plan.self_tests.names -join ',')")
    } finally {
        Remove-TempRoot -Path $repo
    }
}

Invoke-TestCase 'Plan_PinsTheBaseShaInTheContract' {
    $repo = New-PlanRepo
    try {
        & git -C $repo commit -q --allow-empty -m 'second' 2>&1 | Out-Null
        $base = & git -C $repo rev-parse HEAD~1
        $plan = Invoke-FastCheckPlan -Repo $repo -Base $base
        Assert-True ($plan.base_sha -eq $base) "the plan must carry the resolved base, got $($plan.base_sha)"
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
