[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Root,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LlmConfig.psm1') -DisableNameChecking

try {
    $repoRoot = Resolve-LlmRoot $Root
    $skillsRoot = Join-Path $repoRoot $SkillsRelativePath
    if (-not (Test-Path -LiteralPath $skillsRoot)) {
        Write-Host (
            "[skills-index] ERROR: no skills found; skills directory '$SkillsRelativePath' " +
            'does not exist'
        )
        exit 1
    }

    $skillDirectories = @(Get-ChildItem -Path $skillsRoot -Directory)
    if ($skillDirectories.Count -eq 0) {
        Write-Host '[skills-index] ERROR: no skills found; add a skill directory with a SKILL.md'
        exit 1
    }

    $skills = @()
    foreach ($directory in $skillDirectories) {
        $skillPath = Join-Path $directory.FullName 'SKILL.md'
        if (-not (Test-Path -LiteralPath $skillPath)) {
            Write-Host (
                "[skills-index] ERROR: skill directory '$($directory.Name)' is missing SKILL.md"
            )
            exit 1
        }
        try {
            $skills += Read-SkillMetadata -SkillPath $skillPath -ExpectedDirectory $directory.Name
        } catch {
            Write-Host (
                "[skills-index] ERROR: $($directory.Name)/SKILL.md :: $($_.Exception.Message)"
            )
            exit 1
        }
    }

    $content = ConvertTo-SkillsIndexContent -Skills $skills
    $targetPath = $OutputPath
    if ([string]::IsNullOrWhiteSpace($targetPath)) {
        $targetPath = Join-Path $skillsRoot $SkillsIndexFileName
    }
    Write-Utf8NoBom -Path $targetPath -NormalizedText $content
    Write-Host (
        "[skills-index] wrote $targetPath ($($skills.Count) skill(s) across " +
        "$($AllowedSkillCategories.Count) categories)"
    )
    exit 0
} catch {
    Write-Host "[skills-index] ERROR: $($_.Exception.Message)"
    exit 1
}
