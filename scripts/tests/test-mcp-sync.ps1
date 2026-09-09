Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

Write-Host '== mcp-sync contract tests =='

Invoke-TestCase 'Runs_When_BashIsAvailable' {
    $bashCommand = Get-Command bash -ErrorAction SilentlyContinue
    if ($null -eq $bashCommand) {
        Write-Host '    SKIP: bash not found on PATH; mcp-sync contract tests require a POSIX shell.'
        return
    }

    $repoRoot = (Get-Item (Join-Path $PSScriptRoot '..\..')).FullName
    $testScript = Join-Path $repoRoot '.devcontainer/tests/test-mcp-sync.sh'
    Assert-True (Test-Path -LiteralPath $testScript) "sync test script missing: $testScript"

    $output = & $bashCommand $testScript 2>&1 | Out-String
    $LASTEXITCODE | Out-Null
    Write-Host $output
    Assert-ExitCode 0 'mcp-sync contract suite should pass'
    Assert-True (
        $output -match 'mcp-sync contract tests: \d+ passed, 0 failed'
    ) "suite summary should report zero failures, got: $output"
}

Write-Host "== mcp-sync: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
