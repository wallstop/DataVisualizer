# Prints 'run' or 'skip' on stdout: whether the harness self-test suite must
# execute for the changes between -BaseSha and HEAD. CI's llm-lint workflow
# consults this before spending its slowest step (scripts/tests/run-all.ps1)
# on a change that cannot affect any self-test outcome. The verdict fails
# safe: whenever the base cannot be resolved, the suite runs.

param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BaseSha = ''
)

Set-StrictMode -Version 2.0

if ([string]::IsNullOrWhiteSpace($BaseSha) -or $BaseSha -eq '0000000000000000000000000000000000000000') {
    Write-Output 'run'
    exit 0
}

$resolved = & git -C $RepoRoot rev-parse --verify --quiet "$BaseSha^{commit}"
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolved)) {
    Write-Output 'run'
    exit 0
}

Import-Module (Join-Path $PSScriptRoot 'harness-scope.psm1')
$changed = Test-HarnessSurfaceChanged -RepoRoot $RepoRoot -BaseSha $BaseSha
if ($changed) {
    Write-Output 'run'
} else {
    Write-Output 'skip'
}
exit 0
