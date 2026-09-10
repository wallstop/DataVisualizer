Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

Write-Host '== direct Unity validation runner tests =='

$repoRoot = (Get-Item (Join-Path $PSScriptRoot '../..')).FullName
$runner = Join-Path $repoRoot 'scripts/run-unity-validation.ps1'
$pwshPath = (Get-Process -Id $PID).Path

function New-UnityProjectFixture {
    param([string]$Root, [string]$Version = '6000.4.6f1')

    New-Item -ItemType Directory -Path (Join-Path $Root 'Assets') -Force | Out-Null
    Write-FixtureFile -Root $Root -RelativePath 'ProjectSettings/ProjectVersion.txt' -Content (
        "m_EditorVersion: $Version`nm_EditorVersionWithRevision: $Version (fixture)`n"
    )
}

function Invoke-Runner {
    param(
        [string]$Project,
        [string]$Output,
        [string]$Version = '6000.4.6f1',
        [int]$Size = 100,
        [string]$Mode = 'editmode',
        [switch]$PlanOnly
    )

    $arguments = @(
        '-NoProfile',
        '-File', $runner,
        '-HostProject', $Project,
        '-UnityExecutable', $pwshPath,
        '-UnityVersion', $Version,
        '-FixtureSize', $Size,
        '-Suite', $Mode,
        '-OutputDirectory', $Output
    )
    if ($PlanOnly) {
        $arguments += '-PlanOnly'
    }
    & $pwshPath @arguments 2>&1 | Out-Null
    return $LASTEXITCODE
}

Invoke-TestCase 'Writes_ExplicitPlan_When_InputsAreValid' {
    $root = New-TempRoot -Prefix 'unity-runner-'
    try {
        $project = Join-Path $root 'Host Project'
        $output = Join-Path $root 'Evidence Output'
        New-UnityProjectFixture -Root $project

        $exitCode = Invoke-Runner -Project $project -Output $output -Size 10000 -Mode 'playmode' -PlanOnly
        Assert-True ($exitCode -eq 0) "plan-only invocation should pass, got exit $exitCode"

        $manifestPath = Join-Path $output 'data-visualizer-validation.json'
        Assert-True (Test-Path -LiteralPath $manifestPath) 'plan manifest should exist'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        Assert-True ($manifest.status -eq 'planned') 'plan status should be planned'
        Assert-True ($manifest.configuration.fixtureSize -eq 10000) 'fixture size should be recorded'
        Assert-True ($manifest.configuration.suite -eq 'playmode') 'suite should be recorded'
        Assert-True ($manifest.command -contains '-runTests') 'Unity test argument should be present'
        Assert-True ($manifest.command -contains $project) 'path containing spaces should be one argument'
        Assert-True ($manifest.artifacts.testResults.EndsWith('playmode-results.xml')) 'result path should match the suite'
    } finally {
        Remove-TempRoot -Path $root
    }
}

Invoke-TestCase 'Rejects_UnityVersionMismatch_BeforeWritingEvidence' {
    $root = New-TempRoot -Prefix 'unity-runner-'
    try {
        $project = Join-Path $root 'HostProject'
        $output = Join-Path $root 'Output'
        New-UnityProjectFixture -Root $project -Version '2022.3.50f1'

        $exitCode = Invoke-Runner -Project $project -Output $output -PlanOnly
        Assert-True ($exitCode -eq 2) "version mismatch should exit 2, got $exitCode"
        Assert-True (-not (Test-Path -LiteralPath $output)) 'invalid run should not create output'
    } finally {
        Remove-TempRoot -Path $root
    }
}

Invoke-TestCase 'Rejects_OutputInsidePackageRepository' {
    $root = New-TempRoot -Prefix 'unity-runner-'
    try {
        $project = Join-Path $root 'HostProject'
        New-UnityProjectFixture -Root $project
        $insideRepo = Join-Path $repoRoot 'Temp/forbidden-unity-validation-output'

        $exitCode = Invoke-Runner -Project $project -Output $insideRepo -PlanOnly
        Assert-True ($exitCode -eq 2) "in-repository output should exit 2, got $exitCode"
        Assert-True (-not (Test-Path -LiteralPath $insideRepo)) 'rejected output should not be created'
    } finally {
        Remove-TempRoot -Path $root
    }
}

Invoke-TestCase 'Rejects_UnsupportedFixtureSize_AtParameterBinding' {
    $root = New-TempRoot -Prefix 'unity-runner-'
    try {
        $project = Join-Path $root 'HostProject'
        $output = Join-Path $root 'Output'
        New-UnityProjectFixture -Root $project

        $exitCode = Invoke-Runner -Project $project -Output $output -Size 999 -PlanOnly
        Assert-True ($exitCode -ne 0) 'unsupported fixture size should fail'
        Assert-True (-not (Test-Path -LiteralPath $output)) 'invalid size should not create output'
    } finally {
        Remove-TempRoot -Path $root
    }
}

Invoke-TestCase 'Preserves_ProcessFailureExitCode_AndRecordsFailure' {
    $root = New-TempRoot -Prefix 'unity-runner-'
    try {
        $project = Join-Path $root 'HostProject'
        $output = Join-Path $root 'Output'
        New-UnityProjectFixture -Root $project

        # pwsh is a deterministic non-Unity executable: it starts successfully but rejects
        # Unity's command-line switches, exercising child-process exit propagation.
        $exitCode = Invoke-Runner -Project $project -Output $output
        Assert-True ($exitCode -ne 0) 'failed child process should return a nonzero exit code'

        $manifest = Get-Content (
            Join-Path $output 'data-visualizer-validation.json'
        ) -Raw | ConvertFrom-Json
        Assert-True ($manifest.status -eq 'failed') 'process failure should be recorded'
        Assert-True ($manifest.exitCode -eq $exitCode) 'child exit code should be preserved'
        Assert-True (-not [string]::IsNullOrWhiteSpace($manifest.completedAtUtc)) 'completion time should be recorded'
    } finally {
        Remove-TempRoot -Path $root
    }
}

Write-Host "== direct Unity validation runner: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
