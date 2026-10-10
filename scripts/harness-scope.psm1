Set-StrictMode -Version 2.0

# The harness self-tests (scripts/tests/test-*.ps1) verify this repository's
# agent tooling: lint scripts, the release pipeline, MCP configuration, and
# the devcontainer. This module is the single source of truth for which paths
# can change a self-test outcome, so the CI skip logic and the local fast
# check always agree. Edit only here.

$script:SelfTestSurfacePrefixes = @(
    'scripts/',
    '.llm/',
    '.github/',
    '.devcontainer/',
    '.config/'
)

$script:SelfTestSurfaceFiles = @(
    'package.json',
    'package-lock.json',
    'opencode.json',
    '.pre-commit-config.yaml'
)

function Test-SurfacePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    $normalized = ($Path -replace '\\', '/') -replace '^\./', ''
    $normalized = $normalized.TrimStart('/')

    foreach ($prefix in $script:SelfTestSurfacePrefixes) {
        if ($normalized.StartsWith($prefix)) {
            return $true
        }
    }

    foreach ($file in $script:SelfTestSurfaceFiles) {
        if ($normalized -eq $file) {
            return $true
        }
    }

    return $false
}

function Get-CommittedChangedFiles {
    param(
        [string]$RepoRoot,
        [string]$BaseSha,
        [string]$HeadSha = 'HEAD'
    )

    $output = & git -C $RepoRoot diff --name-only $BaseSha $HeadSha 2>$null
    return @($output | Where-Object { $_ })
}

function Get-WorkingTreeChangedFiles {
    param([string]$RepoRoot)

    $tracked = & git -C $RepoRoot diff --name-only HEAD 2>$null
    $untracked = & git -C $RepoRoot ls-files --others --exclude-standard 2>$null
    return @((@($tracked) + @($untracked)) | Where-Object { $_ })
}

function Test-HarnessSurfaceChanged {
    param(
        [string]$RepoRoot,
        [string]$BaseSha
    )

    $paths = @(Get-CommittedChangedFiles -RepoRoot $RepoRoot -BaseSha $BaseSha)
    $paths += @(Get-WorkingTreeChangedFiles -RepoRoot $RepoRoot)

    foreach ($path in $paths) {
        if (Test-SurfacePath -Path $path) {
            return $true
        }
    }

    return $false
}

Export-ModuleMember -Function @(
    'Test-SurfacePath',
    'Get-CommittedChangedFiles',
    'Get-WorkingTreeChangedFiles',
    'Test-HarnessSurfaceChanged'
)
