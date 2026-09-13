Set-StrictMode -Version 2.0

$script:TestFailureCount = 0
. (Join-Path $PSScriptRoot 'TestHelpers.ps1')

$productionScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'install-host-errorprone-analyzers.ps1'
$analyzerNames = @(
    'ErrorProne.NET.Core.dll',
    'ErrorProne.Net.CoreAnalyzers.dll',
    'RuntimeContracts.dll'
)
$sonarAnalyzerNames = @(
    'SonarAnalyzer.dll',
    'SonarAnalyzer.CSharp.dll',
    'SonarAnalyzer.CFG.dll',
    'SonarAnalyzer.ShimLayer.dll',
    'Google.Protobuf.dll'
)
$productionErrorPronePins = [ordered]@{
    Package = 'faa033931cb21546dc7c9cb1705e20126b732d952a8db2fd8d43e6bdd5b8bfcc'
    'ErrorProne.NET.Core.dll' = '8530658374e63c3faf8c049369ee231e6fe589894a240cee2f38a711edb2d02c'
    'ErrorProne.Net.CoreAnalyzers.dll' = '7f769b69b5389049f18ef6e18c3992edfea347026f15ebdd60af901453e9bfca'
    'RuntimeContracts.dll' = '2736206c839eef788c9703c20199a6e499b7ac0009cb7ca5dd28de52d1444543'
}
$productionSonarPins = [ordered]@{
    Package = '17c7fd6230597a4c08a30226e8b29f8e8c2a982ca12d4b9315021c8c41150cf8'
    'SonarAnalyzer.dll' = 'c8a3be5f2b28f221bc6ccc91d3175103ddcdab75d74a56ea69be19be20650142'
    'SonarAnalyzer.CSharp.dll' = '443eb0c078a0f2d5b918b78b5129bc3239d29fb4a21264523bb46b9aa60a31f2'
    'SonarAnalyzer.CFG.dll' = 'd6a119af0f585666a3e330a660de57c1a657b3f43fa51d9c7cc63e2f17255a00'
    'SonarAnalyzer.ShimLayer.dll' = '56bde7d4285d9e06ab88071b8683cada4fa4a28b06a7f79db047ab7de59765ae'
    'Google.Protobuf.dll' = '923be9abdb271ed7766fca4c520954166ef56cdf347219f175d1b70663155563'
}

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
    param([string]$Root, [string[]]$Names, [string]$EntryPrefix)

    $staging = Join-Path $Root 'package-content'
    $analyzerDirectory = Join-Path $staging $EntryPrefix
    New-Item -ItemType Directory -Path $analyzerDirectory -Force | Out-Null
    $hashes = [ordered]@{}
    foreach ($name in $Names) {
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

function Get-PackagePinReplacements {
    param([hashtable]$Package, $ProductionPins)

    $replacements = [ordered]@{}
    $replacements[$ProductionPins.Package] = $Package.Sha256
    foreach ($name in $Package.AnalyzerHashes.Keys) {
        $replacements[$ProductionPins[$name]] = $Package.AnalyzerHashes[$name]
    }
    return $replacements
}

function New-PatchedInstallerFixture {
    param([string]$Root, [hashtable]$ErrorPronePackage, [hashtable]$SonarPackage)

    $scriptDirectory = Join-Path $Root 'scripts'
    New-Item -ItemType Directory -Path $scriptDirectory -Force | Out-Null
    $content = Get-Content -LiteralPath $productionScript -Raw
    $replacements = Get-PackagePinReplacements -Package $ErrorPronePackage -ProductionPins $productionErrorPronePins
    $sonarReplacements = Get-PackagePinReplacements -Package $SonarPackage -ProductionPins $productionSonarPins
    foreach ($pin in $sonarReplacements.Keys) {
        $replacements[$pin] = $sonarReplacements[$pin]
    }
    foreach ($pin in $replacements.Keys) {
        $content = $content.Replace($pin, $replacements[$pin])
    }
    $path = Join-Path $scriptDirectory 'install-host-errorprone-analyzers.ps1'
    [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
    return $path
}

function New-TestContext {
    $packageRoot = New-TempRoot
    $sonarPackageRoot = New-TempRoot
    $scriptRoot = New-TempRoot
    $hostRoot = New-HostProjectFixture
    $package = New-AnalyzerPackageFixture -Root $packageRoot -Names $analyzerNames -EntryPrefix 'analyzers/dotnet/cs'
    $sonarPackage = New-AnalyzerPackageFixture -Root $sonarPackageRoot -Names $sonarAnalyzerNames -EntryPrefix 'analyzers'
    $installer = New-PatchedInstallerFixture -Root $scriptRoot -ErrorPronePackage $package -SonarPackage $sonarPackage
    return [ordered]@{
        PackageRoot = $packageRoot
        SonarPackageRoot = $sonarPackageRoot
        ScriptRoot = $scriptRoot
        HostRoot = $hostRoot
        Package = $package
        SonarPackage = $sonarPackage
        Installer = $installer
    }
}

function Remove-TestContext {
    param([hashtable]$Context)

    Remove-TempRoot $Context.PackageRoot
    Remove-TempRoot $Context.SonarPackageRoot
    Remove-TempRoot $Context.ScriptRoot
    Remove-TempRoot $Context.HostRoot
}

Write-Host '== host analyzer installer self-tests =='

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
        Assert-True `
            (-not (Test-Path -LiteralPath (Join-Path $context.HostRoot 'Assets/Analyzers/SonarAnalyzer'))) `
            'the default analyzer set should not create SonarAnalyzer artifacts'

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
            Assert-True `
                ($metadata -match '(?ms)^  platformData:\s+  - first:\s+      : Any\s+    second:\s+      enabled: 0$') `
                "$name should use Unity's list-shaped disabled-plugin metadata"
            Assert-True `
                ($metadata -notmatch '(?m)^    Any:$') `
                "$name should not use dictionary-shaped platform metadata"
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

Invoke-TestCase 'SonarAnalyzer_Installs_Verifies_AndRepeatsIdempotently' {
    $context = New-TestContext
    try {
        $parentAnalyzerDirectory = Join-Path $context.HostRoot 'Assets/Analyzers'
        New-Item -ItemType Directory -Path $parentAnalyzerDirectory -Force | Out-Null
        $parentMetadataPath = "$parentAnalyzerDirectory.meta"
        [System.IO.File]::WriteAllText($parentMetadataPath, "existing-parent-metadata`n")

        & $context.Installer -HostProject $context.HostRoot -AnalyzerSet SonarAnalyzer -PackagePath $context.SonarPackage.Path *> $null
        Assert-ExitCode 0 'a valid pinned SonarAnalyzer package should install'
        Assert-True `
            ((Get-Content -LiteralPath $parentMetadataPath -Raw) -eq "existing-parent-metadata`n") `
            'installation should preserve an existing parent folder GUID and metadata'

        $destination = Join-Path $context.HostRoot 'Assets/Analyzers/SonarAnalyzer'
        $before = @{}
        foreach ($name in $sonarAnalyzerNames) {
            $path = Join-Path $destination $name
            Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "$name should be installed"
            Assert-True `
                ($context.SonarPackage.AnalyzerHashes[$name] -eq (Get-TestHash $path)) `
                "$name should retain its pinned hash"
            $metadata = Get-Content -LiteralPath "$path.meta" -Raw
            Assert-True ($metadata -match '(?m)^- RoslynAnalyzer$') "$name metadata should apply RoslynAnalyzer"
            Assert-True `
                ($metadata -match '(?ms)^  platformData:\s+  - first:\s+      : Any\s+    second:\s+      enabled: 0$') `
                "$name should use Unity's list-shaped disabled-plugin metadata"
            Assert-True `
                ($metadata -notmatch '(?m)^    Any:$') `
                "$name should not use dictionary-shaped platform metadata"
            $before[$name] = Get-TestHash $path
            $before["$name.meta"] = Get-TestHash "$path.meta"
        }

        & $context.Installer -HostProject $context.HostRoot -AnalyzerSet SonarAnalyzer -PackagePath $context.SonarPackage.Path *> $null
        Assert-ExitCode 0 'repeated installation should succeed'
        & $context.Installer -HostProject $context.HostRoot -AnalyzerSet SonarAnalyzer -VerifyOnly *> $null
        Assert-ExitCode 0 'the installed SonarAnalyzer set should verify without network access'
        foreach ($item in $before.Keys) {
            Assert-True `
                ($before[$item] -eq (Get-TestHash (Join-Path $destination $item))) `
                "$item should be byte-identical after reinstall"
        }
    } finally {
        Remove-TestContext $context
    }
}

Invoke-TestCase 'SonarAnalyzer_FailsVerification_WhenAnalyzerPayloadChanges' {
    $context = New-TestContext
    try {
        & $context.Installer -HostProject $context.HostRoot -AnalyzerSet SonarAnalyzer -PackagePath $context.SonarPackage.Path *> $null
        [System.IO.File]::WriteAllText(
            (Join-Path $context.HostRoot 'Assets/Analyzers/SonarAnalyzer/SonarAnalyzer.CSharp.dll'),
            'altered'
        )

        $output = & $context.Installer -HostProject $context.HostRoot -AnalyzerSet SonarAnalyzer -VerifyOnly *>&1 | Out-String
        Assert-ExitCode 1 'verification should reject an altered SonarAnalyzer'
        Assert-True ($output -match 'hash does not match') "output should identify the hash failure, got: $output"
    } finally {
        Remove-TestContext $context
    }
}

Invoke-TestCase 'All_AnalyzerSet_Verifies_BothInstalledSets' {
    $context = New-TestContext
    try {
        & $context.Installer -HostProject $context.HostRoot -PackagePath $context.Package.Path *> $null
        Assert-ExitCode 0 'the ErrorProne.NET set should install'
        & $context.Installer -HostProject $context.HostRoot -AnalyzerSet SonarAnalyzer -PackagePath $context.SonarPackage.Path *> $null
        Assert-ExitCode 0 'the SonarAnalyzer set should install'

        $output = & $context.Installer -HostProject $context.HostRoot -AnalyzerSet All -VerifyOnly *>&1 | Out-String
        Assert-ExitCode 0 'All mode should verify both installed sets without network access'
        Assert-True ($output -match 'ErrorProne.NET 0\.4\.0-beta\.1 is pinned') "output should verify ErrorProne.NET, got: $output"
        Assert-True ($output -match 'SonarAnalyzer.CSharp 9.32.0.97167 is pinned') "output should verify SonarAnalyzer, got: $output"
    } finally {
        Remove-TestContext $context
    }
}

Write-Host "== host analyzer installer: $script:TestFailureCount failure(s) =="
if ($script:TestFailureCount -gt 0) {
    exit 1
}
exit 0
