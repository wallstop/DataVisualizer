# Runs only the validation relevant to the files changed since -Base, so an
# agent iterating on a two-file change checks in seconds instead of running
# the whole ladder. The full ladder in .llm/context.md still gates review.
# PlanOnly prints the classification without executing any tool, which is
# what the self-tests exercise on machines without the .NET toolchain.

param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Base = '',
    [switch]$PlanOnly
)

Set-StrictMode -Version 2.0

Import-Module (Join-Path $PSScriptRoot 'harness-scope.psm1')

function Resolve-BaseSha {
    param([string]$RepoRoot, [string]$Base)

    if (-not [string]::IsNullOrWhiteSpace($Base)) {
        $resolved = & git -C $RepoRoot rev-parse --verify --quiet "$Base^{commit}"
        if ($LASTEXITCODE -eq 0 -and $resolved) {
            return $resolved
        }
    }

    $main = & git -C $RepoRoot rev-parse --verify --quiet 'origin/main'
    if ($LASTEXITCODE -eq 0 -and $main) {
        $mergeBase = & git -C $RepoRoot merge-base HEAD $main 2>$null
        if ($LASTEXITCODE -eq 0 -and $mergeBase) {
            return $mergeBase
        }
    }

    return (& git -C $RepoRoot rev-parse HEAD)
}

function Get-FastCheckPlan {
    param([string]$RepoRoot, [string]$BaseSha)

    $changed = @(Get-CommittedChangedFiles -RepoRoot $RepoRoot -BaseSha $BaseSha)
    $changed += @(Get-WorkingTreeChangedFiles -RepoRoot $RepoRoot)
    $changed = @($changed | Sort-Object -Unique)

    return [pscustomobject]@{
        BaseSha = $BaseSha
        CSharp = @($changed | Where-Object { $_ -match '\.cs$' })
        Markdown = @($changed | Where-Object { $_ -match '\.md$' })
        Harness = @($changed | Where-Object { Test-SurfacePath -Path $_ }).Count -gt 0
        Packaging = @($changed | Where-Object { $_ -match '^(Editor|Runtime)/' -or $_ -eq 'package.json' }).Count -gt 0
        AssemblyConfig = @($changed | Where-Object { $_ -match '\.(asmdef|ruleset|rsp)$' -or $_ -match '\.dll\.meta$' }).Count -gt 0
        ReleaseSurface = @($changed | Where-Object { $_ -eq '.github/workflows/release.yml' -or $_ -eq '.llm/references/RELEASING.md' }).Count -gt 0
        LineEndings = @($changed | Where-Object { $_ -eq '.editorconfig' -or $_ -eq '.gitattributes' }).Count -gt 0
    }
}

function Assert-NativeExit {
    param([string]$Description)

    if ($LASTEXITCODE -ne 0) {
        throw "$Description exited with $LASTEXITCODE"
    }
}

$script:Failures = @()

function Invoke-FastCheckStep {
    param([string]$Name, [scriptblock]$Action)

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
        Write-Host ("[fast-check] {0} ok ({1:n1}s)" -f $Name, $stopwatch.Elapsed.TotalSeconds)
    } catch {
        Write-Host ("[fast-check] {0} FAILED ({1:n1}s): {2}" -f $Name, $stopwatch.Elapsed.TotalSeconds, $_.Exception.Message)
        $script:Failures += $Name
    }
}

$baseSha = Resolve-BaseSha -RepoRoot $RepoRoot -Base $Base
$plan = Get-FastCheckPlan -RepoRoot $RepoRoot -BaseSha $baseSha

if ($PlanOnly) {
    Write-Host "[fast-check] plan for base $($plan.BaseSha)"
    Write-Host "[fast-check] csharp: $($plan.CSharp -join ', ')"
    Write-Host "[fast-check] markdown: $($plan.Markdown -join ', ')"
    foreach ($group in @('Harness', 'Packaging', 'AssemblyConfig', 'ReleaseSurface', 'LineEndings')) {
        Write-Host "[fast-check] $group : $($plan.$group)"
    }
    exit 0
}

Write-Host "[fast-check] validating changes since $($plan.BaseSha)"
Push-Location $RepoRoot
try {
    if ($plan.CSharp.Count -gt 0) {
        Invoke-FastCheckStep -Name 'csharp format + lints' -Action {
            & dotnet tool run csharpier -- format @($plan.CSharp)
            Assert-NativeExit -Description 'csharpier format'
            & node scripts/lint-csharp-member-order.js
            Assert-NativeExit -Description 'member-order lint'
            & node scripts/lint-csharp-null-assertions.js
            Assert-NativeExit -Description 'null-assertions lint'
            & node scripts/lint-csharp-usings.js
            Assert-NativeExit -Description 'usings lint'
        }
    }

    if ($plan.Markdown.Count -gt 0) {
        Invoke-FastCheckStep -Name 'markdown format' -Action {
            & npx --no-install prettier --write @($plan.Markdown)
            Assert-NativeExit -Description 'prettier write'
        }
        if ($plan.Markdown -contains 'CHANGELOG.md') {
            Invoke-FastCheckStep -Name 'changelog length' -Action {
                & node scripts/lint-changelog-length.js
                Assert-NativeExit -Description 'changelog length lint'
            }
        }
    }

    if ($plan.Harness) {
        Invoke-FastCheckStep -Name 'harness lints + self-tests' -Action {
            & pwsh -NoProfile -File scripts/generate-skills-index.ps1
            Assert-NativeExit -Description 'generate-skills-index'
            $drift = & git status --porcelain -- .llm/skills/index.md
            if ($drift) {
                throw '.llm/skills/index.md is stale; run scripts/generate-skills-index.ps1 and commit the result.'
            }
            & pwsh -NoProfile -File scripts/lint-llm-instructions.ps1
            Assert-NativeExit -Description 'llm-instructions lint'
            & pwsh -NoProfile -File scripts/lint-file-lengths.ps1
            Assert-NativeExit -Description 'file-lengths lint'
            & pwsh -NoProfile -File scripts/tests/run-all.ps1
            Assert-NativeExit -Description 'harness self-tests'
        }
    }

    if ($plan.Packaging) {
        Invoke-FastCheckStep -Name 'npm pack' -Action {
            & npm pack --dry-run --no-audit --no-fund
            Assert-NativeExit -Description 'npm pack'
        }
    }

    if ($plan.AssemblyConfig) {
        Invoke-FastCheckStep -Name 'assembly warnings' -Action {
            & pwsh -NoProfile -File scripts/lint-assembly-warnings.ps1
            Assert-NativeExit -Description 'assembly warnings lint'
        }
    }

    if ($plan.ReleaseSurface) {
        Invoke-FastCheckStep -Name 'release secrets' -Action {
            & node scripts/lint-release-secrets.js
            Assert-NativeExit -Description 'release secrets lint'
        }
    }

    if ($plan.LineEndings) {
        Invoke-FastCheckStep -Name 'line endings' -Action {
            & node scripts/lint-line-endings.js
            Assert-NativeExit -Description 'line endings lint'
        }
    }
} finally {
    Pop-Location
}

if ($script:Failures.Count -gt 0) {
    Write-Host "[fast-check] failed: $($script:Failures -join ', ')"
    exit 1
}
Write-Host '[fast-check] passed'
exit 0
