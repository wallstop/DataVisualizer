[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Root,
    [switch]$Fix,
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LlmConfig.psm1') -DisableNameChecking

$script:Errors = 0

function Write-LlmError {
    param([string]$Message)
    Write-Host "[llm-instructions] ERROR: $Message"
    $script:Errors++
}

function Write-LlmFix {
    param([string]$Message)
    Write-Host "[llm-instructions] fix: $Message"
}

function Get-PackageVersion {
    param([string]$PackageJsonPath)
    $raw = Read-Utf8Text $PackageJsonPath
    try {
        $package = $raw | ConvertFrom-Json
        $version = $package.version
    } catch {
        throw "package.json is not valid JSON: $($_.Exception.Message)"
    }
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'package.json is missing a version field'
    }
    return $version
}

function Update-ContextVersion {
    param([string]$ContextPath, [string]$PackageVersion)
    $text = Read-Utf8Text $ContextPath
    $pattern = '(\*\*Version\*\*:\s*)([^\r\n]*)'
    if (-not [regex]::IsMatch($text, $pattern)) {
        return $false
    }
    $updated = [regex]::new($pattern).Replace(
        $text,
        { param($match) $match.Groups[1].Value + $PackageVersion },
        1
    )
    if ($updated -eq $text) {
        return $false
    }
    Write-Utf8NoBom -Path $ContextPath -NormalizedText $updated
    return $true
}

try {
    $repoRoot = Resolve-LlmRoot $Root
    $contextPath = Join-Path $repoRoot $ContextRelativePath
    $skillsRoot = Join-Path $repoRoot $SkillsRelativePath
    $packageJsonPath = Join-Path $repoRoot 'package.json'
    $generatorPath = Join-Path (Join-Path $repoRoot 'scripts') 'generate-skills-index.ps1'
    $indexPath = Join-Path $skillsRoot $SkillsIndexFileName

    $requiredPaths = @(
        @{ Path = $contextPath; Label = 'context file' },
        @{ Path = $skillsRoot; Label = 'skills directory' },
        @{ Path = $packageJsonPath; Label = 'package manifest' },
        @{ Path = $generatorPath; Label = 'skills index generator' }
    )
    foreach ($pointer in $AgentEntrypoints) {
        $requiredPaths += @{
            Path = Join-Path $repoRoot $pointer.Path
            Label = "agent entrypoint $($pointer.Path)"
        }
    }
    foreach ($required in $requiredPaths) {
        if (-not (Test-Path -LiteralPath $required.Path)) {
            Write-LlmError "required $($required.Label) is missing at '$($required.Path)'"
        }
    }

    $skills = @()
    $skillsValid = $true
    if (Test-Path -LiteralPath $skillsRoot) {
        try {
            $skills = @(Get-SkillMetadataList -SkillsRoot $skillsRoot)
        } catch {
            $skillsValid = $false
            Write-LlmError $_.Exception.Message
        }
        if ($skills.Count -eq 0 -and $skillsValid) {
            $skillsValid = $false
            Write-LlmError 'no skills found; add a skill directory with a SKILL.md'
        }
    } else {
        $skillsValid = $false
    }

    if ($skillsValid -and $skills.Count -gt 0) {
        $expectedIndex = ConvertTo-SkillsIndexContent -Skills $skills
        if (-not (Test-Path -LiteralPath $indexPath)) {
            if ($Fix) {
                Write-Utf8NoBom -Path $indexPath -NormalizedText $expectedIndex
                Write-LlmFix "generated missing $SkillsIndexFileName"
            } else {
                Write-LlmError "$SkillsIndexFileName is missing; run lint-llm-instructions.ps1 -Fix"
            }
        } else {
            $actualIndex = Read-Utf8Text $indexPath
            if ($actualIndex -ne $expectedIndex) {
                if ($Fix) {
                    Write-Utf8NoBom -Path $indexPath -NormalizedText $expectedIndex
                    Write-LlmFix "regenerated stale $SkillsIndexFileName"
                } else {
                    Write-LlmError (
                        "$SkillsIndexFileName is out of date; run " +
                        'lint-llm-instructions.ps1 -Fix or scripts/generate-skills-index.ps1'
                    )
                    $expectedLines = $expectedIndex.Split("`n")
                    $actualLines = $actualIndex.Split("`n")
                    $shown = 0
                    for ($i = 0; $i -lt [Math]::Max($expectedLines.Count, $actualLines.Count) -and $shown -lt 5; $i++) {
                        $expectedLine = if ($i -lt $expectedLines.Count) { $expectedLines[$i] } else { '<missing>' }
                        $actualLine = if ($i -lt $actualLines.Count) { $actualLines[$i] } else { '<missing>' }
                        if ($expectedLine -ne $actualLine) {
                            Write-Host "[llm-instructions]   line $($i + 1): expected '$expectedLine' but found '$actualLine'"
                            $shown++
                        }
                    }
                }
            }
        }
    }

    if (Test-Path -LiteralPath $contextPath) {
        $context = Read-Utf8Text $contextPath
        $h1Count = @($context.Split("`n") | Where-Object { $_ -match '^# ' }).Count
        if ($h1Count -ne 1) {
            Write-LlmError "context.md must contain exactly one H1; found $h1Count"
        }
        if (-not $context.Contains('./skills/index.md')) {
            Write-LlmError (
                'context.md must link the generated skills index (./skills/index.md)'
            )
        }

        try {
            $packageVersion = Get-PackageVersion -PackageJsonPath $packageJsonPath
            $versionPattern = '\*\*Version\*\*:\s*([^\r\n]+)'
            $versionMatch = [regex]::Match($context, $versionPattern)
            if (-not $versionMatch.Success) {
                Write-LlmError "context.md must carry a '**Version**: x.y.z' line"
            } else {
                $contextVersion = $versionMatch.Groups[1].Value.Trim()
                if ($contextVersion -ne $packageVersion) {
                    if ($Fix) {
                        if (Update-ContextVersion -ContextPath $contextPath -PackageVersion $packageVersion) {
                            Write-LlmFix "synced context.md version to $packageVersion"
                        }
                    } else {
                        Write-LlmError (
                            "context.md version '$contextVersion' does not match package.json " +
                            "version '$packageVersion'"
                        )
                    }
                }
            }
        } catch {
            Write-LlmError $_.Exception.Message
        }
    }

    foreach ($pointer in $AgentEntrypoints) {
        $pointerPath = Join-Path $repoRoot $pointer.Path
        if (-not (Test-Path -LiteralPath $pointerPath)) {
            continue
        }
        $pointerText = Read-Utf8Text $pointerPath
        if (-not $pointerText.Contains($pointer.Link)) {
            Write-LlmError (
                "agent entrypoint '$($pointer.Path)' must delegate to the context file via " +
                "markdown link '$($pointer.Link)'"
            )
        }
    }

    if ($script:Errors -gt 0) {
        Write-Host "[llm-instructions] FAILED: $($script:Errors) error(s)"
        exit 1
    }
    $skillCount = $skills.Count
    if (-not $VerboseOutput) {
        Write-Host "[llm-instructions] OK: $skillCount skill(s), index fresh, entrypoints delegated"
    } else {
        Write-Host (
            "[llm-instructions] OK: $skillCount skill(s), index fresh, entrypoints delegated " +
            "(categories: $($AllowedSkillCategories -join ', '))"
        )
    }
    exit 0
} catch {
    Write-Host "[llm-instructions] ERROR: $($_.Exception.Message)"
    exit 1
}
