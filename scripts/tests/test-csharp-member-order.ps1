Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$lintScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'lint-csharp-member-order.js'
Write-Host '== C# member-order self-tests =='

function Invoke-MemberOrderLint {
    param([string]$Root, [string[]]$LintArguments = @())

    $hadPrevious = Test-Path Env:CSHARP_MEMBER_ORDER_ROOTS
    $previous = $env:CSHARP_MEMBER_ORDER_ROOTS
    try {
        $env:CSHARP_MEMBER_ORDER_ROOTS = $Root
        $output = & node $lintScript @LintArguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        if ($hadPrevious) {
            $env:CSHARP_MEMBER_ORDER_ROOTS = $previous
        } else {
            Remove-Item Env:CSHARP_MEMBER_ORDER_ROOTS -ErrorAction SilentlyContinue
        }
    }
}

$orderedFixture = @'
using System;
public sealed class Ordered
{
    public const int Limit = 1;
    public event Action Changed;
    public delegate void Callback();
    public static int SharedValue { get; set; }
    public static int SharedField;
    public int Value { get; set; }
    public int Field;
    public Ordered() { }
    public static void Reset() { }
    public void Run() { }
    private sealed class Nested { }
}
'@

Invoke-TestCase 'Passes_CanonicalOrder' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Ordered.cs' -Content $orderedFixture
        $result = Invoke-MemberOrderLint -Root $root
        Assert-True ($result.ExitCode -eq 0) "ordered fixture should pass: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_OnTierAndAccessInversions' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Bad.cs' -Content @'
public sealed class Bad
{
    private int _field;
    public int Value { get; set; }
    public int PublicField;
}
'@
        $result = Invoke-MemberOrderLint -Root $root
        Assert-True ($result.ExitCode -eq 1) 'misordered fixture should fail'
        Assert-True ($result.Output -match '#672') "failure should identify the ordering rule: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_OnUnderscoredMethodName' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'BadMethodName.cs' -Content @'
public sealed class BadMethodName
{
    public void Should_Run() { }
}
'@
        $result = Invoke-MemberOrderLint -Root $root
        Assert-True ($result.ExitCode -eq 1) 'underscored method name should fail'
        Assert-True ($result.Output -match '#47') "failure should identify the naming rule: $($result.Output)"
        Assert-True ($result.Output -match 'Should_Run') "failure should identify the method: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_OnUnderscoredGenericMethodName' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'BadGenericMethodName.cs' -Content @'
public sealed class BadGenericMethodName
{
    public void Should_Run<T>() { }
}
'@
        $result = Invoke-MemberOrderLint -Root $root
        Assert-True ($result.ExitCode -eq 1) 'underscored generic method name should fail'
        Assert-True ($result.Output -match 'Should_Run') "failure should identify the generic method: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_OnConsecutiveOrdinaryLineComments' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'BadComment.cs' -Content @'
public sealed class BadComment
{
    // First line of one explanation.
    // Second line of the same explanation.
    public void Run() { }
}
'@
        $result = Invoke-MemberOrderLint -Root $root
        Assert-True ($result.ExitCode -eq 1) 'consecutive ordinary line comments should fail'
        Assert-True ($result.Output -match '#57') "failure should identify the comment rule: $($result.Output)"
        Assert-True ($result.Output -match 'BadComment.cs:3') "failure should identify the first line: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_OnLeadingBomConsecutiveOrdinaryLineComments' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        $content = [char]0xFEFF + @'
// First line of one explanation.
// Second line of the same explanation.
public sealed class BadBomComment { }
'@
        Write-FixtureFile -Root $root -RelativePath 'BadBomComment.cs' -Content $content
        $result = Invoke-MemberOrderLint -Root $root
        Assert-True ($result.ExitCode -eq 1) 'a BOM must not hide leading consecutive comments'
        Assert-True ($result.Output -match 'BadBomComment.cs:1') "failure should identify the first line: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Passes_SupportedCommentForms' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'SupportedComments.cs' -Content @'
// ReSharper disable FirstInspection
// ReSharper disable SecondInspection
public sealed class SupportedComments
{
    /// <summary>
    /// XML documentation remains line-oriented.
    /// </summary>
    public int Value { get; set; }

