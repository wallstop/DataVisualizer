Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

Write-Host '== env-local precedence contract tests =='

Invoke-TestCase 'Runs_WhenBashIsAvailable' {
    $bashCommand = Get-Command bash -ErrorAction SilentlyContinue
    if ($null -eq $bashCommand) {
        Write-Host '    SKIP: bash not found on PATH; env-local precedence tests require a POSIX shell.'
        return
    }

    $repoRoot = (Get-Item (Join-Path $PSScriptRoot '..\..')).FullName
    $testScript = Join-Path $repoRoot '.devcontainer/tests/test-env-local-precedence.sh'
    Assert-True (Test-Path -LiteralPath $testScript) "env-local precedence test script missing: $testScript"

    $output = & $bashCommand $testScript 2>&1 | Out-String
    $LASTEXITCODE | Out-Null
    Write-Host $output
    Assert-ExitCode 0 'env-local precedence suite should pass'
    Assert-True (
        $output -match 'env-local precedence tests: \d+ passed, 0 failed'
    ) "suite summary should report zero failures, got: $output"
}

Write-Host "== env-local precedence: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0