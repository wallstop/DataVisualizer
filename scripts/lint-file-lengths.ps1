[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$Root,
    [string[]]$Paths,
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LlmConfig.psm1') -DisableNameChecking

function Format-DisplayPath {
    param([string]$FullPath, [string]$BasePath)
    $relative = $FullPath.Substring($BasePath.Length)
    return $relative.Replace('\', '/').TrimStart('/')
}

function Test-IsUnderLlmDirectory {
    param([string]$FullPath, [string]$LlmPath)
    return $FullPath.StartsWith(
        $LlmPath + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase
    ) -or $FullPath -eq $LlmPath
}

function Get-FileLineCount {
    param([string]$Path)
    $text = Read-Utf8Text $Path
    return Get-PhysicalLineCount $text
}

try {
    $repoRoot = Resolve-LlmRoot $Root
    $llmPath = Join-Path $repoRoot '.llm'
    $targets = @()

    if ($Paths -and $Paths.Count -gt 0) {
        foreach ($relativePath in $Paths) {
            $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $relativePath))
            if (-not (Test-IsUnderLlmDirectory -FullPath $fullPath -LlmPath $llmPath)) {
                Write-Host "[file-length] ERROR: path '$relativePath' is outside the .llm directory"
                exit 1
            }
            if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
                Write-Host "[file-length] ERROR: path '$relativePath' does not exist"
                exit 1
            }
            if (-not $fullPath.EndsWith('.md', [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-Host "[file-length] ERROR: path '$relativePath' is not a markdown file"
                exit 1
            }
            $targets += , $fullPath
        }
    } else {
        if (-not (Test-Path -LiteralPath $llmPath)) {
            Write-Host '[file-length] no .llm directory found; checking planning docs only'
            $targets = @()
        } else {
            $targets = @(
                Get-ChildItem -Path $llmPath -Recurse -Filter '*.md' -File |
                ForEach-Object { $_.FullName }
            )
        }
    }

    $generatedIndexPath = Join-Path $llmPath (
        Join-Path 'skills' $SkillsIndexFileName
    )
    $errors = 0
    $warnings = 0
    $ordered = [System.Linq.Enumerable]::OrderBy(
        [string[]]$targets,
        [System.Func[string, string]] { param($path) $path },
        [System.StringComparer]::Ordinal
    )
    foreach ($target in $ordered) {
        if ($target -eq $generatedIndexPath) {
            continue
        }
        $count = Get-FileLineCount $target
        $displayPath = Format-DisplayPath -FullPath $target -BasePath $repoRoot
        if ($count -gt $MaxFileLines) {
            Write-Host (
                "[file-length] ERROR: $displayPath has $count lines and exceeds the " +
                "$MaxFileLines-line hard limit; split the file"
            )
            $errors++
        } elseif ($count -ge $WarnFileLines) {
            if ($VerboseOutput) {
                Write-Host (
                    "[file-length] WARNING: $displayPath has $count lines and is near the " +
                    "$MaxFileLines-line hard limit; consider splitting"
                )
            }
            $warnings++
        }
    }

    foreach ($planDoc in $PlanningDocPaths) {
        $planPath = Join-Path $repoRoot $planDoc
        if (-not (Test-Path -LiteralPath $planPath -PathType Leaf)) {
            continue
        }
        $count = Get-FileLineCount $planPath
        if ($count -gt $MaxPlanningDocLines) {
            Write-Host (
                "[file-length] ERROR: $planDoc has $count lines and exceeds the " +
                "$MaxPlanningDocLines-line planning-doc hard limit; keep only current and " +
                'future work here and relocate history to progress/ and context to .llm/'
            )
            $errors++
        } elseif ($count -ge $WarnPlanningDocLines) {
            if ($VerboseOutput) {
                Write-Host (
                    "[file-length] WARNING: $planDoc has $count lines and is near the " +
                    "$MaxPlanningDocLines-line planning-doc hard limit; consider splitting"
                )
            }
            $warnings++
        }
    }

    if ($errors -gt 0) {
        Write-Host (
            "[file-length] FAILED: $errors file(s) exceed the line-length hard limit"
        )
        exit 1
    }
    if ($VerboseOutput -and $warnings -gt 0) {
        Write-Host (
            "[file-length] OK: all checked files are within the line-length hard limits " +
            "($warnings file(s) near a limit)"
        )
    } else {
        Write-Host '[file-length] OK: all checked files are within the line-length hard limits'
    }
    exit 0
} catch {
    Write-Host "[file-length] ERROR: $($_.Exception.Message)"
    exit 1
}
