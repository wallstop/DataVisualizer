Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$productionScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'install-host-errorprone-analyzers.ps1'
$analyzerNames = @(
    'ErrorProne.NET.Core.dll',
    'ErrorProne.Net.CoreAnalyzers.dll',
    'RuntimeContracts.dll'
)

function Get-TestHash {
    param([string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-HostProjectFixture {
    $root = New-TempRoot
    Write-FixtureFile -Root $root -RelativePath 'Assets/.keep' -Content ''
    Write-FixtureFile `
        -Root $root `
        -RelativePath 'ProjectSettings/ProjectVersion.txt' `
        -Content 'm_EditorVersion: 6000.4.6f1'
    return $root
}

function New-AnalyzerPackageFixture {
    param([string]$Root)

    $staging = Join-Path $Root 'package-content'
    $analyzerDirectory = Join-Path $staging 'analyzers/dotnet/cs'
    New-Item -ItemType Directory -Path $analyzerDirectory -Force | Out-Null
    $hashes = [ordered]@{}
    foreach ($name in $analyzerNames) {
        $path = Join-Path $analyzerDirectory $name
        [System.IO.File]::WriteAllBytes($path, [System.Text.Encoding]::UTF8.GetBytes("fixture:$name"))
        $hashes[$name] = Get-TestHash $path
    }
    $packagePath = Join-Path $Root 'fixture.nupkg'
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $packagePath
    return [ordered]@{
        Path = $packagePath
        Sha256 = Get-TestHash $packagePath
        AnalyzerHashes = $hashes
    }
}

function New-PatchedInstallerFixture {
    param([string]$Root, [hashtable]$Package)

    $scriptDirectory = Join-Path $Root 'scripts'
    New-Item -ItemType Directory -Path $scriptDirectory -Force | Out-Null
    $content = Get-Content -LiteralPath $productionScript -Raw
    $content = $content.Replace(
        'faa033931cb21546dc7c9cb1705e20126b732d952a8db2fd8d43e6bdd5b8bfcc',
        $Package.Sha256
    )
    $productionHashes = @(
        '8530658374e63c3faf8c049369ee231e6fe589894a240cee2f38a711edb2d02c',
        '7f769b69b5389049f18ef6e18c3992edfea347026f15ebdd60af901453e9bfca',
        '2736206c839eef788c9703c20199a6e499b7ac0009cb7ca5dd28de52d1444543'
    )
    for ($index = 0; $index -lt $analyzerNames.Count; $index++) {
        $content = $content.Replace(
            $productionHashes[$index],
            $Package.AnalyzerHashes[$analyzerNames[$index]]
        )
    }
    $path = Join-Path $scriptDirectory 'install-host-errorprone-analyzers.ps1'
    [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
    return $path
}

function New-TestContext {
    $packageRoot = New-TempRoot
    $scriptRoot = New-TempRoot
    $hostRoot = New-HostProjectFixture
    $package = New-AnalyzerPackageFixture -Root $packageRoot
    $installer = New-PatchedInstallerFixture -Root $scriptRoot -Package $package
    return [ordered]@{
        PackageRoot = $packageRoot
        ScriptRoot = $scriptRoot
        HostRoot = $hostRoot
        Package = $package
        Installer = $installer
    }
}

function Remove-TestContext {
    param([hashtable]$Context)

    Remove-TempRoot $Context.PackageRoot
    Remove-TempRoot $Context.ScriptRoot
    Remove-TempRoot $Context.HostRoot
}

Write-Host '== host ErrorProne.NET analyzer installer self-tests =='

Invoke-TestCase 'Installs_Verifies_AndRepeatsIdempotently' {
    $context = New-TestContext
    try {
        $parentAnalyzerDirectory = Join-Path $context.HostRoot 'Assets/Analyzers'
        New-Item -ItemType Directory -Path $parentAnalyzerDirectory -Force | Out-Null
        $parentMetadataPath = "$parentAnalyzerDirectory.meta"
        [System.IO.File]::WriteAllText($parentMetadataPath, "existing-parent-metadata`n")

        & $context.Installer -HostProject $context.HostRoot -PackagePath $context.Package.Path *> $null
        Assert-ExitCode 0 'a valid pinned package should install'
        Assert-True `
            ((Get-Content -LiteralPath $parentMetadataPath -Raw) -eq "existing-parent-metadata`n") `
            'installation should preserve an existing parent folder GUID and metadata'

        $destination = Join-Path $context.HostRoot 'Assets/Analyzers/ErrorProne.NET'
        $before = @{}
        foreach ($name in $analyzerNames) {
            $path = Join-Path $destination $name
            Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "$name should be installed"
            Assert-True `
                ($context.Package.AnalyzerHashes[$name] -eq (Get-TestHash $path)) `
                "$name should retain its pinned hash"
            $metadata = Get-Content -LiteralPath "$path.meta" -Raw
            Assert-True ($metadata -match '(?m)^- RoslynAnalyzer$') "$name metadata should apply RoslynAnalyzer"
            Assert-True ($metadata -match '(?ms)^    Any:\s+enabled: 0$') "$name should be disabled as a normal plugin"
            $before[$name] = Get-TestHash $path
            $before["$name.meta"] = Get-TestHash "$path.meta"
        }

        & $context.Installer -HostProject $context.HostRoot -PackagePath $context.Package.Path *> $null
        Assert-ExitCode 0 'repeated installation should succeed'
        & $context.Installer -HostProject $context.HostRoot -VerifyOnly *> $null
        Assert-ExitCode 0 'the installed analyzer set should verify without network access'
        foreach ($item in $before.Keys) {
            Assert-True `
                ($before[$item] -eq (Get-TestHash (Join-Path $destination $item))) `
                "$item should be byte-identical after reinstall"
        }
    } finally {
        Remove-TestContext $context
    }
}

Invoke-TestCase 'FailsVerification_WhenAnalyzerPayloadChanges' {
    $context = New-TestContext
    try {
        & $context.Installer -HostProject $context.HostRoot -PackagePath $context.Package.Path *> $null
        [System.IO.File]::WriteAllText(
            (Join-Path $context.HostRoot 'Assets/Analyzers/ErrorProne.NET/RuntimeContracts.dll'),
            'altered'
        )

        $output = & $context.Installer -HostProject $context.HostRoot -VerifyOnly *>&1 | Out-String
        Assert-ExitCode 1 'verification should reject an altered analyzer'
        Assert-True ($output -match 'hash does not match') "output should identify the hash failure, got: $output"
    } finally {
        Remove-TestContext $context
    }
}

Invoke-TestCase 'FailsVerification_WhenAnalyzerMetadataChanges' {
    $context = New-TestContext
    try {
        & $context.Installer -HostProject $context.HostRoot -PackagePath $context.Package.Path *> $null
        $metadataPath = Join-Path $context.HostRoot 'Assets/Analyzers/ErrorProne.NET/ErrorProne.NET.Core.dll.meta'
        $metadata = (Get-Content -LiteralPath $metadataPath -Raw).Replace('- RoslynAnalyzer', '- WrongLabel')
        [System.IO.File]::WriteAllText($metadataPath, $metadata)

        $output = & $context.Installer -HostProject $context.HostRoot -VerifyOnly *>&1 | Out-String
        Assert-ExitCode 1 'verification should reject changed Unity metadata'
        Assert-True ($output -match 'does not match the pinned Unity import settings') "output should identify metadata drift, got: $output"
    } finally {
        Remove-TestContext $context
    }
}

Invoke-TestCase 'Fails_WhenHostProjectIsInvalid' {
    $context = New-TestContext
    $invalidHost = New-TempRoot
    try {
        $output = & $context.Installer -HostProject $invalidHost -PackagePath $context.Package.Path *>&1 | Out-String
        Assert-ExitCode 1 'a directory without Unity project markers should fail'
        Assert-True ($output -match 'missing its Assets directory') "output should identify the invalid project, got: $output"
    } finally {
        Remove-TempRoot $invalidHost
        Remove-TestContext $context
    }
}

Invoke-TestCase 'Fails_WhenDestinationIsInsidePackageRepository' {
    $context = New-TestContext
    $nestedHost = Join-Path $context.ScriptRoot 'HostProject'
    try {
        Write-FixtureFile -Root $nestedHost -RelativePath 'Assets/.keep' -Content ''
        Write-FixtureFile `
            -Root $nestedHost `
            -RelativePath 'ProjectSettings/ProjectVersion.txt' `
            -Content 'm_EditorVersion: 6000.4.6f1'

        $output = & $context.Installer -HostProject $nestedHost -PackagePath $context.Package.Path *>&1 | Out-String
        Assert-ExitCode 1 'a package-repository destination should fail'
        Assert-True ($output -match 'must be outside') "output should identify the ownership boundary, got: $output"
    } finally {
        Remove-TestContext $context
    }
}

Invoke-TestCase 'Fails_WhenNuGetPackageHashChanges' {
    $context = New-TestContext
    try {
        Add-Content -LiteralPath $context.Package.Path -Value 'altered'

        $output = & $context.Installer -HostProject $context.HostRoot -PackagePath $context.Package.Path *>&1 | Out-String
        Assert-ExitCode 1 'an altered NuGet package should fail'
        Assert-True ($output -match 'NuGet package SHA-256') "output should identify package drift, got: $output"
    } finally {
        Remove-TestContext $context
    }
}

Write-Host "== host ErrorProne.NET analyzer installer: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
