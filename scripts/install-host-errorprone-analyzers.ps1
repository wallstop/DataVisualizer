<#
.SYNOPSIS
Installs or verifies the pinned ErrorProne.NET analyzers in a Unity host project.

.DESCRIPTION
Analyzer assets belong in the development host project, not in this package. The
installer verifies the pinned NuGet package and each extracted DLL before writing
them under Assets/Analyzers/ErrorProne.NET. Generated Unity metadata disables the
DLLs as ordinary plugins and applies the case-sensitive RoslynAnalyzer label.

.EXAMPLE
pwsh -NoProfile -File scripts/install-host-errorprone-analyzers.ps1 `
    -HostProject /path/to/unity-project

.EXAMPLE
pwsh -NoProfile -File scripts/install-host-errorprone-analyzers.ps1 `
    -HostProject /path/to/unity-project `
    -VerifyOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$HostProject,

    [string]$PackagePath,

    [switch]$VerifyOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$packageVersion = '0.4.0-beta.1'
$packageUrl = (
    'https://api.nuget.org/v3-flatcontainer/errorprone.net.coreanalyzers/' +
    "$packageVersion/errorprone.net.coreanalyzers.$packageVersion.nupkg"
)
$packageSha256 = 'faa033931cb21546dc7c9cb1705e20126b732d952a8db2fd8d43e6bdd5b8bfcc'
$analyzerRelativeDirectory = 'Assets/Analyzers/ErrorProne.NET'
$analyzers = @(
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
  serializedVersion: 3
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 1
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 0
      settings:
        DefaultValueInitialized: true
    WindowsStoreApps:
      enabled: 0
      settings:
        CPU: AnyCPU
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

$downloadedPackage = $null
try {
    $repositoryRoot = Resolve-FullPath `
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

    $analyzerDirectory = Join-Path $projectRoot $analyzerRelativeDirectory
    if (Test-PathWithin -Candidate $analyzerDirectory -Parent $repositoryRoot) {
        throw 'Analyzer destination must be outside the Data Visualizer package repository.'
    }

    $parentAnalyzerDirectory = Join-Path $assetsPath 'Analyzers'
    $parentMetadataPath = "$parentAnalyzerDirectory.meta"
    $parentMetadata = Get-FolderMetadata '6db6ad37e2934a59a09b9f97f924a13d'
    $analyzerDirectoryMetadataPath = "$analyzerDirectory.meta"
    $analyzerDirectoryMetadata = Get-FolderMetadata '8aff32430d524551aed718a13694594b'

    if ($VerifyOnly) {
        if (-not (Test-Path -LiteralPath $parentMetadataPath -PathType Leaf)) {
            throw "Parent analyzer folder metadata is missing: $parentMetadataPath"
        }
        Assert-ExactText `
            $analyzerDirectoryMetadataPath `
            $analyzerDirectoryMetadata `
            'Analyzer folder metadata'
        foreach ($analyzer in $analyzers) {
            $destination = Join-Path $analyzerDirectory $analyzer.Name
            if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
                throw "Analyzer DLL is missing: $destination"
            }
            if ((Get-FileSha256 $destination) -ne $analyzer.Sha256) {
                throw "Analyzer DLL hash does not match the pin: $destination"
            }
            Assert-ExactText "$destination.meta" (Get-AnalyzerMetadata $analyzer.Guid) 'Analyzer metadata'
        }
        Write-Host "[host-analyzers] OK: ErrorProne.NET $packageVersion is pinned and configured in $analyzerDirectory"
        exit 0
    }

    if ([string]::IsNullOrWhiteSpace($PackagePath)) {
        $downloadedPackage = [System.IO.Path]::GetTempFileName()
        $client = [System.Net.Http.HttpClient]::new()
        try {
            $bytes = $client.GetByteArrayAsync($packageUrl).GetAwaiter().GetResult()
            [System.IO.File]::WriteAllBytes($downloadedPackage, $bytes)
        } finally {
            $client.Dispose()
        }
        $resolvedPackage = $downloadedPackage
    } else {
        $resolvedPackage = Resolve-FullPath -Path $PackagePath -Description 'NuGet package' -MustExist
        if (-not (Test-Path -LiteralPath $resolvedPackage -PathType Leaf)) {
            throw "NuGet package is not a file: $resolvedPackage"
        }
    }
    if ((Get-FileSha256 $resolvedPackage) -ne $packageSha256) {
        throw 'NuGet package SHA-256 does not match the pinned package.'
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackage)
    try {
        $payloads = [ordered]@{}
        foreach ($analyzer in $analyzers) {
            $payload = Read-ZipEntryBytes -Archive $archive -EntryPath $analyzer.Entry
            if ((Get-Sha256 $payload) -ne $analyzer.Sha256) {
                throw "Analyzer SHA-256 does not match the pin: $($analyzer.Entry)"
            }
            $payloads[$analyzer.Name] = $payload
        }
    } finally {
        $archive.Dispose()
    }

    New-Item -ItemType Directory -Path $analyzerDirectory -Force | Out-Null
    if (-not (Test-Path -LiteralPath $parentMetadataPath -PathType Leaf)) {
        Write-Utf8Text $parentMetadataPath $parentMetadata
    }
    Write-Utf8Text $analyzerDirectoryMetadataPath $analyzerDirectoryMetadata
    foreach ($analyzer in $analyzers) {
        $destination = Join-Path $analyzerDirectory $analyzer.Name
        [System.IO.File]::WriteAllBytes($destination, $payloads[$analyzer.Name])
        Write-Utf8Text "$destination.meta" (Get-AnalyzerMetadata $analyzer.Guid)
    }

    Write-Host "[host-analyzers] INSTALLED: ErrorProne.NET $packageVersion in $analyzerDirectory"
    Write-Host '[host-analyzers] Keep this development-only directory out of the package repository.'
    exit 0
} catch {
    Write-Host "[host-analyzers] ERROR: $($_.Exception.Message)"
    exit 1
} finally {
    if ($null -ne $downloadedPackage -and (Test-Path -LiteralPath $downloadedPackage)) {
        Remove-Item -LiteralPath $downloadedPackage -Force
    }
}
