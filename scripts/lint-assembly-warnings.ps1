[CmdletBinding(PositionalBinding = $false)]
param([string]$Root)

$ErrorActionPreference = 'Stop'

function Format-DisplayPath {
    param([string]$FullPath, [string]$BasePath)

    return $FullPath.Substring($BasePath.Length).Replace('\', '/').TrimStart('/')
}

try {
    if ([string]::IsNullOrWhiteSpace($Root)) {
        $Root = Split-Path -Parent $PSScriptRoot
    }
    $repoRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
    if (-not (Test-Path -LiteralPath $repoRoot -PathType Container)) {
        throw "root directory does not exist: $repoRoot"
    }

    $assemblyDefinitions = @(
        Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter '*.asmdef' -File |
        Sort-Object -Property FullName
    )
    if ($assemblyDefinitions.Count -eq 0) {
        Write-Host '[assembly-warnings] ERROR: no assembly definitions found'
        exit 1
    }

    $errors = 0
    foreach ($assemblyDefinition in $assemblyDefinitions) {
        $displayPath = Format-DisplayPath -FullPath $assemblyDefinition.FullName -BasePath $repoRoot
        $rulesets = @(Get-ChildItem -LiteralPath $assemblyDefinition.DirectoryName -Filter '*.ruleset' -File)
        if ($rulesets.Count -ne 1) {
            Write-Host (
                "[assembly-warnings] ERROR: $displayPath has $($rulesets.Count) rulesets in its " +
                'directory; expected exactly 1'
            )
            $errors++
            continue
        }

        $ruleset = $rulesets[0]
        try {
            [xml]$document = Get-Content -LiteralPath $ruleset.FullName -Raw
        } catch {
            $rulesetPath = Format-DisplayPath -FullPath $ruleset.FullName -BasePath $repoRoot
            Write-Host "[assembly-warnings] ERROR: $rulesetPath is not valid XML: $($_.Exception.Message)"
            $errors++
            continue
        }

        $errorIncludeAll = @($document.SelectNodes('/RuleSet/IncludeAll[@Action="Error"]'))
        $allIncludeAll = @($document.SelectNodes('/RuleSet/IncludeAll'))
        $nonErrorRules = @($document.SelectNodes('/RuleSet/Rules/Rule[@Action!="Error"]'))
        $includedRulesets = @($document.SelectNodes('/RuleSet/Include'))
        $hasStrictWarningPolicy = (
            $document.DocumentElement.LocalName -eq 'RuleSet' -and
            $errorIncludeAll.Count -eq 1 -and
            $allIncludeAll.Count -eq 1 -and
            $nonErrorRules.Count -eq 0 -and
            $includedRulesets.Count -eq 0
        )
        if (-not $hasStrictWarningPolicy) {
            $rulesetPath = Format-DisplayPath -FullPath $ruleset.FullName -BasePath $repoRoot
            Write-Host (
                "[assembly-warnings] ERROR: $rulesetPath must contain exactly one " +
                'IncludeAll Action="Error" and may not weaken or import warning rules'
            )
            $errors++
        }
    }

    if (0 -lt $errors) {
        Write-Host "[assembly-warnings] FAILED: $errors assembly warning policy error(s)"
        exit 1
    }

    Write-Host (
        "[assembly-warnings] OK: all $($assemblyDefinitions.Count) assembly definition(s) " +
        'treat warnings as errors'
    )
    exit 0
} catch {
    Write-Host "[assembly-warnings] ERROR: $($_.Exception.Message)"
    exit 1
}
