# Prints 'run' or 'skip' on stdout: whether the harness self-test suite must
# execute for the changes between -BaseSha and HEAD. CI's llm-lint workflow
# consults this before spending its slowest step (scripts/tests/run-all.ps1)
# on a change that cannot affect any self-test outcome. With -Selection the
# script instead prints 'all' or the comma-joined self-test file names that
# cover the changed paths, so CI can run the same scoped selection the local
# fast check runs. Every verdict fails safe: whenever the base cannot be
# resolved or a path has no subject mapping, the full suite runs.

param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BaseSha = '',
    [switch]$Selection
)

Set-StrictMode -Version 2.0

if ([string]::IsNullOrWhiteSpace($BaseSha) -or $BaseSha -eq '0000000000000000000000000000000000000000') {
    if ($Selection) {
        Write-Output 'all'
    } else {
        Write-Output 'run'
    }
    exit 0
}

$resolved = & git -C $RepoRoot rev-parse --verify --quiet "$BaseSha^{commit}"
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolved)) {
    if ($Selection) {
        Write-Output 'all'
    } else {
        Write-Output 'run'
    }
    exit 0
}

Import-Module (Join-Path $PSScriptRoot 'harness-scope.psm1')
$changed = @(Get-CommittedChangedFiles -RepoRoot $RepoRoot -BaseSha $BaseSha)
$changed += @(Get-WorkingTreeChangedFiles -RepoRoot $RepoRoot)

if (-not $Selection) {
    foreach ($path in $changed) {
        if (Test-SurfacePath -Path $path) {
            Write-Output 'run'
            exit 0
        }
    }

    Write-Output 'skip'
    exit 0
}

$testFiles = @(
    Get-ChildItem -Path (Join-Path $PSScriptRoot 'tests') -Filter 'test-*.ps1' |
        ForEach-Object { $_.Name }
)
$selfTestSelection = Get-SelfTestSelection -Paths $changed -AvailableTestFiles $testFiles
if ($selfTestSelection.All) {
    Write-Output 'all'
} else {
    Write-Output ($selfTestSelection.Names -join ',')
}
exit 0
