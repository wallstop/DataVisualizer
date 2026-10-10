Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runnerSource = Join-Path $repoRoot 'scripts/tests/run-all.ps1'

Write-Host '== run-all self-tests =='

# run-all discovers test-*.ps1 in its own directory, so a fixture copies the
# runner beside two stub suites and exercises the name filter and the
# fail-safe fallback against them.
function New-RunnerFixture {
    $root = New-TempRoot -Prefix 'run-all-'
    $testsDirectory = Join-Path $root 'scripts/tests'
    New-Item -ItemType Directory -Path $testsDirectory -Force | Out-Null
    Copy-Item -LiteralPath $runnerSource -Destination (Join-Path $testsDirectory 'run-all.ps1')
    Write-FixtureFile -Root $root -RelativePath 'scripts/tests/test-alpha-passes.ps1' -Content "exit 0`n"
    Write-FixtureFile -Root $root -RelativePath 'scripts/tests/test-zulu-fails.ps1' -Content "exit 1`n"
    return [pscustomobject]@{ Root = $root; Runner = Join-Path $testsDirectory 'run-all.ps1' }
}

function Invoke-Runner {
    param([pscustomobject]$Fixture, [string[]]$Names)

    $argumentList = @('-NoProfile', '-File', $Fixture.Runner)
    if ($Names.Count -gt 0) {
        # pwsh -File cannot carry an array argument; the runner splits commas.
        $argumentList += @('-Names', ($Names -join ','))
    }
    $output = (& pwsh @argumentList 2>&1 | Out-String).TrimEnd()
    return $output, $LASTEXITCODE
}

Invoke-TestCase 'Names_RunsOnlyTheSelectedFiles' {
    $fixture = New-RunnerFixture
    try {
        $output, $exitCode = Invoke-Runner -Fixture $fixture -Names @('test-alpha-passes.ps1')
        Assert-ExitCode 0 'the passing selected suite should pass'
        Assert-True ($output -match 'surface selection: 1 test file\(s\): test-alpha-passes\.ps1') (
            "the runner should report the selection, got: $output")
        Assert-True ($output -match 'all 1 test file\(s\) passed') (
            "the runner should count only the selected file, got: $output")
        Assert-True ($output -notmatch 'test-zulu-fails') 'the unselected failing suite must not run'
    } finally {
        Remove-TempRoot $fixture.Root
    }
}

Invoke-TestCase 'UnknownNames_FailSafeToTheFullSuite' {
    $fixture = New-RunnerFixture
    try {
        $output, $exitCode = Invoke-Runner -Fixture $fixture -Names @('test-alpha-passes.ps1', 'test-missing.ps1')
        Assert-ExitCode 1 'the full suite must run when a requested name is unknown, so the failing stub fails'
        Assert-True ($output -match 'unknown test file\(s\) requested: test-missing\.ps1') (
            "the runner should name the unknown selection, got: $output")
        Assert-True ($output -match '1 of 2 test file\(s\) failed') (
            'the fallback must run every discovered file, so the failing stub is caught')
    } finally {
        Remove-TempRoot $fixture.Root
    }
}

Invoke-TestCase 'EmptyNames_RunEverything' {
    $fixture = New-RunnerFixture
    try {
        $output, $exitCode = Invoke-Runner -Fixture $fixture -Names @()
        Assert-ExitCode 1 'the full suite must run without a selection, so the failing stub fails'
        Assert-True ($output -notmatch 'surface selection') 'an empty selection must not report a narrowed run'
        Assert-True ($output -match '1 of 2 test file\(s\) failed') ("both stub files must run, got: $output")
    } finally {
        Remove-TempRoot $fixture.Root
    }
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "run-all self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'run-all self-tests: all passed'
exit 0