    public string Example { get; } = @"
// Content inside a multi-line string is not a comment.
// Consecutive content lines must remain valid.
";

    /*
        Ordinary explanations spanning lines use an indented block.
        Single-line comments remain valid too.
    */
    public void Run() { }

    /*
// Content inside a block comment is not a line-comment block.
// Its leading markers must remain valid too.
    */
    // One standalone note is valid.
    private void Stop() { }
}
'@
        $result = Invoke-MemberOrderLint -Root $root
        Assert-True ($result.ExitCode -eq 0) "supported comment forms should pass: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_OnRuntimeLinqImportsAndQualifiedCalls' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Runtime/UsesLinq.cs' -Content @'
namespace Fixture
{
    using System.Linq;
    using Query = global::System.Linq.Enumerable;
    using static System.Linq.Enumerable;

    public sealed class UsesLinq
    {
        public int Count(int[] values) { return System.Linq.Enumerable.Count(values); }
    }
}
'@
        $result = Invoke-MemberOrderLint -Root $root
        Assert-True ($result.ExitCode -eq 1) 'Runtime LINQ dependencies should fail'
        Assert-True ($result.Output -match '#61') "failure should identify the LINQ rule: $($result.Output)"
        Assert-True ($result.Output -match 'Runtime/UsesLinq.cs:3') "failure should identify the first import: $($result.Output)"
        Assert-True ($result.Output -match 'Runtime/UsesLinq.cs:4') "failure should identify the aliased import: $($result.Output)"
        Assert-True ($result.Output -match 'Runtime/UsesLinq.cs:5') "failure should identify the static import: $($result.Output)"
        Assert-True ($result.Output -match 'Runtime/UsesLinq.cs:9') "failure should identify the qualified call: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Passes_RuntimeLinqTextInCommentsAndStrings' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Runtime/NoLinqDependency.cs' -Content @'
public sealed class NoLinqDependency
{
    public string Example { get; } = "System.Linq.Enumerable";

    // System.Linq is named only as documentation.
    public void Run() { }
}
'@
        $result = Invoke-MemberOrderLint -Root $root
        Assert-True ($result.ExitCode -eq 0) "comments and strings should not create LINQ violations: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Passes_EditorLinqDependency' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Editor/UsesLinq.cs' -Content @'
namespace Fixture
{
    using System.Linq;

    public sealed class UsesLinq { }
}
'@
        $result = Invoke-MemberOrderLint -Root $root
        Assert-True ($result.ExitCode -eq 0) "the Runtime-only rule should not reject Editor LINQ: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Fails_WhenNestedTypeIsInterspersed' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Nested.cs' -Content @'
public sealed class NestedFixture
{
    public void First() { }
    private sealed class Nested { }
    public void Second() { }
}
'@
        $result = Invoke-MemberOrderLint -Root $root
        Assert-True ($result.ExitCode -eq 1) 'interspersed nested type should fail'
        Assert-True ($result.Output -match 'nested class') "failure should identify the nested type: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'FixesTierOrderByExactPermutation' {
    $root = New-TempRoot -Prefix 'member-order-'
    try {
        Write-FixtureFile -Root $root -RelativePath 'Fixable.cs' -Content @'
public sealed class Fixable
{
    private int _field;
    public int Value { get; set; }
    public void Run() { string marker = "body-preserved"; }
}
'@
        $fixed = Invoke-MemberOrderLint -Root $root -LintArguments @('--fix')
        Assert-True ($fixed.ExitCode -eq 0) "fixable fixture should be repaired: $($fixed.Output)"
        $verified = Invoke-MemberOrderLint -Root $root
        Assert-True ($verified.ExitCode -eq 0) "repaired fixture should pass: $($verified.Output)"
        $content = Get-Content -LiteralPath (Get-FixturePath $root 'Fixable.cs') -Raw
        Assert-True ($content.IndexOf('Value') -lt $content.IndexOf('_field')) 'property should precede field'
        Assert-True ($content -match 'body-preserved') 'fix must preserve member bodies'
    } finally {
        Remove-TempRoot $root
    }
}

if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
