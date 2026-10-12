#!/usr/bin/env python3
"""Installs or verifies the pinned analyzer sets in a Unity host project.

Typed Python port of ``install-host-errorprone-analyzers.ps1`` (issue
#171): the same pinned sets, validation order, Unity metadata contract,
exit codes, and console messages, driven by the standard library.

Analyzer assets belong in the development host project, not in this
package. The installer verifies the pinned NuGet package and each
extracted DLL before writing them under ``Assets/Analyzers/<Set>``.
Generated Unity metadata disables the DLLs as ordinary plugins and
applies the case-sensitive RoslynAnalyzer label.

Select a set with ``--analyzer-set ErrorProne.NET`` (default),
``SonarAnalyzer``, or ``All``. Each set pins its NuGet package and every
analyzer DLL by SHA-256.

Usage:
    python scripts/install_host_errorprone_analyzers.py --host-project <path>

    python scripts/install_host_errorprone_analyzers.py --host-project <path> \
        --analyzer-set SonarAnalyzer

    python scripts/install_host_errorprone_analyzers.py --host-project <path> \
        --analyzer-set All --verify-only
"""

from __future__ import annotations

import argparse
import hashlib
import os
import re
import sys
import tempfile
import urllib.request
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Tuple

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent

ANALYZERS_PARENT_RELATIVE_DIRECTORY = "Assets/Analyzers"
ANALYZERS_PARENT_GUID = "6db6ad37e2934a59a09b9f97f924a13d"


@dataclass(frozen=True)
class AnalyzerPin:
    """One analyzer DLL and its pinned package entry and Unity GUID."""

    name: str
    entry: str
    sha256: str
    guid: str


@dataclass(frozen=True)
class AnalyzerSetDefinition:
    """One pinned analyzer set: its package pin and every DLL pin."""

    key: str
    display_name: str
    version: str
    package_url: str
    package_sha256: str
    relative_directory: str
    directory_guid: str
    analyzers: Tuple[AnalyzerPin, ...]


ANALYZER_SETS: Dict[str, AnalyzerSetDefinition] = {
    definition.key: definition
    for definition in (
        AnalyzerSetDefinition(
            key="ErrorProne.NET",
            display_name="ErrorProne.NET",
            version="0.4.0-beta.1",
            package_url="https://api.nuget.org/v3-flatcontainer/errorprone.net.coreanalyzers/0.4.0-beta.1/errorprone.net.coreanalyzers.0.4.0-beta.1.nupkg",
            package_sha256="faa033931cb21546dc7c9cb1705e20126b732d952a8db2fd8d43e6bdd5b8bfcc",
            relative_directory="Assets/Analyzers/ErrorProne.NET",
            directory_guid="8aff32430d524551aed718a13694594b",
            analyzers=(
                AnalyzerPin(
                    name="ErrorProne.NET.Core.dll",
                    entry="analyzers/dotnet/cs/ErrorProne.NET.Core.dll",
                    sha256="8530658374e63c3faf8c049369ee231e6fe589894a240cee2f38a711edb2d02c",
                    guid="b481962c998ae4104ba6510bca336000",
                ),
                AnalyzerPin(
                    name="ErrorProne.Net.CoreAnalyzers.dll",
                    entry="analyzers/dotnet/cs/ErrorProne.Net.CoreAnalyzers.dll",
                    sha256="7f769b69b5389049f18ef6e18c3992edfea347026f15ebdd60af901453e9bfca",
                    guid="456eb7187c4664d4bbdb92e5273e6c2c",
                ),
                AnalyzerPin(
                    name="RuntimeContracts.dll",
                    entry="analyzers/dotnet/cs/RuntimeContracts.dll",
                    sha256="2736206c839eef788c9703c20199a6e499b7ac0009cb7ca5dd28de52d1444543",
                    guid="f7a20012ab6014331af8c6ca66d63560",
                ),
            ),
        ),
        AnalyzerSetDefinition(
            key="SonarAnalyzer",
            display_name="SonarAnalyzer.CSharp",
            version="9.32.0.97167",
            package_url="https://api.nuget.org/v3-flatcontainer/sonaranalyzer.csharp/9.32.0.97167/sonaranalyzer.csharp.9.32.0.97167.nupkg",
            package_sha256="17c7fd6230597a4c08a30226e8b29f8e8c2a982ca12d4b9315021c8c41150cf8",
            relative_directory="Assets/Analyzers/SonarAnalyzer",
            directory_guid="130ea89660434645b6f37d8a778dbb2a",
            analyzers=(
                AnalyzerPin(
                    name="SonarAnalyzer.dll",
                    entry="analyzers/SonarAnalyzer.dll",
                    sha256="c8a3be5f2b28f221bc6ccc91d3175103ddcdab75d74a56ea69be19be20650142",
                    guid="8fe4aed7ac0940ffaed50fbf8f1952b9",
                ),
                AnalyzerPin(
                    name="SonarAnalyzer.CSharp.dll",
                    entry="analyzers/SonarAnalyzer.CSharp.dll",
                    sha256="443eb0c078a0f2d5b918b78b5129bc3239d29fb4a21264523bb46b9aa60a31f2",
                    guid="d0ed3c3a25db4b6fb3b58188a11064e0",
                ),
                AnalyzerPin(
                    name="SonarAnalyzer.CFG.dll",
                    entry="analyzers/SonarAnalyzer.CFG.dll",
                    sha256="d6a119af0f585666a3e330a660de57c1a657b3f43fa51d9c7cc63e2f17255a00",
                    guid="c0368dc68b02404dac205d9acc6a0aac",
                ),
                AnalyzerPin(
                    name="SonarAnalyzer.ShimLayer.dll",
                    entry="analyzers/SonarAnalyzer.ShimLayer.dll",
                    sha256="56bde7d4285d9e06ab88071b8683cada4fa4a28b06a7f79db047ab7de59765ae",
                    guid="291da6359ab94e258046132b47c64264",
                ),
                AnalyzerPin(
                    name="Google.Protobuf.dll",
                    entry="analyzers/Google.Protobuf.dll",
                    sha256="923be9abdb271ed7766fca4c520954166ef56cdf347219f175d1b70663155563",
                    guid="1532c635595c4603985ee8d8f020dae7",
                ),
            ),
        ),
    )
}


