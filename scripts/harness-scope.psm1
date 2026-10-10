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
    '.pre-commit-config.yaml',
    '.mcp.json',
    '.env.local.example'
)

# Which self-test files verify which tooling subject. A change outside every
# subject (shared manifests, the suite's own helpers, or anything unmapped)
# selects every test file, so a subject without a mapping still gets coverage.
# CI keeps running the full suite on every surface change, which catches
# mapping drift; the local fast check runs only the selection.
$script:SelfTestSubjectsByPath = @{
    'scripts/fast-check.ps1'                        = @('test-fast-check.ps1')
    'scripts/harness-scope.psm1'                    = @('test-fast-check.ps1', 'test-harness-scope.ps1')
    'scripts/should-run-self-tests.ps1'             = @('test-harness-scope.ps1')
    'scripts/generate-skills-index.ps1'             = @('test-generate-skills-index.ps1')
    'scripts/lint-llm-instructions.ps1'             = @('test-lint-llm-instructions.ps1')
    'scripts/lint-file-lengths.ps1'                 = @('test-file-lengths.ps1')
    'scripts/lint-assembly-warnings.ps1'            = @('test-assembly-warnings.ps1')
    'scripts/lint-csharp-member-order.js'           = @('test-csharp-member-order.ps1')
    'scripts/lint-csharp-null-assertions.js'        = @('test-lint-csharp-null-assertions.ps1')
    'scripts/lint-csharp-usings.js'                 = @('test-lint-csharp-usings.ps1')
    'scripts/lint-changelog-length.js'              = @('test-lint-changelog-length.ps1')
    'scripts/lint-line-endings.js'                  = @('test-lint-line-endings.ps1')
    'scripts/lint-release-secrets.js'               = @('test-lint-release-secrets.ps1')
    'scripts/run-unity-validation.ps1'              = @('test-run-unity-validation.ps1')
    'scripts/install-host-errorprone-analyzers.ps1' = @('test-host-errorprone-analyzers.ps1')
    'scripts/release/verify-release.mjs'            = @('test-verify-release.ps1')
    'scripts/release/validate-unitypackage.mjs'     = @('test-validate-unitypackage.ps1')
    'scripts/release/build-unitypackage.mjs'        = @('test-build-unitypackage.ps1', 'test-validate-unitypackage.ps1')
    'scripts/release/tag-release.mjs'               = @('test-tag-release.ps1')
    'scripts/release/prepare-release.mjs'           = @('test-prepare-release.ps1')
    'scripts/release/extract-release-notes.mjs'     = @('test-extract-release-notes.ps1')
    'scripts/LlmConfig.psm1'                        = @('test-lint-llm-instructions.ps1', 'test-file-lengths.ps1', 'test-generate-skills-index.ps1')
    '.devcontainer/ai-backends.sh'                  = @('test-ai-backends.ps1', 'test-env-local-precedence.ps1')
    '.devcontainer/env-local.sh'                    = @('test-ai-backends.ps1', 'test-env-local-precedence.ps1')
    '.devcontainer/recycle-stale-opencode-service.sh' = @('test-opencode-service-recycle.ps1')
}

# Coarse subjects for tooling trees whose files share one contract suite.
# Specific prefixes win over the coarse trees, so order matters; the mcp
# subtree verifies the MCP contract suite, not the .llm content lints.
$script:SelfTestSubjectPrefixes = @(
    @{ Prefix = '.llm/mcp/'; Names = @('test-mcp-credentials.ps1', 'test-mcp-sync.ps1') },
    @{ Prefix = '.devcontainer/'; Names = @('test-ai-backends.ps1', 'test-env-local-precedence.ps1', 'test-mcp-credentials.ps1', 'test-mcp-sync.ps1', 'test-opencode-service-recycle.ps1') },
    @{ Prefix = '.llm/'; Names = @('test-lint-llm-instructions.ps1', 'test-file-lengths.ps1', 'test-generate-skills-index.ps1') },
    @{ Prefix = '.github/'; Names = @('test-release-workflows.ps1', 'test-lint-release-secrets.ps1') }
)

$script:SelfTestSharedPrefixes = @(
    'scripts/tests/',
    '.config/'
)

function ConvertTo-NormalizedRelativePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ''
    }

    $normalized = ($Path -replace '\\', '/') -replace '^\./', ''
    return $normalized.TrimStart('/')
}

function Test-SurfacePath {
    param([string]$Path)

    $normalized = ConvertTo-NormalizedRelativePath -Path $Path
    if (-not $normalized) {
        return $false
    }

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

function Get-SelfTestSelection {
    # Maps changed surface paths onto the self-test files that must run. Any
    # shared or unmapped path - or a mapped name that is not among the
    # available files - fails safe to the full suite. Returns
    # All = $true with no names, or All = $false with the test file names.
    param(
        [string[]]$Paths,
        [string[]]$AvailableTestFiles = @()
    )

    $selected = @{}
    foreach ($path in $Paths) {
        $normalized = ConvertTo-NormalizedRelativePath -Path $path
        # Paths outside the self-test surface cannot change a test outcome,
        # so they never influence the selection; only surface paths that no
        # subject claims fail safe to the full suite.
        if (-not $normalized -or -not (Test-SurfacePath -Path $normalized)) {
            continue
        }

        if ($script:SelfTestSubjectsByPath.ContainsKey($normalized)) {
            foreach ($name in $script:SelfTestSubjectsByPath[$normalized]) {
                $selected[$name] = $true
            }
            continue
        }

        # Editing a self-test file selects itself for immediate feedback.
        if ($normalized -match '^scripts/tests/(test-[a-z0-9-]+\.ps1)$') {
            $selected[$Matches[1]] = $true
            continue
        }

        foreach ($sharedPrefix in $script:SelfTestSharedPrefixes) {
            if ($normalized.StartsWith($sharedPrefix)) {
                return [pscustomobject]@{ All = $true; Names = @() }
            }
        }

        $matched = $false
        foreach ($subject in $script:SelfTestSubjectPrefixes) {
            if ($normalized.StartsWith($subject.Prefix)) {
                $matched = $true
                foreach ($name in $subject.Names) {
                    $selected[$name] = $true
                }
                break
            }
        }

        if (-not $matched) {
            return [pscustomobject]@{ All = $true; Names = @() }
        }
    }

    if ($selected.Count -eq 0) {
        return [pscustomobject]@{ All = $true; Names = @() }
    }

    $names = @($selected.Keys | Sort-Object)
    if ($AvailableTestFiles.Count -gt 0) {
        foreach ($name in $names) {
            if ($AvailableTestFiles -notcontains $name) {
                return [pscustomobject]@{ All = $true; Names = @() }
            }
        }
    }

    return [pscustomobject]@{ All = $false; Names = $names }
}

Export-ModuleMember -Function @(
    'Test-SurfacePath',
    'Get-CommittedChangedFiles',
    'Get-WorkingTreeChangedFiles',
    'Test-HarnessSurfaceChanged',
    'Get-SelfTestSelection'
)
