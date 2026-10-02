Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$lintScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'lint-line-endings.js'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Write-Host '== Line-ending contract self-tests =='

function Invoke-LineEndingLint {
    param(
        [string]$EditorConfig,
        [string]$GitAttributes,
        [string[]]$LintArguments = @()
    )

    $hadEditorConfig = Test-Path Env:LINE_ENDINGS_EDITORCONFIG
    $previousEditorConfig = $env:LINE_ENDINGS_EDITORCONFIG
    $hadGitAttributes = Test-Path Env:LINE_ENDINGS_GITATTRIBUTES
    $previousGitAttributes = $env:LINE_ENDINGS_GITATTRIBUTES
    try {
        $env:LINE_ENDINGS_EDITORCONFIG = $EditorConfig
        $env:LINE_ENDINGS_GITATTRIBUTES = $GitAttributes
        $output = & node $lintScript @LintArguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        if ($hadEditorConfig) {
            $env:LINE_ENDINGS_EDITORCONFIG = $previousEditorConfig
        } else {
            Remove-Item Env:LINE_ENDINGS_EDITORCONFIG -ErrorAction SilentlyContinue
        }
        if ($hadGitAttributes) {
            $env:LINE_ENDINGS_GITATTRIBUTES = $previousGitAttributes
        } else {
            Remove-Item Env:LINE_ENDINGS_GITATTRIBUTES -ErrorAction SilentlyContinue
        }
    }
}

