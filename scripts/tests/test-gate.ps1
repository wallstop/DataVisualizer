Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$gatePath = Join-Path $repoRoot 'scripts/gate.py'

Write-Host '== Gate plan self-tests =='

# The gate is typed Python executed through uv locally; the self-tests only
# need a working interpreter because --plan executes nothing.
function Find-GatePython {
    $candidates = if ($IsWindows) { @('python', 'python3') } else { @('python3', 'python') }
    foreach ($candidate in $candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($null -eq $command) {
            continue
        }
        & $command.Source --version 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            return $command.Source
        }
    }
    throw 'the gate self-tests require a working Python 3 interpreter (python3 or python)'
}

function Invoke-Gate {
    param([string]$Python, [string[]]$Arguments)

    $output = (& $Python $gatePath @Arguments 2>&1 | Out-String)
    return [pscustomobject]@{
        Output   = $output
        ExitCode = $LASTEXITCODE
    }
}

$python = Find-GatePython

# The plan is the gate's contract: every check the ladder requires appears
# exactly once, with its command and dependencies, and nothing executes.
Invoke-TestCase 'Plan_PrintsTheFullLaneTable' {
    $result = Invoke-Gate -Python $python -Arguments @('--plan')
    Assert-True ($result.ExitCode -eq 0) "the plan must exit 0 (got $($result.ExitCode)): $($result.Output)"
    $plan = $result.Output | ConvertFrom-Json
    Assert-True ($plan.schema -eq 1) "the plan must carry schema 1, got $($plan.schema)"
    $laneNames = @($plan.lanes | ForEach-Object { $_.name })
    Assert-True (($laneNames -join ',') -eq 'csharp,markdown,assembly,harness,tests,pack') (
        "the plan must list the six lanes in order, got: $($laneNames -join ',')")
    $stepNames = @($plan.lanes | ForEach-Object { $_.steps } | ForEach-Object { $_.name })
    Assert-True ($stepNames.Count -eq 16) "the plan must list 16 steps, got $($stepNames.Count)"
    Assert-True ($stepNames.Count -eq ($stepNames | Sort-Object -Unique).Count) 'step names must be unique'
    foreach ($name in $stepNames) {
        Assert-True ($name -match '^[a-z]+\.[a-z-]+$') "step names must be lane-qualified, got: $name"
    }
}

Invoke-TestCase 'Plan_CarriesCommandsAndDependencies' {
    $plan = (Invoke-Gate -Python $python -Arguments @('--plan')).Output | ConvertFrom-Json
    $steps = @{}
    foreach ($lane in $plan.lanes) {
        foreach ($step in $lane.steps) {
            Assert-True ($step.args.Count -ge 1) "step $($step.name) must carry a command"
            Assert-True ($step.title) "step $($step.name) must carry a title"
            $steps[$step.name] = $step
        }
    }
    Assert-True (($steps['csharp.member-order'].after -join ',') -eq 'csharp.format') (
        'the member-order lint must run after the CSharpier format')
    Assert-True (($steps['harness.skills-index'].after -join ',') -eq 'markdown.format') (
        'the harness lane must wait for Markdown formatting, which rewrites .llm files')
    Assert-True (($steps['harness.skills-index-drift'].after -join ',') -eq 'harness.skills-index') (
        'the drift check must run after the index regeneration')
    foreach ($dependency in $steps['harness.skills-index-drift'].after) {
        Assert-True ($steps.ContainsKey($dependency)) "dependency $dependency must exist"
    }
}

Invoke-TestCase 'Plan_MarksTheDriftCheckAsAnEmptyOutputCheck' {
    $plan = (Invoke-Gate -Python $python -Arguments @('--plan')).Output | ConvertFrom-Json
    $checks = @{}
    foreach ($lane in $plan.lanes) {
        foreach ($step in $lane.steps) {
            $checks[$step.name] = $step.check
        }
    }
    Assert-True ($checks['harness.skills-index-drift'] -eq 'empty-output') (
        'the skills index drift check must require empty output')
    $withChecks = @($checks.Keys | Where-Object { $checks[$_] -and $_ -ne 'harness.skills-index-drift' })
    Assert-True ($withChecks.Count -eq 0) "only the drift check may carry a check, got: $($withChecks -join ',')"
}