def sha256_bytes(payload: bytes) -> str:
    """Hash payload bytes into the lowercase hex digest the pins use."""
    return hashlib.sha256(payload).hexdigest()


def sha256_file(path: Path) -> str:
    """Hash a file's bytes into the lowercase hex digest the pins use."""
    return sha256_bytes(path.read_bytes())


def resolve_full_path(path: str, description: str, must_exist: bool = False) -> Path:
    """Resolve an absolute path, optionally requiring it to exist."""
    if not path or not path.strip():
        raise ValueError(f"{description} must not be empty.")
    resolved = Path(os.path.abspath(path))
    if must_exist and not resolved.exists():
        raise ValueError(f"{description} does not exist: {resolved}")
    return resolved


def is_path_within(candidate: Path, parent: Path) -> bool:
    """Return whether ``candidate`` is ``parent`` itself or inside it.

    Windows path comparisons ignore case because the file system does;
    every other platform compares ordinally.
    """

    def fold(value: str) -> str:
        return value.lower() if os.name == "nt" else value

    parent_with_separator = str(parent).rstrip("/\\") + os.sep
    return fold(str(candidate)) == fold(str(parent)) or fold(str(candidate)).startswith(fold(parent_with_separator))


def analyzer_metadata(guid: str) -> str:
    """Build the pinned Unity plugin metadata for one analyzer DLL."""
    return "\n".join(
        [
            "fileFormatVersion: 2",
            f"guid: {guid}",
            "labels:",
            "- RoslynAnalyzer",
            "PluginImporter:",
            "  externalObjects: {}",
            "  serializedVersion: 2",
            "  iconMap: {}",
            "  executionOrder: {}",
            "  defineConstraints: []",
            "  isPreloaded: 0",
            "  isOverridable: 1",
            "  isExplicitlyReferenced: 0",
            "  validateReferences: 1",
            "  platformData:",
            "  - first:",
            "      : Any",
            "    second:",
            "      enabled: 0",
            "      settings:",
            "        Exclude Editor: 1",
            "        Exclude Linux64: 1",
            "        Exclude OSXUniversal: 1",
            "        Exclude Win: 1",
            "        Exclude Win64: 1",
            "  - first:",
            "      Any:",
            "    second:",
            "      enabled: 0",
            "      settings: {}",
            "  - first:",
            "      Editor: Editor",
            "    second:",
            "      enabled: 0",
            "      settings:",
            "        DefaultValueInitialized: true",
            "  userData:",
            "  assetBundleName:",
            "  assetBundleVariant:",
        ]
    )