# The lint resolves its overrides against the repository root, so fixtures pass
# absolute paths.
function Invoke-FixtureLint {
    param([string]$Root, [string[]]$LintArguments = @())

    return Invoke-LineEndingLint `
        -EditorConfig (Join-Path $Root '.editorconfig') `
        -GitAttributes (Join-Path $Root '.gitattributes') `
        -LintArguments $LintArguments
}

$editorConfigLf = @'
root = true

[*]
end_of_line = lf
'@

# The configuration that shipped the failure: the editor asks for CRLF for every
# file and git checks out the platform default.
$editorConfigCrlf = @'
root = true

[*]
end_of_line = crlf
'@

$gitAttributesPlatformDefault = @'
* text=auto
'@

$gitAttributesLf = @'
* text=auto eol=lf
'@

$gitAttributesCrlf = @'
* text=auto eol=crlf
'@

# Each row is one configuration pair and the outcome the guard owes it. The
# drifted rows reproduce the shapes this repository has shipped or could ship.
$cases = @(
    [pscustomobject]@{
        Name = 'Passes_WhenBothConfigurationsUseLf'
        EditorConfig = $editorConfigLf
        GitAttributes = $gitAttributesLf
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        Name = 'Passes_WhenBothConfigurationsUseCrlf'
        EditorConfig = $editorConfigCrlf
        GitAttributes = $gitAttributesCrlf
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheEditorAsksForCrlfAndGitChecksOutLf'
        EditorConfig = $editorConfigCrlf
        GitAttributes = $gitAttributesLf
        ExpectPass = $false
        Expect = "git checks out lf.+requires crlf"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenGitAttributesLeavesTheEndingToThePlatform'
        EditorConfig = $editorConfigLf
        GitAttributes = $gitAttributesPlatformDefault
        ExpectPass = $false
        Expect = "git leaves the checkout ending to the platform.+requires lf"
    }
    [pscustomobject]@{
        # The same defect narrowed to one extension: a guard that only compared
        # each rule with the `[*]` section would read this as in sync, because the
        # broad rule's sample path carries no extension.
        Name = 'Fails_WhenOnlyOneExtensionAsksForCrlf'
        EditorConfig = "$editorConfigLf`n`n[*.cs]`nend_of_line = crlf`n"
        GitAttributes = $gitAttributesLf
        ExpectPass = $false
        Expect = "'sample\.cs': git checks out lf.+requires crlf"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheDefaultRuleIsMissing'
        EditorConfig = $editorConfigLf
        GitAttributes = "*.cs text eol=lf`n"
        ExpectPass = $false
        Expect = "has no '\*' rule"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheDefaultRuleMarksEveryPathBinary'
        EditorConfig = $editorConfigLf
        GitAttributes = "* -text`n"
        ExpectPass = $false
        Expect = "marks every path binary"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenASpecificRuleDisagreesWithTheEditor'
        EditorConfig = $editorConfigLf
        GitAttributes = "* text=auto eol=lf`n*.ps1 text eol=crlf`n"
        ExpectPass = $false
        Expect = "'sample\.ps1': git checks out crlf.+requires lf"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenAGitAttributesEndingHasNoTextAttribute'
        EditorConfig = $editorConfigLf
        GitAttributes = "* text=auto eol=lf`n*.unitypackage eol=lf`n"
        ExpectPass = $false
        Expect = "sets an ending for '\*\.unitypackage' without 'text'"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenABinaryRuleAlsoPinsAnEnding'
        EditorConfig = $editorConfigLf
        GitAttributes = "* text=auto eol=lf`n*.unitypackage -text eol=lf`n"
        ExpectPass = $false
        Expect = "marks '\*\.unitypackage' binary and also sets an ending"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheEndingNameIsUnknown'
        EditorConfig = $editorConfigLf
        GitAttributes = "* text=auto eol=cr`n"
        ExpectPass = $false
        Expect = "unknown line ending 'cr'"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenNoEditorConfigSectionDeclaresTheDefault'
        EditorConfig = "root = true`n`n[*.cs]`nindent_size = 4`n"
        GitAttributes = $gitAttributesLf
        ExpectPass = $false
        Expect = "must carry a '\[\*\]' section"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenTheDefaultSectionOmitsEndOfLine'
        EditorConfig = "root = true`n`n[*]`nindent_size = 4`n"
        GitAttributes = $gitAttributesLf
        ExpectPass = $false
        Expect = "sets no 'end_of_line' for '\[\*\]'"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenAnEditorConfigLineIsNotAProperty'
        EditorConfig = "root = true`n`n[*]`nend_of_line`n"
        GitAttributes = $gitAttributesLf
        ExpectPass = $false
        Expect = "is not a 'key = value' line"
    }
    [pscustomobject]@{
        Name = 'IgnoresBinaryRulesThatPinNoEnding'
        EditorConfig = $editorConfigLf
        GitAttributes = "* text=auto eol=lf`n*.png binary`n*.unitypackage -text`n"
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        # A later section wins, so a path-specific ending must be honoured on both
        # sides instead of compared against the default alone.
        Name = 'Passes_WhenALaterEditorConfigSectionNarrowsTheEnding'
        EditorConfig = "$editorConfigLf`n`n[*.ps1]`nend_of_line = crlf`n"
        GitAttributes = "* text=auto eol=lf`n*.ps1 text eol=crlf`n"
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        # The repository groups extensions in one brace list, so every alternative
        # has to be compared on both sides: a matcher that ignored the braces would
        # resolve those sections as unmatched and report a matching ending as a
        # mismatch, and one that compared only the first would leave `*.ps1` out.
        Name = 'Passes_WhenAnEditorConfigSectionGroupsExtensionsInBraces'
        EditorConfig = "$editorConfigLf`n`n[{*.cs,*.ps1}]`nindent_size = 4`nend_of_line = crlf`n"
        GitAttributes = "* text=auto eol=lf`n*.cs text eol=crlf`n*.ps1 text eol=crlf`n"
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        # Git takes each attribute from the last matching rule, so a binary rule
        # after the text rule must win even though the text rule pins an ending.
        Name = 'Passes_WhenALaterBinaryRuleOverridesTheTextRule'
        EditorConfig = $editorConfigLf
        GitAttributes = "* text=auto eol=lf`ndocs/images/*.png -text`n"
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        # `binary` is git's macro for `-text`, so a path it covers carries no
        # ending and a narrower editor section must not be reported against it.
        Name = 'Passes_WhenTheBinaryMacroCoversAPathTheEditorNarrows'
        EditorConfig = "$editorConfigLf`n`n[*.png]`nend_of_line = crlf`n"
        GitAttributes = "* text=auto eol=lf`n*.png binary`n"
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        # A `**` pattern is refused rather than read as two `*`: a matcher that
        # guessed would stop matching deeper paths and hide the drift behind them.
        Name = 'Fails_WhenAGitAttributesRuleUsesARecursiveGlob'
        EditorConfig = $editorConfigLf
        GitAttributes = "* text=auto eol=lf`nEditor/** text eol=crlf`n"
        ExpectPass = $false
        Expect = "recursive '\*\*' segment in 'Editor/\*\*'"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenAnEditorConfigSectionUsesARecursiveGlob'
        EditorConfig = "$editorConfigLf`n`n[Editor/**]`nend_of_line = crlf`n"
        GitAttributes = $gitAttributesLf
        ExpectPass = $false
        Expect = "recursive '\*\*' segment in 'Editor/\*\*'"
    }
    [pscustomobject]@{
        # Comparing only the first alternative left `*.ps1` unchecked, so a real
        # disagreement passed. Git pins `*.md` back to the default; `*.ps1` still
        # checks out LF while the editor asks for CRLF.
        Name = 'Fails_WhenALaterBraceAlternativeDriftsOnTheGitSide'
        EditorConfig = $editorConfigCrlf
        GitAttributes = "* text=auto eol=crlf`n{*.md,*.ps1} text eol=lf`n*.md text eol=crlf`n"
        ExpectPass = $false
        Expect = "'sample\.ps1': git checks out lf.+requires crlf"
    }
    [pscustomobject]@{
        # The mirror image: the narrowed section carries the brace group and git
        # leaves the second extension on the broad rule.
        Name = 'Fails_WhenALaterBraceAlternativeDriftsOnTheEditorSide'
        EditorConfig = "$editorConfigLf`n`n[{*.md,*.ps1}]`nend_of_line = crlf`n"
        GitAttributes = "* text=auto eol=lf`n*.md text eol=crlf`n"
        ExpectPass = $false
        Expect = "'sample\.ps1': git checks out lf.+requires crlf"
    }
    [pscustomobject]@{
        # Every alternative of a brace group compared on both sides and every one
        # in sync, so comparing the later ones must not start reporting the
        # alternatives that agree.
        Name = 'Passes_WhenEveryBraceAlternativeAgrees'
        EditorConfig = "$editorConfigLf`n`n[{*.md,*.ps1}]`nend_of_line = crlf`n"
        GitAttributes = "* text=auto eol=lf`n{*.md,*.ps1} text eol=crlf`n"
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        # `*.ps1` comes from the inner group, so a split that stops at the first
        # `}` or that ignores nesting depth never compares it and its drift goes
        # unreported.
        Name = 'Fails_WhenBraceGroupsNest'
        EditorConfig = "$editorConfigCrlf`n`n[*.{cs,{ps1,bat}}]`nend_of_line = lf`n"
        GitAttributes = "* text=auto eol=crlf`n*.ps1 text eol=crlf`n"
        ExpectPass = $false
        Expect = "'sample\.ps1': git checks out crlf.+requires lf"
    }
    [pscustomobject]@{
        Name = 'Passes_WhenNestedBraceGroupsAgree'
        EditorConfig = "$editorConfigLf`n`n[*.{cs,{ps1,bat}}]`nend_of_line = crlf`n"
        GitAttributes = "* text=auto eol=lf`n*.cs text eol=crlf`n*.ps1 text eol=crlf`n*.bat text eol=crlf`n"
        ExpectPass = $true
        Expect = ''
    }
    [pscustomobject]@{
        # The other constructs the matcher cannot resolve exactly. Each would
        # otherwise be read as literal text and silently stop matching.
        Name = 'Fails_WhenAGitAttributesRuleIsAnchored'
        EditorConfig = $editorConfigLf
        GitAttributes = "* text=auto eol=lf`n/docs/*.md text eol=crlf`n"
        ExpectPass = $false
        Expect = "anchored or directory-only '/' in '/docs/\*\.md'"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenAGitAttributesRuleIsDirectoryOnly'
        EditorConfig = $editorConfigLf
        GitAttributes = "* text=auto eol=lf`nbuild/ text eol=crlf`n"
        ExpectPass = $false
        Expect = "anchored or directory-only '/' in 'build/'"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenAGitAttributesRuleUsesACharacterClass'
        EditorConfig = $editorConfigLf
        GitAttributes = "* text=auto eol=lf`n*[0-9].cs text eol=crlf`n"
        ExpectPass = $false
        Expect = "character class in '\*\[0-9\]\.cs'"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenAGitAttributesRuleUsesABackslashEscape'
        EditorConfig = $editorConfigLf
        GitAttributes = "* text=auto eol=lf`ndocs/\*.md text eol=crlf`n"
        ExpectPass = $false
        Expect = "backslash escape in 'docs/\\\*\.md'"
    }
    [pscustomobject]@{
        Name = 'Fails_WhenAnEditorConfigSectionIsAnchored'
        EditorConfig = "$editorConfigLf`n`n[/docs/*.md]`nend_of_line = crlf`n"
        GitAttributes = $gitAttributesLf
        ExpectPass = $false
        Expect = "anchored or directory-only '/' in '/docs/\*\.md'"
    }
)

