Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

Write-Host '== opencode-service-recycle contract tests =='

Invoke-TestCase 'Runs_WhenBashIsAvailable' {
    $bashCommand = Get-Command bash -ErrorAction SilentlyContinue
    if ($null -eq $bashCommand) {
        Write-Host '    SKIP: bash not found on PATH; opencode-service-recycle tests require a POSIX shell.'
        return
    }

    $repoRoot = (Get-Item (Join-Path $PSScriptRoot '..\..')).FullName
    $testScript = Join-Path $repoRoot '.devcontainer/tests/test-opencode-service-recycle.sh'
    Assert-True (Test-Path -LiteralPath $testScript) "recycle test script missing: $testScript"

    $output = & $bashCommand $testScript 2>&1 | Out-String
    $LASTEXITCODE | Out-Null
    Write-Host $output
    Assert-ExitCode 0 'opencode-service-recycle suite should pass'
    Assert-True (
        $output -match 'opencode-service-recycle tests: \d+ passed, 0 failed'
    ) "suite summary should report zero failures, got: $output"
}

Write-Host "== opencode-service-recycle: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0