[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Root,
    [switch]$Serve
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LlmConfig.psm1') -DisableNameChecking

function Resolve-PythonLauncher {
    foreach ($candidate in @('python3', 'python')) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($null -ne $command) {
            return $command.Source
        }
    }
    return $null
}

function Get-VenvPythonPath {
    param([string]$VenvRoot)
    $relative = if ($IsWindows) { 'Scripts/python.exe' } else { 'bin/python' }
    return Join-Path $VenvRoot $relative
}

function Invoke-CheckedPython {
    param(
        [string]$PythonPath,
        [string[]]$Arguments,
        [string]$Description
    )
    & $PythonPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE"
    }
}

try {
    $repoRoot = Resolve-LlmRoot $Root
    $requirementsPath = Join-Path $repoRoot 'requirements-docs.txt'
    $configPath = Join-Path $repoRoot 'mkdocs.yml'
    foreach ($required in @($requirementsPath, $configPath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            Write-Host "[docs] ERROR: required file '$required' does not exist"
            exit 1
        }
    }

    $venvRoot = Join-Path $repoRoot '.docs-venv'
    $pythonPath = Get-VenvPythonPath -VenvRoot $venvRoot
    if (-not (Test-Path -LiteralPath $pythonPath -PathType Leaf)) {
        $launcher = Resolve-PythonLauncher
        if ([string]::IsNullOrEmpty($launcher)) {
            Write-Host '[docs] ERROR: no python3 or python executable is on PATH'
            exit 1
        }
        Write-Host "[docs] creating the virtual environment at $venvRoot"
        Invoke-CheckedPython -PythonPath $launcher -Arguments @(
            '-m', 'venv', $venvRoot
        ) -Description 'python -m venv'
        $pythonPath = Get-VenvPythonPath -VenvRoot $venvRoot
        if (-not (Test-Path -LiteralPath $pythonPath -PathType Leaf)) {
            Write-Host "[docs] ERROR: the virtual environment has no interpreter at $pythonPath"
            exit 1
        }
    }

    Invoke-CheckedPython -PythonPath $pythonPath -Arguments @(
        '-m', 'pip', 'install', '--quiet', '--disable-pip-version-check',
        '--requirement', $requirementsPath
    ) -Description 'pip install -r requirements-docs.txt'

    if ($Serve) {
        Invoke-CheckedPython -PythonPath $pythonPath -Arguments @(
            '-m', 'mkdocs', 'serve', '--config-file', $configPath
        ) -Description 'mkdocs serve'
        exit 0
    }

    # --strict is repeated on the command line so the build stays strict even
    # if mkdocs.yml is later relaxed for the preview server. The output
    # directory stays owned by mkdocs.yml.
    Invoke-CheckedPython -PythonPath $pythonPath -Arguments @(
        '-m', 'mkdocs', 'build', '--strict', '--config-file', $configPath
    ) -Description 'mkdocs build --strict'

    Write-Host '[docs] OK: strict build succeeded'
    exit 0
} catch {
    Write-Host "[docs] ERROR: $($_.Exception.Message)"
    exit 1
}