Invoke-TestCase 'Plan_ResolvesEveryProgram' {
    $plan = (Invoke-Gate -Python $python -Arguments @('--plan')).Output | ConvertFrom-Json
    Assert-True ($plan.unresolved_programs.Count -eq 0) (
        "every step program must resolve on PATH, unresolved: $($plan.unresolved_programs -join ',')")
}

# --only re-runs a failed step without retyping the lane: the requested step
# plus every transitive dependency, and nothing else.
Invoke-TestCase 'Only_SelectsTheRequestedStepsWithDependencies' {
    $result = Invoke-Gate -Python $python -Arguments @('--plan', '--only', 'harness.skills-index-drift')
    Assert-True ($result.ExitCode -eq 0) "the plan must exit 0 (got $($result.ExitCode)): $($result.Output)"
    $plan = $result.Output | ConvertFrom-Json
    $selected = @($plan.lanes | ForEach-Object { $_.steps } | ForEach-Object { $_.name })
    Assert-True (($selected -join ',') -eq 'markdown.format,harness.skills-index,harness.skills-index-drift') (
        "the selection must keep the requested step and its dependencies, got: $($selected -join ',')")
}

Invoke-TestCase 'Only_FailsOnUnknownStepNames' {
    $result = Invoke-Gate -Python $python -Arguments @('--only', 'bogus.step')
    Assert-True ($result.ExitCode -eq 2) "an unknown step must exit 2 (got $($result.ExitCode)): $($result.Output)"
    Assert-True ($result.Output -match 'bogus\.step') 'the error must name the unknown step'
}

# The gate orchestrates the non-Unity ladder; the Unity suites are a separate
# host-dependent gate and must never appear in the step table.
Invoke-TestCase 'Plan_ExcludesTheUnitySuites' {
    $plan = (Invoke-Gate -Python $python -Arguments @('--plan')).Output | ConvertFrom-Json
    $commands = @($plan.lanes | ForEach-Object { $_.steps } | ForEach-Object { $_.args -join ' ' })
    $unityCommands = @($commands | Where-Object { $_ -match 'runTests|testPlatform|-runTests' })
    Assert-True ($unityCommands.Count -eq 0) "no step may launch a Unity suite, got: $($unityCommands -join '; ')"
}

# A failed step must never report without a reason: a launch failure reaches
# the console and the report JSON, and an unusable git in the environment
# block degrades to null instead of crashing the gate (PR #172 review).
Invoke-TestCase 'LaunchFailure_SurfacesTheReasonInConsoleAndReport' {
    $savedPath = $env:PATH
    $reportPath = Join-Path (New-TempRoot -Prefix 'gate-launchfail-') 'report.json'
    try {
        $env:PATH = ''
        $result = Invoke-Gate -Python $python -Arguments @('--only', 'csharp.tool-restore', '--report', $reportPath)
    } finally {
        $env:PATH = $savedPath
    }
    Assert-True ($result.ExitCode -eq 1) "a failed launch must exit 1 (got $($result.ExitCode)): $($result.Output)"
    Assert-True ($result.Output -match "program not found on PATH: 'dotnet'") (
        "the console must name the missing program: $($result.Output)")
    Assert-True ($result.Output -notmatch 'Traceback') "a launch failure must not crash with a traceback: $($result.Output)"
    Assert-True (Test-Path -LiteralPath $reportPath) 'the report must still be written after a launch failure'
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $step = $report.steps[0]
    Assert-True ($step.ok -eq $false) 'the launch-failed step must be recorded as failed'
    Assert-True ($null -eq $step.exit_code) "a step that never ran must have no exit code, got $($step.exit_code)"
    Assert-True ($step.failure -match 'dotnet') "the report must carry the failure reason, got: $($step.failure)"
    Assert-True ($null -eq $report.environment.git_head) 'an unusable git must degrade to a null git head'
    Remove-TempRoot -Path (Split-Path -Parent $reportPath)
}

