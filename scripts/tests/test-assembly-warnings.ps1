Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$lintScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'lint-assembly-warnings.ps1'
$assemblyDefinition = '{"name":"Fixture.Assembly"}'
$warningsAsErrorsRuleset = @'
<?xml version="1.0" encoding="utf-8"?>
<RuleSet Name="Warnings as errors" Description="Test fixture" ToolsVersion="10.0">
  <IncludeAll Action="Error" />
</RuleSet>
'@
$maximumWarningsResponseFile = '-warn:4'

function Write-AssemblyFixture {
    param(
        [string]$Root,
        [string]$Directory,
        [string]$Ruleset = $warningsAsErrorsRuleset
    )

    Write-FixtureFile -Root $Root -RelativePath "$Directory/Fixture.asmdef" -Content $assemblyDefinition
    Write-FixtureFile -Root $Root -RelativePath "$Directory/WarningsAsErrors.ruleset" -Content $Ruleset
    Write-FixtureFile -Root $Root -RelativePath "$Directory/csc.rsp" -Content $maximumWarningsResponseFile
}

Write-Host '== assembly warnings policy self-tests =='

Invoke-TestCase 'Passes_ForRepositoryAssemblies' {
    $repoRoot = (Get-Item (Join-Path $PSScriptRoot '../..')).FullName

    & $lintScript -Root $repoRoot *> $null
    Assert-ExitCode 0 'repository assemblies should satisfy the warning policy'
}

Invoke-TestCase 'Passes_When_EveryAssemblyHasWarningsAsErrors' {
    $root = New-TempRoot
    try {
        Write-AssemblyFixture -Root $root -Directory 'Runtime'
        Write-AssemblyFixture -Root $root -Directory 'Editor/Nested'

        & $lintScript -Root $root *> $null
        Assert-ExitCode 0 'complete warnings-as-errors coverage should pass'
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenAssemblyHasNoRuleset' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath 'Runtime/Fixture.asmdef' -Content $assemblyDefinition

        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'an assembly without a ruleset should fail'
        Assert-True ($output -match 'Runtime/Fixture\.asmdef') "output should name the assembly, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenAssemblyHasNoCompilerResponseFile' {
    $root = New-TempRoot
    try {
        Write-FixtureFile -Root $root -RelativePath 'Runtime/Fixture.asmdef' -Content $assemblyDefinition
        Write-FixtureFile `
            -Root $root `
            -RelativePath 'Runtime/WarningsAsErrors.ruleset' `
            -Content $warningsAsErrorsRuleset

        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'an assembly without csc.rsp should fail'
        Assert-True ($output -match 'expected exactly one named csc\.rsp') "output should require csc.rsp, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenAssemblyHasAmbiguousCompilerResponseFiles' {
    $root = New-TempRoot
    try {
        Write-AssemblyFixture -Root $root -Directory 'Runtime'
        Write-FixtureFile -Root $root -RelativePath 'Runtime/mcs.rsp' -Content '-warn:4'

        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'an assembly with ambiguous response files should fail'
        Assert-True ($output -match '2 compiler response files') "output should report the ambiguity, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenCompilerWarningLevelIsLowerThanMaximum' {
    $root = New-TempRoot
    try {
        Write-AssemblyFixture -Root $root -Directory 'Runtime'
        Write-FixtureFile -Root $root -RelativePath 'Runtime/csc.rsp' -Content '-warn:3'

        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'a lower compiler warning level should fail'
        Assert-True ($output -match 'maximum warning-level argument') "output should require maximum warnings, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenCompilerWarningLevelIsMalformed' {
    $root = New-TempRoot
    try {
        Write-AssemblyFixture -Root $root -Directory 'Runtime'
        Write-FixtureFile -Root $root -RelativePath 'Runtime/csc.rsp' -Content '-warn:all'

        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'a malformed compiler warning level should fail'
        Assert-True ($output -match 'maximum warning-level argument') "output should require -warn:4, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenAssemblyHasMultipleRulesets' {
    $root = New-TempRoot
    try {
        Write-AssemblyFixture -Root $root -Directory 'Runtime'
        Write-FixtureFile -Root $root -RelativePath 'Runtime/Override.ruleset' -Content $warningsAsErrorsRuleset

        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'ambiguous assembly rulesets should fail'
        Assert-True ($output -match '2 rulesets') "output should report the ambiguity, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenRulesetIsMalformed' {
    $root = New-TempRoot
    try {
        Write-AssemblyFixture -Root $root -Directory 'Runtime' -Ruleset '<RuleSet><IncludeAll'

        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'malformed ruleset XML should fail'
        Assert-True ($output -match 'valid XML') "output should explain malformed XML, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenIncludeAllDoesNotPromoteWarnings' {
    $root = New-TempRoot
    try {
        $warningRuleset = @'
<RuleSet Name="Warnings only" Description="Test fixture" ToolsVersion="10.0">
  <IncludeAll Action="Warning" />
</RuleSet>
'@
        Write-AssemblyFixture -Root $root -Directory 'Runtime' -Ruleset $warningRuleset

        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'a non-error IncludeAll action should fail'
        Assert-True ($output -match 'IncludeAll Action="Error"') "output should name the policy, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenCompilerResponseFileLoadsPackageAnalyzer' {
    $root = New-TempRoot
    try {
        Write-AssemblyFixture -Root $root -Directory 'Runtime'
        Write-FixtureFile `
            -Root $root `
            -RelativePath 'Runtime/csc.rsp' `
            -Content "-warn:4`n-analyzer:`"Runtime/Analyzer.dll`""

        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'a package-local analyzer argument should fail'
        Assert-True ($output -match 'must not load analyzers') "output should reject package-local analyzers, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenRepositoryContainsLabeledAnalyzer' {
    $root = New-TempRoot
    try {
        Write-AssemblyFixture -Root $root -Directory 'Runtime'
        Write-FixtureFile -Root $root -RelativePath 'Tools/Analyzer.dll.meta' -Content "labels:`n- RoslynAnalyzer"

        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'a repository-local labeled analyzer should fail'
        Assert-True ($output -match 'repository-local analyzer payload') "output should reject the analyzer payload, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenRepositoryContainsKnownAnalyzerPayload' {
    $root = New-TempRoot
    try {
        Write-AssemblyFixture -Root $root -Directory 'Runtime'
        Write-FixtureFile `
            -Root $root `
            -RelativePath 'Tools/ErrorProne.NET.NOTICE.md' `
            -Content 'development analyzer notice'

        $output = & $lintScript -Root $root *>&1 | Out-String
        Assert-ExitCode 1 'a known repository-local analyzer payload should fail'
        Assert-True ($output -match 'repository-local analyzer payload') "output should reject the known analyzer payload, got: $output"
    } finally {
        Remove-TempRoot $root
    }
}

Write-Host "== assembly warnings policy: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
