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

function Write-AssemblyFixture {
    param(
        [string]$Root,
        [string]$Directory,
        [string]$Ruleset = $warningsAsErrorsRuleset
    )

    Write-FixtureFile -Root $Root -RelativePath "$Directory/Fixture.asmdef" -Content $assemblyDefinition
    Write-FixtureFile -Root $Root -RelativePath "$Directory/WarningsAsErrors.ruleset" -Content $Ruleset
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

Write-Host "== assembly warnings policy: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