# An unwritable report is a visible error, not a crash that drowns the gate
# in a traceback or changes its verdict. The stripped PATH keeps every step
# unspawned so the case costs interpreter time only (no toolchain on CI).
Invoke-TestCase 'ReportWriteFailure_IsVisibleWithoutCrashingTheGate' {
    $savedPath = $env:PATH
    $tempRoot = New-TempRoot -Prefix 'gate-reportfail-'
    $reportPath = Join-Path (Join-Path $tempRoot 'missing') 'report.json'
    try {
        $env:PATH = ''
        $result = Invoke-Gate -Python $python -Arguments @('--only', 'csharp.tool-restore', '--report', $reportPath)
    } finally {
        $env:PATH = $savedPath
        Remove-TempRoot -Path $tempRoot
    }
    Assert-True ($result.ExitCode -eq 1) "the step failure must set the verdict (got $($result.ExitCode)): $($result.Output)"
    Assert-True ($result.Output -match '\[gate\] report FAILED:') (
        "the console must report the unwritable report path: $($result.Output)")
    Assert-True ($result.Output -match "program not found on PATH: 'dotnet'") (
        "the step failure reason must stay visible: $($result.Output)")
    Assert-True ($result.Output -notmatch 'Traceback') "a report write failure must not crash: $($result.Output)"
}

# A lane thread that dies without recording must fail the gate AND surface
# the belt's reason on the console, not only in the report JSON: the belt is
# itself a failure path. KeyboardInterrupt escapes the lane's ``except
# Exception`` guard, so the patched step reaches the belt unrecorded.
Invoke-TestCase 'LaneCrash_SurfacesTheReasonInConsoleAndReport' {
    $scriptsSource = Split-Path -Parent $PSScriptRoot
    $tempRoot = New-TempRoot -Prefix 'gate-lanecrash-'
    $reportPath = Join-Path $tempRoot 'report.json'
    # Precomputed outside the array: an unparenthesized 'a' + $var + 'b'
    # element inside @(...) flattens into separate array elements, silently
    # corrupting the built content.
    $pathLine = 'sys.path.insert(0, r"' + $scriptsSource + '")'
    $driver = @(
        'import sys',
        $pathLine,
        'import gate',
        'def crash(self, lane_name, step):',
        '    raise KeyboardInterrupt("lane died (fixture)")',
        'gate.Gate._run_step = crash',
        'sys.exit(gate.main(["--only", "csharp.tool-restore", "--report", sys.argv[1]]))'
    ) -join "`n"
    Write-FixtureFile -Root $tempRoot -RelativePath 'lane-crash-driver.py' -Content ($driver + "`n")
    try {
        $output = (& $python (Join-Path $tempRoot 'lane-crash-driver.py') $reportPath 2>&1 | Out-String).TrimEnd()
        Assert-ExitCode 1 'a crashed lane must fail the gate'
        Assert-True ($output -match '\[gate\] FAIL csharp\.tool-restore') (
            "the console must carry the belt's FAIL line, got: $output")
        Assert-True ($output -match 'no result recorded') (
            "the console must carry the belt's failure reason, got: $output")
        Assert-True ($output -match 'failed: 1 of 1 step') (
            "the summary must count the belt record, got: $output")
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $step = $report.steps[0]
        Assert-True ($step.ok -eq $false) 'the belt-recorded step must be failed in the report'
        Assert-True ($step.failure -match 'no result recorded') (
            "the report must carry the belt's failure reason, got: $($step.failure)")
        Assert-True ($report.passed -eq $false) 'the report verdict must be failed'
    } finally {
        Remove-TempRoot -Path $tempRoot
    }
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "gate self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'gate self-tests: all passed'
exit 0
