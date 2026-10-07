Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$lintScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'lint-release-secrets.js'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Write-Host '== Release credential self-tests =='

function Invoke-ReleaseLint {
    param(
        [string]$WorkflowDir,
        [string]$Document,
        [string[]]$LintArguments = @()
    )

    $hadWorkflowDir = Test-Path Env:RELEASE_SECRETS_WORKFLOW_DIR
    $previousWorkflowDir = $env:RELEASE_SECRETS_WORKFLOW_DIR
    $hadDocument = Test-Path Env:RELEASE_SECRETS_DOC
    $previousDocument = $env:RELEASE_SECRETS_DOC
    try {
        $env:RELEASE_SECRETS_WORKFLOW_DIR = $WorkflowDir
        $env:RELEASE_SECRETS_DOC = $Document
        $output = & node $lintScript @LintArguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        if ($hadWorkflowDir) {
            $env:RELEASE_SECRETS_WORKFLOW_DIR = $previousWorkflowDir
        } else {
            Remove-Item Env:RELEASE_SECRETS_WORKFLOW_DIR -ErrorAction SilentlyContinue
        }
        if ($hadDocument) {
            $env:RELEASE_SECRETS_DOC = $previousDocument
        } else {
            Remove-Item Env:RELEASE_SECRETS_DOC -ErrorAction SilentlyContinue
        }
    }
}

# Fixture blocks model credentials from preparation, tagging, and publication.
# The scanner reads their combined credential references from one release file.
function Write-ReleaseWorkflows {
    param([string]$Root, [string]$Prep, [string]$Tag, [string]$Publish)

    Write-FixtureFile -Root $Root -RelativePath 'workflows/release.yml' -Content ($Prep + "`n" + $Tag + "`n" + $Publish)
}

