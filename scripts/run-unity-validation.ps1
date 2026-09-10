<#
.SYNOPSIS
Runs one local Unity test suite and records reproducible invocation evidence.

.DESCRIPTION
This direct-host runner is the first validation-harness slice for issue #21. Run it
on the machine that owns both the Unity executable and host project. FixtureSize is
recorded and forwarded for fixture-aware test entrypoints; fixture generation is a
separate task. Output is intentionally rejected when it is inside this package repo.

.EXAMPLE
pwsh -NoProfile -File scripts/run-unity-validation.ps1 `
    -HostProject /path/to/host-project `
    -UnityExecutable /path/to/Unity `
    -UnityVersion 6000.4.6f1 `
    -FixtureSize 100 `
    -Suite editmode `
    -OutputDirectory /tmp/data-visualizer-validation

.EXAMPLE
pwsh -NoProfile -File scripts/run-unity-validation.ps1 `
    -HostProject C:\Code\DataVisualizerHost `
    -UnityExecutable C:\Unity\Editor\Unity.exe `
    -UnityVersion 2022.3.50f1 `
    -FixtureSize 10000 `
    -Suite playmode `
    -OutputDirectory C:\Temp\DataVisualizerValidation `
    -PlanOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$HostProject,

    [Parameter(Mandatory = $true)]
    [string]$UnityExecutable,

    [Parameter(Mandatory = $true)]
    [string]$UnityVersion,

    [Parameter(Mandatory = $true)]
    [ValidateSet(100, 1000, 10000, 50000)]
    [int]$FixtureSize,

    [Parameter(Mandatory = $true)]
    [ValidateSet('editmode', 'playmode')]
    [string]$Suite,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 900,

    [switch]$PlanOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Resolve-ValidationPath {
    param([string]$Path, [string]$Description, [switch]$MustExist)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Description must not be empty."
    }

    $resolved = [System.IO.Path]::GetFullPath($Path)
    if ($MustExist -and -not (Test-Path -LiteralPath $resolved)) {
        throw "$Description does not exist: $resolved"
    }
    return $resolved
}

function Test-PathWithin {
    param([string]$Candidate, [string]$Parent)

    $comparison = if ($IsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    } else {
        [System.StringComparison]::Ordinal
    }
    $separator = [System.IO.Path]::DirectorySeparatorChar
    $parentWithSeparator = $Parent.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + $separator
    return $Candidate.Equals($Parent, $comparison) -or $Candidate.StartsWith(
        $parentWithSeparator,
        $comparison
    )
}

function Write-RunManifest {
    param([hashtable]$Manifest, [string]$Path)

    $json = $Manifest | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText(
        $Path,
        $json + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false)
    )
}

try {
    $repoRoot = Resolve-ValidationPath -Path (Split-Path -Parent $PSScriptRoot) -Description 'Repository root' -MustExist
    $projectPath = Resolve-ValidationPath -Path $HostProject -Description 'Host project' -MustExist
    $unityPath = Resolve-ValidationPath -Path $UnityExecutable -Description 'Unity executable' -MustExist
    if (-not (Test-Path -LiteralPath $unityPath -PathType Leaf)) {
        throw "Unity executable is not a file: $unityPath"
    }

    $assetsPath = Join-Path $projectPath 'Assets'
    $projectVersionPath = Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $assetsPath -PathType Container)) {
        throw "Host project is missing its Assets directory: $assetsPath"
    }
    if (-not (Test-Path -LiteralPath $projectVersionPath -PathType Leaf)) {
        throw "Host project is missing ProjectSettings/ProjectVersion.txt."
    }

    $versionText = Get-Content -LiteralPath $projectVersionPath -Raw
    $versionMatch = [regex]::Match($versionText, '(?m)^m_EditorVersion:\s*(\S+)\s*$')
    if (-not $versionMatch.Success) {
        throw "Host project does not declare m_EditorVersion."
    }
    $actualUnityVersion = $versionMatch.Groups[1].Value
    if (-not $actualUnityVersion.Equals($UnityVersion, [System.StringComparison]::Ordinal)) {
        throw "Requested Unity $UnityVersion, but the host project declares $actualUnityVersion."
    }

    $outputPath = Resolve-ValidationPath -Path $OutputDirectory -Description 'Output directory'
    if (Test-PathWithin -Candidate $outputPath -Parent $repoRoot) {
        throw "Output directory must be outside the package repository: $repoRoot"
    }
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

    $manifestPath = Join-Path $outputPath 'data-visualizer-validation.json'
    $resultsPath = Join-Path $outputPath "$Suite-results.xml"
    $logPath = Join-Path $outputPath "$Suite-unity.log"
    $unityArguments = @(
        '-batchmode',
        '-quit',
        '-projectPath', $projectPath,
        '-runTests',
        '-testPlatform', $Suite,
        '-testResults', $resultsPath,
        '-logFile', $logPath,
        '-dataVisualizerFixtureSize', $FixtureSize.ToString(
            [System.Globalization.CultureInfo]::InvariantCulture
        )
    )
    $manifest = [ordered]@{
        schemaVersion = 1
        status = if ($PlanOnly) { 'planned' } else { 'running' }
        startedAtUtc = [DateTime]::UtcNow.ToString('o')
        completedAtUtc = $null
        exitCode = $null
        configuration = [ordered]@{
            hostProject = $projectPath
            unityExecutable = $unityPath
            unityVersion = $UnityVersion
            fixtureSize = $FixtureSize
            suite = $Suite
            outputDirectory = $outputPath
            timeoutSeconds = $TimeoutSeconds
        }
        command = @($unityPath) + $unityArguments
        artifacts = [ordered]@{
            testResults = $resultsPath
            unityLog = $logPath
        }
    }
    Write-RunManifest -Manifest $manifest -Path $manifestPath

    if ($PlanOnly) {
        Write-Host "Validation plan written to $manifestPath"
        exit 0
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $unityPath
    $startInfo.UseShellExecute = $false
    foreach ($argument in $unityArguments) {
        $startInfo.ArgumentList.Add([string]$argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'Unity process could not be started.'
    }

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        $manifest.status = 'timedOut'
        $manifest.exitCode = 124
    } else {
        $manifest.exitCode = $process.ExitCode
        $manifest.status = if ($process.ExitCode -eq 0) { 'passed' } else { 'failed' }
    }
    $manifest.completedAtUtc = [DateTime]::UtcNow.ToString('o')
    Write-RunManifest -Manifest $manifest -Path $manifestPath
    exit $manifest.exitCode
} catch {
    if (
        ($null -ne (Get-Variable -Name manifest -ErrorAction SilentlyContinue)) -and
        ($null -ne (Get-Variable -Name manifestPath -ErrorAction SilentlyContinue))
    ) {
        $manifest.status = 'failed'
        $manifest.exitCode = 2
        $manifest.completedAtUtc = [DateTime]::UtcNow.ToString('o')
        Write-RunManifest -Manifest $manifest -Path $manifestPath
    }
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 2
}
