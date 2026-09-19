Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$lintScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'lint-csharp-usings.js'
Write-Host '== C# using-placement self-tests =='

function Invoke-UsingLint {
    param([string]$Root, [string[]]$LintArguments = @())

    $hadPrevious = Test-Path Env:CSHARP_USINGS_ROOTS
    $previous = $env:CSHARP_USINGS_ROOTS
    try {
        $env:CSHARP_USINGS_ROOTS = $Root
        $output = & node $lintScript @LintArguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        if ($hadPrevious) {
            $env:CSHARP_USINGS_ROOTS = $previous
        } else {
            Remove-Item Env:CSHARP_USINGS_ROOTS -ErrorAction SilentlyContinue
        }
    }
}

$compliantFixture = @'
namespace Demo
{
    using System;
    using System.Collections.Generic;
    using Inspector = UnityEditor.Editor;

    internal sealed class Compliant
    {
        private readonly List<int> values = new List<int>();
    }
}
'@

$misplacedFixture = @'
// Misplaced.cs carries a header comment above the misplaced usings.
using System;
using System.Collections.Generic;

namespace Demo
{
    internal sealed class Misplaced
    {
        private readonly List<int> values = new List<int>();
    }
}
'@

$sanctionedPreambleFixture = @'
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Other.Assembly")]

namespace Demo
{
    using System;

    internal sealed class Sanctioned { }
}
'@

$namespacelessFixture = @'
using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Sonar Code Smell", "S101")]

'@

$maskedFixture = @'
namespace Demo
{
    // using System;
    /* using UnityEngine; */
    private const string Usage = "using System;";

    internal sealed class Masked { }
}
'@

$barrierFixture = @'
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Demo
{
    internal sealed class Barrier { }
}
'@

Invoke-TestCase 'Passes_UsingsInsideNamespace' {
    $root = New-TempRoot -Prefix 'usings-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Compliant.cs' -Content $compliantFixture
        $result = Invoke-UsingLint -Root $root
        Assert-True ($result.ExitCode -eq 0) "compliant fixture should pass: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_OnUsingsOutsideNamespaceWithLocations' {
    $root = New-TempRoot -Prefix 'usings-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Misplaced.cs' -Content $misplacedFixture
        $result = Invoke-UsingLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "misplaced fixture should fail: $($result.Output)"
        Assert-True ($result.Output -match 'Misplaced\.cs:2') "must report the first using line: $($result.Output)"
        Assert-True ($result.Output -match 'Misplaced\.cs:3') "must report the second using line: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Passes_SanctionedAssemblyAttributeShapes' {
    $root = New-TempRoot -Prefix 'usings-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Sanctioned.cs' -Content $sanctionedPreambleFixture
        Write-FixtureFile -Root $root -RelativePath 'Namespaceless.cs' -Content $namespacelessFixture
        $result = Invoke-UsingLint -Root $root
        Assert-True (
            $result.ExitCode -eq 0
        ) "assembly-attribute preambles and namespaceless files must pass: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Ignores_CommentsStringsAndNonCSharpFiles' {
    $root = New-TempRoot -Prefix 'usings-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Masked.cs' -Content $maskedFixture
        Write-FixtureFile -Root $root -RelativePath 'notes.md' -Content 'using System;'
        $result = Invoke-UsingLint -Root $root
        Assert-True (
            $result.ExitCode -eq 0
        ) "masked occurrences and non-C# files must pass: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fix_MovesUsingsInsideNamespaceAndRepairsExitCode' {
    $root = New-TempRoot -Prefix 'usings-'
    try {
        $relativePath = 'Misplaced.cs'
        Write-FixtureFile -Root $root -RelativePath $relativePath -Content $misplacedFixture
        $fixturePath = Get-FixturePath -Root $root -RelativePath $relativePath
        $before = Get-Content -LiteralPath $fixturePath -Raw
        $result = Invoke-UsingLint -Root $root -LintArguments @('--fix')
        Assert-True ($result.ExitCode -eq 0) "fix should leave the tree clean: $($result.Output)"
        Assert-True ($result.Output -match 'Misplaced\.cs') "fix should name the repaired file: $($result.Output)"

        $after = Get-Content -LiteralPath $fixturePath -Raw
        Assert-True ($after -ne $before) "fix must rewrite the fixture"
        Assert-True (
            $after -match 'header comment above the misplaced usings'
        ) "fix must preserve header comments"
        Assert-True (
            $after -match 'namespace Demo\s*\{\s*\r?\n\s*using System;'
        ) "usings must move inside the namespace: $after"

        $verification = Invoke-UsingLint -Root $root
        Assert-True ($verification.ExitCode -eq 0) "post-fix scan must pass: $($verification.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fix_DeclinesBarrierRegionsWithoutRewriting' {
    $root = New-TempRoot -Prefix 'usings-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Barrier.cs' -Content $barrierFixture
        $fixturePath = Get-FixturePath -Root $root -RelativePath 'Barrier.cs'
        $before = Get-Content -LiteralPath $fixturePath -Raw
        $result = Invoke-UsingLint -Root $root -LintArguments @('--fix')
        Assert-True ($result.ExitCode -eq 1) "barrier region must keep the failure: $($result.Output)"
        Assert-True (
            $result.Output -match 'manual barrier'
        ) "fix must report the declined barrier: $($result.Output)"
        $after = Get-Content -LiteralPath $fixturePath -Raw
        Assert-True ($after -eq $before) "fix must not rewrite a barrier region"
    } finally {
        Remove-TempRoot $root
    }
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "using-placement self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'using-placement self-tests: all passed'
exit 0