# The lint resolves its overrides against the repository root, so fixtures pass
# absolute paths.
function Invoke-FixtureLint {
    param([string]$Root, [string[]]$LintArguments = @())

    return Invoke-ReleaseLint `
        -WorkflowDir (Join-Path $Root 'workflows') `
        -Document (Join-Path $Root 'RELEASING.md') `
        -LintArguments $LintArguments
}

$inSyncPrep = @'
name: Prepare release
jobs:
  prepare:
    steps:
      - uses: actions/checkout@v7
        with:
          token: ${{ secrets.RELEASE_TOKEN || github.token }}
'@

$inSyncTag = @'
name: Tag release
jobs:
  tag:
    steps:
      - uses: actions/checkout@v7
        with:
          token: ${{ secrets.RELEASE_TOKEN || github.token }}
'@

$inSyncPublish = @'
name: Publish to NPM
jobs:
  publish:
    steps:
      - run: gh release view "$TAG"
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
'@

$inSyncDocument = @'
# Releasing

Prose may mention NPM_TOKEN because no token is involved.

<!-- release-secrets:begin -->

| Secret | Scope |
| --- | --- |
| `RELEASE_TOKEN` | `contents: write` |

<!-- release-secrets:end -->

More prose.
'@

Invoke-TestCase 'Passes_WhenWorkflowSecretsAreDocumented' {
    $root = New-TempRoot -Prefix 'release-secrets-'
    try {
        Write-ReleaseWorkflows -Root $root -Prep $inSyncPrep -Tag $inSyncTag -Publish $inSyncPublish
        Write-FixtureFile -Root $root -RelativePath 'RELEASING.md' -Content $inSyncDocument
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 0) "in-sync fixture should pass: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenWorkflowSecretIsUndocumented' {
    $root = New-TempRoot -Prefix 'release-secrets-'
    try {
        $prep = $inSyncPrep + "`n      - run: echo `"`${{ secrets.DRY_RUN_SIGNING_KEY }}`"`n"
        Write-ReleaseWorkflows -Root $root -Prep $prep -Tag $inSyncTag -Publish $inSyncPublish
        Write-FixtureFile -Root $root -RelativePath 'RELEASING.md' -Content $inSyncDocument
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "undocumented secret must fail: $($result.Output)"
        Assert-True (
            $result.Output -match 'DRY_RUN_SIGNING_KEY'
        ) "must name the undocumented secret: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenDocumentedSecretIsReadByNoWorkflow' {
    $root = New-TempRoot -Prefix 'release-secrets-'
    try {
        $document = $inSyncDocument -replace '`RELEASE_TOKEN`', '`RELEASE_TOKEN` / `AUTO_COMMIT_APP_ID`'
        Write-ReleaseWorkflows -Root $root -Prep $inSyncPrep -Tag $inSyncTag -Publish $inSyncPublish
        Write-FixtureFile -Root $root -RelativePath 'RELEASING.md' -Content $document
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "unused documented secret must fail: $($result.Output)"
        Assert-True (
            $result.Output -match 'AUTO_COMMIT_APP_ID'
        ) "must name the phantom credential: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Ignores_CommentedSecretsAndBuiltInTokens' {
    $root = New-TempRoot -Prefix 'release-secrets-'
    try {
        $tag = @'
name: Tag release
# A commented-out reference: ${{ secrets.REMOVED_SIGNING_KEY }}
jobs:
  tag:
    steps:
      - run: echo "# ${{ secrets.RELEASE_TOKEN }} stays data, not a comment"
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
'@
        Write-ReleaseWorkflows -Root $root -Prep $inSyncPrep -Tag $tag -Publish $inSyncPublish
        Write-FixtureFile -Root $root -RelativePath 'RELEASING.md' -Content $inSyncDocument
        $result = Invoke-FixtureLint -Root $root
        Assert-True (
            $result.ExitCode -eq 0
        ) "comments must be masked and built-ins excluded: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Detects_BracketFormSecretReferences' {
    $root = New-TempRoot -Prefix 'release-secrets-'
    try {
        # GitHub Actions accepts secrets['NAME'] and secrets["NAME"]; a guard that
        # only matched the dot form would pass a workflow holding an undocumented
        # credential, which is the exact failure this check exists to prevent.
        $publish = @'
name: Publish to NPM
jobs:
  publish:
    steps:
      - env:
          SINGLE: ${{ secrets['SINGLE_QUOTED_SECRET'] }}
          DOUBLE: ${{ secrets["DOUBLE_QUOTED_SECRET"] }}
'@
        Write-ReleaseWorkflows -Root $root -Prep $inSyncPrep -Tag $inSyncTag -Publish $publish
        Write-FixtureFile -Root $root -RelativePath 'RELEASING.md' -Content $inSyncDocument
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "bracket-form secrets must be reported: $($result.Output)"
        Assert-True (
            $result.Output -match 'SINGLE_QUOTED_SECRET'
        ) "must name the single-quoted secret: $($result.Output)"
        Assert-True (
            $result.Output -match 'DOUBLE_QUOTED_SECRET'
        ) "must name the double-quoted secret: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Finds_SecretsInAnyReleaseWorkflow' {
    $root = New-TempRoot -Prefix 'release-secrets-'
    try {
        # Guards against per-file matcher state leaking between workflows: a secret
        # reachable only from the last workflow read must still be found.
        $noSecretPrep = "name: Prepare release`njobs:`n  prepare:`n    steps:`n      - run: echo ok`n"
        $noSecretTag = "name: Tag release`njobs:`n  tag:`n    steps:`n      - run: echo ok`n"
        $publish = @'
name: Publish to NPM
jobs:
  publish:
    steps:
      - env:
          K: ${{ secrets.LAST_WORKFLOW_ONLY }}
'@
        Write-ReleaseWorkflows -Root $root -Prep $noSecretPrep -Tag $noSecretTag -Publish $publish
        $document = $inSyncDocument -replace '`RELEASE_TOKEN`', '`LAST_WORKFLOW_ONLY`'
        Write-FixtureFile -Root $root -RelativePath 'RELEASING.md' -Content $document
        $result = Invoke-FixtureLint -Root $root
        Assert-True (
            $result.ExitCode -eq 0
        ) "the last workflow's secrets must be read: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenDocumentedBlockIsMissing' {
    $root = New-TempRoot -Prefix 'release-secrets-'
    try {
        Write-ReleaseWorkflows -Root $root -Prep $inSyncPrep -Tag $inSyncTag -Publish $inSyncPublish
        Write-FixtureFile -Root $root -RelativePath 'RELEASING.md' -Content "# Releasing`n`nNo block here.`n"
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "a missing block must fail closed: $($result.Output)"
        Assert-True (
            $result.Output -match 'release-secrets:begin'
        ) "must explain the required block: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenDocumentedBlockIsEmpty' {
    $root = New-TempRoot -Prefix 'release-secrets-'
    try {
        $document = "# Releasing`n`n<!-- release-secrets:begin -->`n`n<!-- release-secrets:end -->`n"
        Write-ReleaseWorkflows -Root $root -Prep $inSyncPrep -Tag $inSyncTag -Publish $inSyncPublish
        Write-FixtureFile -Root $root -RelativePath 'RELEASING.md' -Content $document
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "an empty block must fail closed: $($result.Output)"
        Assert-True (
            $result.Output -match 'empty release-secrets block'
        ) "must explain the empty block: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenWorkflowIsMissing' {
    $root = New-TempRoot -Prefix 'release-secrets-'
    try {
        Write-ReleaseWorkflows -Root $root -Prep $inSyncPrep -Tag $inSyncTag -Publish $inSyncPublish
        Remove-Item -LiteralPath (Join-Path $root 'workflows/release.yml') -Force
        Write-FixtureFile -Root $root -RelativePath 'RELEASING.md' -Content $inSyncDocument
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "a missing release workflow must fail closed: $($result.Output)"
        Assert-True (
            $result.Output -match 'release\.yml'
        ) "must name the missing workflow: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Passes_WhenNoCustomSecretsAreRequired' {
    $root = New-TempRoot -Prefix 'release-secrets-'
    try {
        $workflow = "name: Built-in authentication`njobs:`n  release:`n    steps:`n      - env:`n          GH_TOKEN: `${{ github.token }}`n"
        Write-ReleaseWorkflows -Root $root -Prep $workflow -Tag $workflow -Publish $inSyncPublish
        $document = "<!-- release-secrets:begin -->`nNo custom secrets are required.`n<!-- release-secrets:end -->"
        Write-FixtureFile -Root $root -RelativePath 'RELEASING.md' -Content $document
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 0) "explicit no-secret contract should pass: $($result.Output)"
        Write-ReleaseWorkflows -Root $root -Prep $inSyncPrep -Tag $workflow -Publish $inSyncPublish
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 1) 'No-secret documentation must still reject a custom workflow secret.'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Passes_AgainstTheRepositoryItself' {
    $result = Invoke-ReleaseLint -WorkflowDir (Join-Path $repoRoot '.github/workflows') -Document (Join-Path $repoRoot '.llm/references/RELEASING.md') -LintArguments @('--verbose')
    Assert-True ($result.ExitCode -eq 0) "the repository must satisfy its own contract: $($result.Output)"
    Assert-True (
        $result.Output -match 'Release credential contract in sync: 0 secret'
    ) "verbose output must report the compared set: $($result.Output)"
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "release credential self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'release credential self-tests: all passed'
exit 0
