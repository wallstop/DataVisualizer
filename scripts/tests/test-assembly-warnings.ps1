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
$maximumWarningsResponseFile = '-warn:5'

function Write-AssemblyFixture {
    param(
        [string]$Root,
        [string]$Directory,
        [string]$Ruleset = $warningsAsErrorsRuleset,
        [string]$ResponseFile = $maximumWarningsResponseFile
    )

    Write-FixtureFile -Root $Root -RelativePath "$Directory/Fixture.asmdef" -Content $assemblyDefinition
    Write-FixtureFile -Root $Root -RelativePath "$Directory/WarningsAsErrors.ruleset" -Content $Ruleset
    Write-FixtureFile -Root $Root -RelativePath "$Directory/csc.rsp" -Content $ResponseFile
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

# Each row is one response file and the outcome the guard owes it. The
# suppression rows are the shapes a prefix list accepts by not naming them: a
# switch it never reads turns off the policy it claims to enforce. The last rows
# are constructs the guard does not model at all, so it refuses them instead of
# guessing what they do.
$responseFileCases = @(
    [pscustomobject]@{
        Name = 'Passes_WhenTheResponseFileCarriesOnlyTheMaximumWarningLevel'
        Content = '-warn:5'
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        Name = 'Passes_WhenTheResponseFileUsesTheShortWarningLevelForm'
        Content = '-w:5'
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        Name = 'Passes_WhenTheResponseFileUsesTheSlashSpelling'
        Content = '/warn:5'
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        # Blank lines are not arguments, and most committed files end with one.
        Name = 'Passes_WhenTheResponseFileEndsWithBlankLines'
        Content = "-warn:5`n`n"
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheWarningLevelIsLowerThanMaximum'
        Content = '-warn:3'
        ExpectPass = $false
        Expect = 'found: -warn:3'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheWarningLevelIsMalformed'
        Content = '-warn:all'
        ExpectPass = $false
        Expect = 'found: -warn:all'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheResponseFileSuppressesNamedWarnings'
        Content = "-warn:5`n-nowarn:1701,CS0162`n"
        ExpectPass = $false
        Expect = 'found: -warn:5 -nowarn:1701,CS0162'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheResponseFileSuppressesEveryWarning'
        Content = "-warn:5`n-nowarn`n"
        ExpectPass = $false
        Expect = 'found: -warn:5 -nowarn'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheResponseFileUsesTheSlashSuppressionForm'
        Content = "-warn:5`n/nowarn`n"
        ExpectPass = $false
        Expect = 'found: -warn:5 /nowarn'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheResponseFileDisablesWarningsAsErrors'
        Content = "-warn:5`n-warnaserror-`n"
        ExpectPass = $false
        Expect = 'found: -warn:5 -warnaserror-'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheResponseFileDisablesWarningsAsErrorsWithTheSlashForm'
        Content = "/warn:5`n/warnaserror-`n"
        ExpectPass = $false
        Expect = 'found: /warn:5 /warnaserror-'
    }
    [pscustomobject]@{
        # The ruleset already promotes every warning, so a second switch either
        # way gives the one policy a second source of truth.
        Name = 'Fails_WhenTheResponseFileEnablesWarningsAsErrorsSeparately'
        Content = "-warn:5`n-warnaserror+`n"
        ExpectPass = $false
        Expect = 'found: -warn:5 -warnaserror\+'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheResponseFileLoadsAPackageAnalyzer'
        Content = "-warn:5`n-analyzer:`"Runtime/Analyzer.dll`"`n"
        ExpectPass = $false
        Expect = 'found: -warn:5 -analyzer:"Runtime/Analyzer\.dll"'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheResponseFileHidesASuppressionBehindAComment'
        Content = "-warn:5`n# -nowarn:1701`n"
        ExpectPass = $false
        Expect = 'found: -warn:5 # -nowarn:1701'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheResponseFilePacksTwoArgumentsOnOneLine'
        Content = '-warn:5 -nowarn:1701'
        ExpectPass = $false
        Expect = 'must contain exactly one argument'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheResponseFileRepeatsTheWarningLevel'
        Content = "-warn:5`n-warn:5`n"
        ExpectPass = $false
        Expect = 'found: -warn:5 -warn:5'
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheResponseFileIsEmpty'
        Content = ''
        ExpectPass = $false
        Expect = 'found: no arguments'
    }
)

foreach ($script:responseFileCase in $responseFileCases) {
    Invoke-TestCase $script:responseFileCase.Name {
        $root = New-TempRoot -Prefix 'assembly-warnings-'
        try {
            Write-AssemblyFixture `
                -Root $root `
                -Directory 'Runtime' `
                -ResponseFile $script:responseFileCase.Content

            $output = & $lintScript -Root $root *>&1 | Out-String
            if ($script:responseFileCase.ExpectPass) {
                Assert-ExitCode 0 "$($script:responseFileCase.Name) should pass"
            } else {
                Assert-ExitCode 1 "$($script:responseFileCase.Name) should fail"
                Assert-True (
                    $output -match 'Runtime/csc\.rsp'
                ) "the response file should be named, got: $output"
                Assert-True (
                    $output -match $script:responseFileCase.Expect
                ) "expected '$($script:responseFileCase.Expect)' in: $output"
            }
        } finally {
            Remove-TempRoot $root
        }
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
