Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$workflowRoot = Join-Path $repoRoot '.github/workflows'
$workflow = Get-Content -Raw (Join-Path $workflowRoot 'release.yml')
$prep = $workflow.Substring(0, $workflow.IndexOf("  release:`n"))
$publish = $workflow.Substring($workflow.IndexOf("  release:`n"))

Invoke-TestCase 'Uses_OneReleaseWorkflow_WithBuiltInAuthentication' {
    Assert-True ($workflow -notmatch 'secrets.RELEASE_TOKEN') 'Release workflow must not need a custom token.'
    Assert-True ($prep -match 'gh workflow run llm-lint.yml') 'Preparation must start release-branch CI.'
    Assert-True ($publish -notmatch 'gh workflow run') 'Tagging and publication must execute in the same job.'
    Assert-True ($publish -match 'id-token: write') 'npm requires OIDC permission.'
    Assert-True ($workflow -match 'pull_request:' -and $workflow -match 'types: \[closed\]') 'Merged release PRs must trigger publication.'
    foreach ($old in @('release-prep.yml', 'release-tag.yml', 'npm-publish.yml')) {
        Assert-True (-not (Test-Path (Join-Path $workflowRoot $old))) "Obsolete $old must be removed."
    }
}

Invoke-TestCase 'Validates_AllArtifacts_BeforeNpmPublication' {
    $publication = $publish.IndexOf('- name: Publish to npm with trusted publishing')
    foreach ($step in @('Build the release .unitypackage', 'Validate the release .unitypackage', 'Prepare release notes and the npm tarball checksum')) {
        $index = $publish.IndexOf("- name: $step")
        Assert-True ($index -ge 0 -and $index -lt $publication) "$step must succeed before irreversible npm publication."
    }
}

Invoke-TestCase 'Rehearses_WithoutPublishing' {
    Assert-True ($workflow -match 'dry_run:') 'Publish workflow needs an artifact rehearsal input.'
    foreach ($name in @('Publish to npm with trusted publishing', 'Create or update the GitHub Release')) {
        Assert-True ($publish -match ("- name: " + [regex]::Escape($name) + '\r?\n        if: \$\{\{ inputs.dry_run != true \}\}')) "$name must be disabled during a rehearsal."
    }
    Assert-True ($publish -match 'actions/upload-artifact@') 'Rehearsal must retain reviewable artifacts.'
}

Invoke-TestCase 'Validates_PreparedTree_EvenDuringDryRun' {
    Assert-True ($prep -notmatch 'args\+=\(--dry-run\)') 'Runner must validate the bumped tree in a rehearsal.'
    Assert-True ($prep -match 'extract-release-notes.mjs' -and $prep -match 'validate-unitypackage.mjs') 'Preparation must validate the future release notes and Unity archive.'
    Assert-True ($prep -notmatch 'later release-automation slices') 'Release PR must describe the implemented chain.'
    Assert-True ($prep -match '--body-file') 'Release PR body must use a file.'
}

Invoke-TestCase 'Prepares_Tags_AndBuilds_ACompleteRelease' {
    $root = New-TempRoot -Prefix 'release-chain-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'package.json' -Content '{"name":"com.test.fixture","version":"1.2.3","files":["Editor","Editor.meta","package.json.meta","CHANGELOG.md","CHANGELOG.md.meta"]}'
        Write-FixtureFile -Root $root -RelativePath '.llm/context.md' -Content '**Version**: 1.2.3'
        Write-FixtureFile -Root $root -RelativePath 'CHANGELOG.md' -Content "## [Unreleased]`n`n### Fixed`n`n- Fix asset selection.`n"
        Write-FixtureFile -Root $root -RelativePath 'Editor/fixture.txt' -Content 'release asset'
        foreach ($path in @('Editor.meta', 'Editor/fixture.txt.meta', 'package.json.meta', 'CHANGELOG.md.meta')) {
            $guid = [Guid]::NewGuid().ToString('N')
            Write-FixtureFile -Root $root -RelativePath $path -Content "fileFormatVersion: 2`nguid: $guid`n"
        }
        & git -C $root init --quiet
        Assert-ExitCode 0 'fixture init'
        & git -C $root config user.name 'Release Fixture'
        & git -C $root config user.email 'fixture@example.invalid'
        & git -C $root add -A
        & git -C $root commit --quiet -m 'Initial fixture'
        Assert-ExitCode 0 'fixture commit'
        $releaseScripts = Join-Path $repoRoot 'scripts/release'
        & node (Join-Path $releaseScripts 'prepare-release.mjs') --root $root --bump patch
        Assert-ExitCode 0 'prepare candidate'
        & git -C $root add -A
        & git -C $root commit --quiet -m 'Prepare release'
        Assert-ExitCode 0 'candidate commit'
        & node (Join-Path $releaseScripts 'tag-release.mjs') --root $root --branch release/v1.2.4
        Assert-ExitCode 0 'create annotated release tag'
        $kind = & git -C $root cat-file -t refs/tags/v1.2.4
        Assert-True ($kind -eq 'tag') 'Release tag must be annotated.'
        & node (Join-Path $releaseScripts 'verify-release.mjs') --root $root --tag v1.2.4
        Assert-ExitCode 0 'pack and validate npm payload'
        $dist = Join-Path $root 'dist'
        & node (Join-Path $releaseScripts 'build-unitypackage.mjs') --root $root --output $dist
        Assert-ExitCode 0 'build Unity archive'
        $archive = Join-Path $dist 'com.test.fixture-1.2.4.unitypackage'
        & node (Join-Path $releaseScripts 'validate-unitypackage.mjs') --root $root --archive $archive
        Assert-ExitCode 0 'validate Unity archive against tracked tree'
        $firstHash = (Get-FileHash -Algorithm SHA256 $archive).Hash
        & node (Join-Path $releaseScripts 'build-unitypackage.mjs') --root $root --output $dist
        Assert-ExitCode 0 'repeat archive build'
        Assert-True ((Get-FileHash -Algorithm SHA256 $archive).Hash -eq $firstHash) 'Rebuilding the same release must reproduce its Unity archive.'
        $notes = Join-Path $dist 'release-notes.md'
        & node (Join-Path $releaseScripts 'extract-release-notes.mjs') --root $root --version 1.2.4 --output $notes
        Assert-ExitCode 0 'extract release notes'
        Assert-True ((Get-Content -Raw $notes) -match 'Fix asset selection') 'GitHub notes must contain the rotated changelog entry.'
        Assert-True (Test-Path (Join-Path $root 'com.test.fixture-1.2.4.tgz')) 'npm archive must be ready for publication.'
        Assert-True (Test-Path "$archive.sha256") 'Unity archive must have its checksum companion.'
    } finally {
        Remove-TempRoot $root
    }
}

if ($script:TestFailureCount -gt 0) { exit 1 }
Write-Host 'Release workflow contracts: all passed'
