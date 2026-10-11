Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runnerSource = Join-Path $repoRoot 'scripts/tests/run_all.py'
Write-Host '== run-all self-tests =='

# The runner is typed Python (issue #171); the tests only need a working
# interpreter because the stub suites never touch a toolchain.
$python = Find-Python

# run-all discovers test-*.ps1 in its own directory, so a fixture copies the
# runner beside two stub suites and exercises the name filter and the
# fail-safe fallback against them.
function New-RunnerFixture {
    $root = New-TempRoot -Prefix 'run-all-'
    $testsDirectory = Join-Path $root 'scripts/tests'
    New-Item -ItemType Directory -Path $testsDirectory -Force | Out-Null
    Copy-Item -LiteralPath $runnerSource -Destination (Join-Path $testsDirectory 'run_all.py')
    Write-FixtureFile -Root $root -RelativePath 'scripts/tests/test-alpha-passes.ps1' -Content "exit 0`n"
    Write-FixtureFile -Root $root -RelativePath 'scripts/tests/test-zulu-fails.ps1' -Content "exit 1`n"
    return [pscustomobject]@{ Root = $root; Runner = Join-Path $testsDirectory 'run_all.py' }
}

function Invoke-Runner {
    param([pscustomobject]$Fixture, [string[]]$Names)

    $argumentList = @($Fixture.Runner)
    if ($Names.Count -gt 0) {
        $argumentList += @('--names', ($Names -join ','))
    }
    $output = (& $python @argumentList 2>&1 | Out-String).TrimEnd()
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

# A worker that cannot start a child must record a failure, not vanish: the
# pwsh runner failed closed on a spawn error and the port must too.
Invoke-TestCase 'SpawnFailure_CountsAsFailed' {
    $fixture = New-RunnerFixture
    $driver = @(
        'import os, subprocess, sys',
        'tests_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "scripts", "tests")',
        'sys.path.insert(0, tests_dir)',
        'import run_all',
        'def raise_spawn(*args, **kwargs):',
        '    raise OSError("spawn failed (fixture)")',
        'subprocess.run = raise_spawn',
        'sys.exit(run_all.main(["--names", "test-alpha-passes.ps1"]))'
    ) -join "`n"
    Write-FixtureFile -Root $fixture.Root -RelativePath 'spawn-failure-driver.py' -Content ($driver + "`n")
    try {
        $output = (& $python (Join-Path $fixture.Root 'spawn-failure-driver.py') 2>&1 | Out-String).TrimEnd()
        Assert-ExitCode 1 'a crashed worker must fail the run'
        Assert-True ($output -match 'spawn failed \(fixture\)') (
            "the failure must carry the worker's reason, got: $output")
        Assert-True ($output -match '1 of 1 test file\(s\) failed') (
            'the crashed suite must count as failed, got: $output')
        Assert-True ($output -notmatch 'all 1 test file\(s\) passed') (
            'a crashed worker must never report success, got: $output')
    } finally {
        Remove-TempRoot $fixture.Root
    }
}

# Without pwsh the runner cannot run anything and must fail closed instead
# of reporting an empty pass (mirrors the gate's stripped-PATH case).
Invoke-TestCase 'MissingPwsh_FailsClosed' {
    $fixture = New-RunnerFixture
    $savedPath = $env:PATH
    try {
        $env:PATH = ''
        $output = (& $python @($fixture.Runner, '--names', 'test-alpha-passes.ps1') 2>&1 | Out-String).TrimEnd()
        Assert-ExitCode 1 'a missing pwsh must fail the run'
        Assert-True ($output -match 'pwsh is required') (
            "the error must name the missing requirement, got: $output")
        Assert-True ($output -notmatch 'passed') 'a run that started nothing must not report success'
    } finally {
        $env:PATH = $savedPath
        Remove-TempRoot $fixture.Root
    }
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "run-all self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'run-all self-tests: all passed'
exit 0
