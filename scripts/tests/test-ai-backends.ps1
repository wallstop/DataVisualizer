Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

Write-Host '== ai-backends launcher contract tests =='

Invoke-TestCase 'Runs_When_BashIsAvailable' {
    $bashCommand = Get-Command bash -ErrorAction SilentlyContinue
    if ($null -eq $bashCommand) {
        Write-Host '    SKIP: bash not found on PATH; ai-backends contract tests require a POSIX shell.'
        return
    }

    $repoRoot = (Get-Item (Join-Path $PSScriptRoot '..\..')).FullName
    $testScript = Join-Path $repoRoot '.devcontainer/tests/test-ai-backends.sh'
    Assert-True (Test-Path -LiteralPath $testScript) "launcher test script missing: $testScript"

    $output = & $bashCommand $testScript 2>&1 | Out-String
    $LASTEXITCODE | Out-Null
    Write-Host $output
    Assert-ExitCode 0 'ai-backends contract suite should pass'
    Assert-True (
        $output -match 'ai-backends contract tests: \d+ passed, 0 failed'
    ) "suite summary should report zero failures, got: $output"
}

Write-Host "== ai-backends: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