def folder_metadata(guid: str) -> str:
    """Build the pinned Unity folder metadata for one analyzer directory."""
    return "\n".join(
        [
            "fileFormatVersion: 2",
            f"guid: {guid}",
            "folderAsset: yes",
            "DefaultImporter:",
            "  externalObjects: {}",
            "  userData:",
            "  assetBundleName:",
            "  assetBundleVariant:",
        ]
    )


def write_utf8_text(path: Path, content: str) -> None:
    """Write normalized UTF-8 text without a BOM and with one trailing newline."""
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(content.replace("\r\n", "\n") + "\n")


def assert_exact_text(path: Path, expected: str, description: str) -> None:
    """Require a file to hold exactly ``expected`` text, newline-normalized."""
    if not path.is_file():
        raise ValueError(f"{description} is missing: {path}")
    actual = path.read_bytes().decode("utf-8").replace("\r\n", "\n")
    normalized_expected = expected.replace("\r\n", "\n") + "\n"
    if actual != normalized_expected:
        raise ValueError(f"{description} does not match the pinned Unity import settings: {path}")


def read_zip_entry_bytes(archive: zipfile.ZipFile, entry_path: str) -> bytes:
    """Read one exact zip entry, failing closed when it is missing."""
    try:
        with archive.open(entry_path) as source:
            return source.read()
    except KeyError:
        raise ValueError(f"NuGet package is missing required entry: {entry_path}") from None


def resolve_analyzer_package(set_definition: AnalyzerSetDefinition, package_path: str, downloaded: List[Path]) -> Path:
    """Return the analyzer package file, downloading the pinned URL when needed."""
    if not package_path or not package_path.strip():
        handle = tempfile.NamedTemporaryFile(prefix="analyzer-package-", suffix=".nupkg", delete=False)
        downloaded_path = Path(handle.name)
        handle.close()
        downloaded.append(downloaded_path)
        with urllib.request.urlopen(set_definition.package_url) as response:
            payload = response.read()
        downloaded_path.write_bytes(payload)
        return downloaded_path

    resolved_package = resolve_full_path(package_path, "NuGet package", must_exist=True)
    if not resolved_package.is_file():
        raise ValueError(f"NuGet package is not a file: {resolved_package}")
    return resolved_package


def verify_analyzer_set(set_definition: AnalyzerSetDefinition, project_root: Path) -> None:
    """Verify one installed analyzer set against its pins."""
    analyzer_directory = project_root / set_definition.relative_directory
    if is_path_within(analyzer_directory, REPOSITORY_ROOT):
        raise ValueError("Analyzer destination must be outside the DxVisualizer package repository.")
    assert_exact_text(
        Path(f"{analyzer_directory}.meta"),
        folder_metadata(set_definition.directory_guid),
        "Analyzer folder metadata",
    )
    for analyzer in set_definition.analyzers:
        destination = analyzer_directory / analyzer.name
        if not destination.is_file():
            raise ValueError(f"Analyzer DLL is missing: {destination}")
        if sha256_file(destination) != analyzer.sha256:
            raise ValueError(f"Analyzer DLL hash does not match the pin: {destination}")
        assert_exact_text(Path(f"{destination}.meta"), analyzer_metadata(analyzer.guid), "Analyzer metadata")
    print(
        f"[host-analyzers] OK: {set_definition.display_name} {set_definition.version} "
        f"is pinned and configured in {analyzer_directory}"
    )


