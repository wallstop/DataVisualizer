<#
.SYNOPSIS
Installs or verifies the pinned analyzer sets (ErrorProne.NET, SonarAnalyzer) in
a Unity host project.

.DESCRIPTION
Analyzer assets belong in the development host project, not in this package. The
installer verifies the pinned NuGet package and each extracted DLL before writing
them under Assets/Analyzers/<Set>. Generated Unity metadata disables the DLLs as
ordinary plugins and applies the case-sensitive RoslynAnalyzer label.

Select a set with -AnalyzerSet ErrorProne.NET (default), SonarAnalyzer, or All.
Each set pins its NuGet package and every analyzer DLL by SHA-256.

.EXAMPLE
pwsh -NoProfile -File scripts/install-host-errorprone-analyzers.ps1 `
    -HostProject /path/to/unity-project

.EXAMPLE
pwsh -NoProfile -File scripts/install-host-errorprone-analyzers.ps1 `
    -HostProject /path/to/unity-project `
    -AnalyzerSet SonarAnalyzer

.EXAMPLE
pwsh -NoProfile -File scripts/install-host-errorprone-analyzers.ps1 `
    -HostProject /path/to/unity-project `
    -AnalyzerSet All `
    -VerifyOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$HostProject,

    [string]$PackagePath,

    [ValidateSet('ErrorProne.NET', 'SonarAnalyzer', 'All')]
    [string]$AnalyzerSet = 'ErrorProne.NET',

    [switch]$VerifyOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$analyzersParentRelativeDirectory = 'Assets/Analyzers'
$analyzersParentGuid = '6db6ad37e2934a59a09b9f97f924a13d'

$analyzerSets = [ordered]@{
    'ErrorProne.NET' = ([ordered]@{
        DisplayName = 'ErrorProne.NET'
        Version = '0.4.0-beta.1'
        PackageUrl = 'https://api.nuget.org/v3-flatcontainer/errorprone.net.coreanalyzers/0.4.0-beta.1/errorprone.net.coreanalyzers.0.4.0-beta.1.nupkg'
        PackageSha256 = 'faa033931cb21546dc7c9cb1705e20126b732d952a8db2fd8d43e6bdd5b8bfcc'
        RelativeDirectory = 'Assets/Analyzers/ErrorProne.NET'
        DirectoryGuid = '8aff32430d524551aed718a13694594b'
        Analyzers = @(
            [ordered]@{
                Name = 'ErrorProne.NET.Core.dll'
                Entry = 'analyzers/dotnet/cs/ErrorProne.NET.Core.dll'
                Sha256 = '8530658374e63c3faf8c049369ee231e6fe589894a240cee2f38a711edb2d02c'
                Guid = 'b481962c998ae4104ba6510bca336000'
            },
            [ordered]@{
                Name = 'ErrorProne.Net.CoreAnalyzers.dll'
                Entry = 'analyzers/dotnet/cs/ErrorProne.Net.CoreAnalyzers.dll'
                Sha256 = '7f769b69b5389049f18ef6e18c3992edfea347026f15ebdd60af901453e9bfca'
                Guid = '456eb7187c4664d4bbdb92e5273e6c2c'
            },
            [ordered]@{
                Name = 'RuntimeContracts.dll'
                Entry = 'analyzers/dotnet/cs/RuntimeContracts.dll'
                Sha256 = '2736206c839eef788c9703c20199a6e499b7ac0009cb7ca5dd28de52d1444543'
                Guid = 'f7a20012ab6014331af8c6ca66d63560'
            }
        )
    })
    'SonarAnalyzer' = ([ordered]@{
        DisplayName = 'SonarAnalyzer.CSharp'
        Version = '9.32.0.97167'
        PackageUrl = 'https://api.nuget.org/v3-flatcontainer/sonaranalyzer.csharp/9.32.0.97167/sonaranalyzer.csharp.9.32.0.97167.nupkg'
        PackageSha256 = '17c7fd6230597a4c08a30226e8b29f8e8c2a982ca12d4b9315021c8c41150cf8'
        RelativeDirectory = 'Assets/Analyzers/SonarAnalyzer'
        DirectoryGuid = '130ea89660434645b6f37d8a778dbb2a'
        Analyzers = @(
            [ordered]@{
                Name = 'SonarAnalyzer.dll'
                Entry = 'analyzers/SonarAnalyzer.dll'
                Sha256 = 'c8a3be5f2b28f221bc6ccc91d3175103ddcdab75d74a56ea69be19be20650142'
                Guid = '8fe4aed7ac0940ffaed50fbf8f1952b9'
            },
            [ordered]@{
                Name = 'SonarAnalyzer.CSharp.dll'
                Entry = 'analyzers/SonarAnalyzer.CSharp.dll'
                Sha256 = '443eb0c078a0f2d5b918b78b5129bc3239d29fb4a21264523bb46b9aa60a31f2'
                Guid = 'd0ed3c3a25db4b6fb3b58188a11064e0'
            },
            [ordered]@{
                Name = 'SonarAnalyzer.CFG.dll'
                Entry = 'analyzers/SonarAnalyzer.CFG.dll'
                Sha256 = 'd6a119af0f585666a3e330a660de57c1a657b3f43fa51d9c7cc63e2f17255a00'
                Guid = 'c0368dc68b02404dac205d9acc6a0aac'
            },
            [ordered]@{
                Name = 'SonarAnalyzer.ShimLayer.dll'
                Entry = 'analyzers/SonarAnalyzer.ShimLayer.dll'
                Sha256 = '56bde7d4285d9e06ab88071b8683cada4fa4a28b06a7f79db047ab7de59765ae'
                Guid = '291da6359ab94e258046132b47c64264'
            },
            [ordered]@{
                Name = 'Google.Protobuf.dll'
                Entry = 'analyzers/Google.Protobuf.dll'
                Sha256 = '923be9abdb271ed7766fca4c520954166ef56cdf347219f175d1b70663155563'
                Guid = '1532c635595c4603985ee8d8f020dae7'
            }
        )
    })
}