foreach ($script:case in $cases) {
    Invoke-TestCase $script:case.Name {
        $root = New-TempRoot -Prefix 'line-endings-'
        try {
            Write-FixtureFile -Root $root -RelativePath '.editorconfig' -Content $script:case.EditorConfig
            Write-FixtureFile -Root $root -RelativePath '.gitattributes' -Content $script:case.GitAttributes
            $result = Invoke-FixtureLint -Root $root
            if ($script:case.ExpectPass) {
                Assert-True ($result.ExitCode -eq 0) "expected a clean contract: $($result.Output)"
            } else {
                Assert-True ($result.ExitCode -eq 1) "expected drift to fail: $($result.Output)"
                Assert-True (
                    $result.Output -match $script:case.Expect
                ) "expected '$($script:case.Expect)' in: $($result.Output)"
            }
        } finally {
            Remove-TempRoot $root
        }
    }
}

Invoke-TestCase 'Fails_WhenAGitAttributesFileIsMissing' {
    $root = New-TempRoot -Prefix 'line-endings-'
    try {
        Write-FixtureFile -Root $root -RelativePath '.editorconfig' -Content $editorConfigLf
        $result = Invoke-FixtureLint -Root $root
        Assert-True ($result.ExitCode -eq 1) "a missing file must fail closed: $($result.Output)"
        Assert-True (
            $result.Output -match 'git attributes'
        ) "must explain the unreadable input: $($result.Output)"
    } finally {
        Remove-TempRoot $root
    }
}

Invoke-TestCase 'Passes_AgainstTheRepositoryItself' {
    $result = Invoke-LineEndingLint `
        -EditorConfig (Join-Path $repoRoot '.editorconfig') `
        -GitAttributes (Join-Path $repoRoot '.gitattributes') `
        -LintArguments @('--verbose')
    Assert-True ($result.ExitCode -eq 0) "the repository must satisfy its own contract: $($result.Output)"
    Assert-True (
        $result.Output -match 'Line-ending contract in sync: lf across \d+ path'
    ) "verbose output must report the resolved ending: $($result.Output)"
}

if ($script:TestFailureCount -gt 0) {
    Write-Host "line-ending self-tests: $($script:TestFailureCount) failed"
    exit 1
}
Write-Host 'line-ending self-tests: all passed'
exit 0