def install_analyzer_set(
    set_definition: AnalyzerSetDefinition,
    project_root: Path,
    package_path: str,
    parent_analyzer_directory: Path,
    parent_metadata: str,
    downloaded: List[Path],
) -> None:
    """Install one analyzer set into the host project after verifying every pin."""
    analyzer_directory = project_root / set_definition.relative_directory
    if is_path_within(analyzer_directory, REPOSITORY_ROOT):
        raise ValueError("Analyzer destination must be outside the DxVisualizer package repository.")

    resolved_package = resolve_analyzer_package(set_definition, package_path, downloaded)
    if sha256_file(resolved_package) != set_definition.package_sha256:
        raise ValueError("NuGet package SHA-256 does not match the pinned package.")

    payloads: Dict[str, bytes] = {}
    with zipfile.ZipFile(resolved_package) as archive:
        for analyzer in set_definition.analyzers:
            payload = read_zip_entry_bytes(archive, analyzer.entry)
            if sha256_bytes(payload) != analyzer.sha256:
                raise ValueError(f"Analyzer SHA-256 does not match the pin: {analyzer.entry}")
            payloads[analyzer.name] = payload

    parent_metadata_path = Path(f"{parent_analyzer_directory}.meta")
    analyzer_directory.mkdir(parents=True, exist_ok=True)
    if not parent_metadata_path.is_file():
        write_utf8_text(parent_metadata_path, parent_metadata)
    write_utf8_text(Path(f"{analyzer_directory}.meta"), folder_metadata(set_definition.directory_guid))
    for analyzer in set_definition.analyzers:
        destination = analyzer_directory / analyzer.name
        destination.write_bytes(payloads[analyzer.name])
        write_utf8_text(Path(f"{destination}.meta"), analyzer_metadata(analyzer.guid))

    print(f"[host-analyzers] INSTALLED: {set_definition.display_name} {set_definition.version} in {analyzer_directory}")


def parse_arguments(argv: Optional[List[str]] = None) -> argparse.Namespace:
    """Parse the installer command line."""
    parser = argparse.ArgumentParser(
        prog="install-host-errorprone-analyzers",
        description="Install or verify the pinned analyzer sets in a Unity host project.",
    )
    parser.add_argument("--host-project", required=True, help="Unity host project root that owns Assets/Analyzers")
    parser.add_argument(
        "--package-path", default="", help="path to a pre-downloaded analyzer .nupkg (default: download the pin)"
    )
    parser.add_argument(
        "--analyzer-set",
        default="ErrorProne.NET",
        choices=[*ANALYZER_SETS.keys(), "All"],
        help="analyzer set to install or verify",
    )
    parser.add_argument("--verify-only", action="store_true", help="verify the installed set without network access")
    return parser.parse_args(argv)


def run(options: argparse.Namespace) -> int:
    """Run one install-or-verify pass and return the process exit code."""
    downloaded: List[Path] = []
    try:
        project_root = resolve_full_path(options.host_project, "Host project", must_exist=True)
        assets_path = project_root / "Assets"
        project_version_path = project_root / "ProjectSettings/ProjectVersion.txt"
        if not assets_path.is_dir():
            raise ValueError(f"Host project is missing its Assets directory: {assets_path}")
        if not project_version_path.is_file():
            raise ValueError("Host project is missing ProjectSettings/ProjectVersion.txt.")
        version_text = project_version_path.read_text(encoding="utf-8")
        if re.search(r"(?m)^m_EditorVersion:\s*\S+\s*$", version_text) is None:
            raise ValueError("Host project does not declare m_EditorVersion.")

        parent_analyzer_directory = project_root / ANALYZERS_PARENT_RELATIVE_DIRECTORY
        parent_metadata = folder_metadata(ANALYZERS_PARENT_GUID)

        selected_set_keys = list(ANALYZER_SETS.keys()) if options.analyzer_set == "All" else [options.analyzer_set]

        if options.verify_only:
            parent_metadata_path = Path(f"{parent_analyzer_directory}.meta")
            if not parent_metadata_path.is_file():
                raise ValueError(f"Parent analyzer folder metadata is missing: {parent_metadata_path}")
            for set_key in selected_set_keys:
                verify_analyzer_set(ANALYZER_SETS[set_key], project_root)
            return 0

        for set_key in selected_set_keys:
            install_analyzer_set(
                ANALYZER_SETS[set_key],
                project_root,
                options.package_path,
                parent_analyzer_directory,
                parent_metadata,
                downloaded,
            )
        print("[host-analyzers] Keep this development-only directory out of the package repository.")
        return 0
    except Exception as error:
        print(f"[host-analyzers] ERROR: {error}")
        return 1
    finally:
        for downloaded_package in downloaded:
            try:
                downloaded_package.unlink()
            except OSError:
                pass


def main(argv: Optional[List[str]] = None) -> int:
    """Entry point."""
    return run(parse_arguments(argv))


if __name__ == "__main__":
    sys.exit(main())