function Get-Sha256 {
    param([byte[]]$Bytes)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    } finally {
        $sha256.Dispose()
    }
}

function Get-FileSha256 {
    param([string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Resolve-FullPath {
    param([string]$Path, [string]$Description, [switch]$MustExist)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Description must not be empty."
    }
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if ($MustExist -and -not (Test-Path -LiteralPath $resolved)) {
        throw "$Description does not exist: $resolved"
    }
    return $resolved
}

function Test-PathWithin {
    param([string]$Candidate, [string]$Parent)

    $comparison = if ($IsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    } else {
        [System.StringComparison]::Ordinal
    }
    $separator = [System.IO.Path]::DirectorySeparatorChar
    $parentWithSeparator = $Parent.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + $separator
    return $Candidate.Equals($Parent, $comparison) -or $Candidate.StartsWith(
        $parentWithSeparator,
        $comparison
    )
}

function Get-AnalyzerMetadata {
    param([string]$Guid)

    return @"
fileFormatVersion: 2
guid: $Guid
labels:
- RoslynAnalyzer
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 1
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      : Any
    second:
      enabled: 0
      settings:
        Exclude Editor: 1
        Exclude Linux64: 1
        Exclude OSXUniversal: 1
        Exclude Win: 1
        Exclude Win64: 1
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  userData:
  assetBundleName:
  assetBundleVariant:
"@
}

function Get-FolderMetadata {
    param([string]$Guid)

    return @"
fileFormatVersion: 2
guid: $Guid
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@
}

function Write-Utf8Text {
    param([string]$Path, [string]$Content)

    [System.IO.File]::WriteAllText(
        $Path,
        $Content.Replace("`r`n", "`n") + "`n",
        [System.Text.UTF8Encoding]::new($false)
    )
}

function Assert-ExactText {
    param([string]$Path, [string]$Expected, [string]$Description)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
    $actual = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    $normalizedExpected = $Expected.Replace("`r`n", "`n") + "`n"
    if (-not $actual.Equals($normalizedExpected, [System.StringComparison]::Ordinal)) {
        throw "$Description does not match the pinned Unity import settings: $Path"
    }
}

function Read-ZipEntryBytes {
    param([System.IO.Compression.ZipArchive]$Archive, [string]$EntryPath)

    $entry = $Archive.GetEntry($EntryPath)
    if ($null -eq $entry) {
        throw "NuGet package is missing required entry: $EntryPath"
    }
    $source = $entry.Open()
    $buffer = [System.IO.MemoryStream]::new()
    try {
        $source.CopyTo($buffer)
        return $buffer.ToArray()
    } finally {
        $buffer.Dispose()
        $source.Dispose()
    }
}

function Test-AnalyzerSet {
    param([hashtable]$Set, [string]$ProjectRoot)

    $analyzerDirectory = Join-Path $ProjectRoot $Set.RelativeDirectory
    if (Test-PathWithin -Candidate $analyzerDirectory -Parent $script:repositoryRoot) {
        throw 'Analyzer destination must be outside the Data Visualizer package repository.'
    }
    Assert-ExactText `
        "$analyzerDirectory.meta" `
        (Get-FolderMetadata $Set.DirectoryGuid) `
        'Analyzer folder metadata'
    foreach ($analyzer in $Set.Analyzers) {
        $destination = Join-Path $analyzerDirectory $analyzer.Name
        if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
            throw "Analyzer DLL is missing: $destination"
        }
        if ((Get-FileSha256 $destination) -ne $analyzer.Sha256) {
            throw "Analyzer DLL hash does not match the pin: $destination"
        }
        Assert-ExactText "$destination.meta" (Get-AnalyzerMetadata $analyzer.Guid) 'Analyzer metadata'
    }
    Write-Host "[host-analyzers] OK: $($Set.DisplayName) $($Set.Version) is pinned and configured in $analyzerDirectory"
}

function Install-AnalyzerSet {
    param([hashtable]$Set, [string]$ProjectRoot)

    $analyzerDirectory = Join-Path $ProjectRoot $Set.RelativeDirectory
    if (Test-PathWithin -Candidate $analyzerDirectory -Parent $script:repositoryRoot) {
        throw 'Analyzer destination must be outside the Data Visualizer package repository.'
    }

    $resolvedPackage = Resolve-AnalyzerPackage -Set $Set
    if ((Get-FileSha256 $resolvedPackage) -ne $Set.PackageSha256) {
        throw 'NuGet package SHA-256 does not match the pinned package.'
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackage)
    try {
        $payloads = [ordered]@{}
        foreach ($analyzer in $Set.Analyzers) {
            $payload = Read-ZipEntryBytes -Archive $archive -EntryPath $analyzer.Entry
            if ((Get-Sha256 $payload) -ne $analyzer.Sha256) {
                throw "Analyzer SHA-256 does not match the pin: $($analyzer.Entry)"
            }
            $payloads[$analyzer.Name] = $payload
        }
    } finally {
        $archive.Dispose()
    }

    $parentMetadataPath = "$script:parentAnalyzerDirectory.meta"
    New-Item -ItemType Directory -Path $analyzerDirectory -Force | Out-Null
    if (-not (Test-Path -LiteralPath $parentMetadataPath -PathType Leaf)) {
        Write-Utf8Text $parentMetadataPath $script:parentMetadata
    }
    Write-Utf8Text "$analyzerDirectory.meta" (Get-FolderMetadata $Set.DirectoryGuid)
    foreach ($analyzer in $Set.Analyzers) {
        $destination = Join-Path $analyzerDirectory $analyzer.Name
        [System.IO.File]::WriteAllBytes($destination, $payloads[$analyzer.Name])
        Write-Utf8Text "$destination.meta" (Get-AnalyzerMetadata $analyzer.Guid)
    }

    Write-Host "[host-analyzers] INSTALLED: $($Set.DisplayName) $($Set.Version) in $analyzerDirectory"
}

function Resolve-AnalyzerPackage {
    param([hashtable]$Set)

    if ([string]::IsNullOrWhiteSpace($PackagePath)) {
        $downloaded = [System.IO.Path]::GetTempFileName()
        $script:downloadedPackages += $downloaded
        $client = [System.Net.Http.HttpClient]::new()
        try {
            $bytes = $client.GetByteArrayAsync($Set.PackageUrl).GetAwaiter().GetResult()
            [System.IO.File]::WriteAllBytes($downloaded, $bytes)
        } finally {
            $client.Dispose()
        }
        return $downloaded
    }

    $resolvedPackage = Resolve-FullPath -Path $PackagePath -Description 'NuGet package' -MustExist
    if (-not (Test-Path -LiteralPath $resolvedPackage -PathType Leaf)) {
        throw "NuGet package is not a file: $resolvedPackage"
    }
    return $resolvedPackage
}

$script:downloadedPackages = @()
$script:repositoryRoot = $null
$script:parentAnalyzerDirectory = $null
$script:parentMetadata = $null
try {
    $script:repositoryRoot = Resolve-FullPath `
        -Path (Split-Path -Parent $PSScriptRoot) `
        -Description 'Package repository' `
        -MustExist
    $projectRoot = Resolve-FullPath -Path $HostProject -Description 'Host project' -MustExist
    $assetsPath = Join-Path $projectRoot 'Assets'
    $projectVersionPath = Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $assetsPath -PathType Container)) {
        throw "Host project is missing its Assets directory: $assetsPath"
    }
    if (-not (Test-Path -LiteralPath $projectVersionPath -PathType Leaf)) {
        throw "Host project is missing ProjectSettings/ProjectVersion.txt."
    }
    $versionText = Get-Content -LiteralPath $projectVersionPath -Raw
    if ($versionText -notmatch '(?m)^m_EditorVersion:\s*\S+\s*$') {
        throw 'Host project does not declare m_EditorVersion.'
    }

    $script:parentAnalyzerDirectory = Join-Path $projectRoot $analyzersParentRelativeDirectory
    $script:parentMetadata = Get-FolderMetadata $analyzersParentGuid

    if ($AnalyzerSet -eq 'All') {
        $selectedSetKeys = @($analyzerSets.Keys)
    } else {
        $selectedSetKeys = @($AnalyzerSet)
    }

    if ($VerifyOnly) {
        if (-not (Test-Path -LiteralPath "$script:parentAnalyzerDirectory.meta" -PathType Leaf)) {
            throw "Parent analyzer folder metadata is missing: $script:parentAnalyzerDirectory.meta"
        }
        foreach ($setKey in $selectedSetKeys) {
            Test-AnalyzerSet -Set $analyzerSets[$setKey] -ProjectRoot $projectRoot
        }
        exit 0
    }

    foreach ($setKey in $selectedSetKeys) {
        Install-AnalyzerSet -Set $analyzerSets[$setKey] -ProjectRoot $projectRoot
    }
    Write-Host '[host-analyzers] Keep this development-only directory out of the package repository.'
    exit 0
} catch {
    Write-Host "[host-analyzers] ERROR: $($_.Exception.Message)"
    exit 1
} finally {
    foreach ($downloadedPackage in $script:downloadedPackages) {
        if ($null -ne $downloadedPackage -and (Test-Path -LiteralPath $downloadedPackage)) {
            Remove-Item -LiteralPath $downloadedPackage -Force
        }
    }
}