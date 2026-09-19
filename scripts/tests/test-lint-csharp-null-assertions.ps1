Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$lintScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'lint-csharp-null-assertions.js'
Write-Host '== C# null-assertion self-tests =='

function Invoke-NullAssertionLint {
    param([string]$Root, [string[]]$LintArguments = @())

    $hadPrevious = Test-Path Env:CSHARP_NULL_ASSERTION_ROOTS
    $previous = $env:CSHARP_NULL_ASSERTION_ROOTS
    try {
        $env:CSHARP_NULL_ASSERTION_ROOTS = $Root
        $output = & node $lintScript @LintArguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        if ($hadPrevious) {
            $env:CSHARP_NULL_ASSERTION_ROOTS = $previous
        } else {
            Remove-Item Env:CSHARP_NULL_ASSERTION_ROOTS -ErrorAction SilentlyContinue
        }
    }
}

$cleanFixture = @'
using NUnit.Framework;
using UnityEngine;

public sealed class NullComparisonTests
{
    [Test]
    public void ShouldUseExplicitUnityNullComparisons()
    {
        ScriptableObject asset = null;
        Assert.That(asset == null);
        asset = ScriptableObject.CreateInstance<ScriptableObject>();
        Assert.That(asset != null, "asset must be created");
        Object.DestroyImmediate(asset);
        Assert.That(asset == null, "destroyed wrapper must compare null");
        string identifier = asset.GetInstanceID().ToString();
        Assert.That(identifier, Is.Not.Empty);
    }
}
'@

$violatingFixture = @'
using NUnit.Framework;
using UnityEngine;

public sealed class BannedTests
{
    [Test]
    public void ShouldUseExplicitUnityNullComparisons()
    {
        ScriptableObject asset = null;
        Assert.IsNull(asset);
        Assert.IsNotNull(asset, "asset must be created");
    }

    [Test]
    public void ShouldUseExplicitUnityNullConstraints()
    {
        ScriptableObject asset = null;
        Assert.That(asset, Is.Null);
        string identifier = "id";
        Assert.That(identifier, Is.Not.Null.And.Not.Empty);
    }
}
'@

Invoke-TestCase 'Passes_ExplicitUnityNullComparisons' {
    $root = New-TempRoot -Prefix 'null-assertions-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Clean.cs' -Content $cleanFixture
        $result = Invoke-NullAssertionLint -Root $root
        Assert-True ($result.ExitCode -eq 0) "clean fixture should pass: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_OnAssertIsNullAndIsNotNullWithLocations' {
    $root = New-TempRoot -Prefix 'null-assertions-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Banned.cs' -Content $violatingFixture
        $result = Invoke-NullAssertionLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "violating fixture should fail: $($result.Output)"
        Assert-True ($result.Output -match 'Banned\.cs:10') "must report Assert.IsNull line: $($result.Output)"
        Assert-True ($result.Output -match 'Banned\.cs:11') "must report Assert.IsNotNull line: $($result.Output)"
        Assert-True ($result.Output -match 'Banned\.cs:18') "must report Is.Null line: $($result.Output)"
        Assert-True ($result.Output -match 'Banned\.cs:20') "must report Is.Not.Null line: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_OnViolationsInNestedDirectories' {
    $root = New-TempRoot -Prefix 'null-assertions-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Tests/Editor/Nested/Banned.cs' -Content $violatingFixture
        $result = Invoke-NullAssertionLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "nested violation should fail: $($result.Output)"
        Assert-True (
            $result.Output -match 'Tests/Editor/Nested/Banned\.cs:10'
        ) "must report the nested path: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Ignores_CommentsStringsAndNonCSharpFiles' {
    $root = New-TempRoot -Prefix 'null-assertions-'
    try {
        $masked = @'
public sealed class Masked
{
    // Assert.IsNull(reference);
    /* Assert.IsNotNull(reference); */
    // Is.Null(reference);
    /* Is.Not.Null.And.Not.Empty */
    private const string Usage = "Assert.IsNull(reference);";
    private readonly string[] usages = { "Assert.IsNotNull(reference);", "Is.Null" };

    public void Run()
    {
        string message = $"Assert.IsNull(reference);";
        System.Console.WriteLine(message);
    }
}
'@
        Write-FixtureFile -Root $root -RelativePath 'Masked.cs' -Content $masked
        Write-FixtureFile -Root $root -RelativePath 'notes.md' -Content 'Assert.IsNull(x);'
        $result = Invoke-NullAssertionLint -Root $root
        Assert-True (
            $result.ExitCode -eq 0
        ) "masked occurrences and non-C# files must pass: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "null-assertion self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'null-assertion self-tests: all passed'
exit 0
