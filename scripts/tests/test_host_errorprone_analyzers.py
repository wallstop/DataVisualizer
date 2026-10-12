#!/usr/bin/env python3
"""Host analyzer installer self-tests (typed Python port of ``test-host-errorprone-analyzers.ps1``, issue #171).

Every case from the PowerShell original is kept with the same name and
assertions; only the driver language changed. The installer is driven as
a child process: a Python child costs a few milliseconds to start, and
the real CLI contract is exercised instead of an in-process call. The
zip fixtures use the standard library writer, which produces the same
archive shape the PowerShell original's ``Compress-Archive`` produced.
"""

from __future__ import annotations

import hashlib
import re
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Optional, Sequence, Tuple

sys.dont_write_bytecode = True

from _test_helpers import (
    assert_exit_code,
    assert_true,
    failure_count,
    find_python,
    new_temp_root,
    remove_temp_root,
    run_case,
    run_tool_output,
    write_fixture_file,
)

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
INSTALLER = SCRIPTS_DIR / "install_host_errorprone_analyzers.py"

ANALYZER_NAMES = (
    "ErrorProne.NET.Core.dll",
    "ErrorProne.Net.CoreAnalyzers.dll",
    "RuntimeContracts.dll",
)
SONAR_ANALYZER_NAMES = (
    "SonarAnalyzer.dll",
    "SonarAnalyzer.CSharp.dll",
    "SonarAnalyzer.CFG.dll",
    "SonarAnalyzer.ShimLayer.dll",
    "Google.Protobuf.dll",
)
PRODUCTION_ERROR_PRONE_PINS: Tuple[Tuple[str, str], ...] = (
    ("Package", "faa033931cb21546dc7c9cb1705e20126b732d952a8db2fd8d43e6bdd5b8bfcc"),
    ("ErrorProne.NET.Core.dll", "8530658374e63c3faf8c049369ee231e6fe589894a240cee2f38a711edb2d02c"),
    ("ErrorProne.Net.CoreAnalyzers.dll", "7f769b69b5389049f18ef6e18c3992edfea347026f15ebdd60af901453e9bfca"),
    ("RuntimeContracts.dll", "2736206c839eef788c9703c20199a6e499b7ac0009cb7ca5dd28de52d1444543"),
)
PRODUCTION_SONAR_PINS: Tuple[Tuple[str, str], ...] = (
    ("Package", "17c7fd6230597a4c08a30226e8b29f8e8c2a982ca12d4b9315021c8c41150cf8"),
    ("SonarAnalyzer.dll", "c8a3be5f2b28f221bc6ccc91d3175103ddcdab75d74a56ea69be19be20650142"),
    ("SonarAnalyzer.CSharp.dll", "443eb0c078a0f2d5b918b78b5129bc3239d29fb4a21264523bb46b9aa60a31f2"),
    ("SonarAnalyzer.CFG.dll", "d6a119af0f585666a3e330a660de57c1a657b3f43fa51d9c7cc63e2f17255a00"),
    ("SonarAnalyzer.ShimLayer.dll", "56bde7d4285d9e06ab88071b8683cada4fa4a28b06a7f79db047ab7de59765ae"),
    ("Google.Protobuf.dll", "923be9abdb271ed7766fca4c520954166ef56cdf347219f175d1b70663155563"),
)


def sha256(path: Path) -> str:
    """Hash a fixture file into the lowercase hex digest the pins use."""
    return hashlib.sha256(path.read_bytes()).hexdigest()


@dataclass(frozen=True)
class PackageFixture:
    """One fixture analyzer package: its archive, package hash, and DLL hashes."""

    root: Path
    path: Path
    sha256: str
    analyzer_hashes: Dict[str, str]


@dataclass(frozen=True)
class TestContext:
    """Every fixture tree one case needs, cleaned up together."""

    package_root: Path
    sonar_package_root: Path
    script_root: Path
    host_root: Path
    package: PackageFixture
    sonar_package: PackageFixture
    installer: Path


def new_host_project_fixture() -> Path:
    """Build a minimal Unity host project the installer accepts."""
    root = new_temp_root()
    write_fixture_file(root, "Assets/.keep", "")
    write_fixture_file(root, "ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.4.6f1")
    return root


def new_analyzer_package_fixture(root: Path, names: Sequence[str], entry_prefix: str) -> PackageFixture:
    """Pack the named analyzer DLLs into a fixture NuGet archive."""
    staging = root / "package-content"
    analyzer_directory = staging / entry_prefix
    analyzer_directory.mkdir(parents=True)
    analyzer_hashes: Dict[str, str] = {}
    for name in names:
        path = analyzer_directory / name
        path.write_bytes(f"fixture:{name}".encode("utf-8"))
        analyzer_hashes[name] = sha256(path)
    package_path = root / "fixture.nupkg"
    with zipfile.ZipFile(package_path, "w") as archive:
        for analyzer_path in sorted(staging.rglob("*")):
            if analyzer_path.is_file():
                archive.write(analyzer_path, str(analyzer_path.relative_to(staging)).replace("\\", "/"))
    return PackageFixture(root=root, path=package_path, sha256=sha256(package_path), analyzer_hashes=analyzer_hashes)


def package_pin_replacements(package: PackageFixture, production_pins: Sequence[Tuple[str, str]]) -> Dict[str, str]:
    """Map every production pin onto its fixture hash for one package."""
    replacements: Dict[str, str] = {}
    for name, pin in production_pins:
        replacements[pin] = package.sha256 if name == "Package" else package.analyzer_hashes[name]
    return replacements


def new_patched_installer_fixture(
    root: Path, error_prone_package: PackageFixture, sonar_package: PackageFixture
) -> Path:
    """Patch the production installer's pins onto the fixture hashes."""
    script_directory = root / "scripts"
    script_directory.mkdir(parents=True)
    content = INSTALLER.read_text(encoding="utf-8")
    replacements = package_pin_replacements(error_prone_package, PRODUCTION_ERROR_PRONE_PINS)
    for key, value in package_pin_replacements(sonar_package, PRODUCTION_SONAR_PINS).items():
        replacements[key] = value
    for pin, replacement in replacements.items():
        content = content.replace(pin, replacement)
    path = script_directory / "install_host_errorprone_analyzers.py"
    write_fixture_file(root, "scripts/install_host_errorprone_analyzers.py", content)
    return path


def new_test_context() -> TestContext:
    """Build one fixture context: two packages, a patched installer, a host."""
    package_root = new_temp_root()
    sonar_package_root = new_temp_root()
    script_root = new_temp_root()
    host_root = new_host_project_fixture()
    package = new_analyzer_package_fixture(package_root, ANALYZER_NAMES, "analyzers/dotnet/cs")
    sonar_package = new_analyzer_package_fixture(sonar_package_root, SONAR_ANALYZER_NAMES, "analyzers")
    installer = new_patched_installer_fixture(script_root, package, sonar_package)
    return TestContext(
        package_root=package_root,
        sonar_package_root=sonar_package_root,
        script_root=script_root,
        host_root=host_root,
        package=package,
        sonar_package=sonar_package,
        installer=installer,
    )


def remove_test_context(context: TestContext) -> None:
    """Delete every tree one case created."""
    remove_temp_root(context.package_root)
    remove_temp_root(context.sonar_package_root)
    remove_temp_root(context.script_root)
    remove_temp_root(context.host_root)


def invoke_installer(
    python: str,
    installer: Path,
    host_root: Path,
    package_path: Optional[Path] = None,
    analyzer_set: Optional[str] = None,
    verify_only: bool = False,
) -> Tuple[str, int]:
    """Run the installer as a child with output merged, like the original's ``*>&1``."""
    arguments = [python, str(installer), "--host-project", str(host_root)]
    if package_path is not None:
        arguments.extend(["--package-path", str(package_path)])
    if analyzer_set is not None:
        arguments.extend(["--analyzer-set", analyzer_set])
    if verify_only:
        arguments.append("--verify-only")
    return run_tool_output(arguments)


def assert_analyzers_installed(
    destination: Path,
    context: TestContext,
    names: Sequence[str],
    package: PackageFixture,
) -> Dict[str, str]:
    """Assert one installed set matches every pin, returning the recorded hashes."""
    before: Dict[str, str] = {}
    for name in names:
        path = destination / name
        assert_true(path.is_file(), f"{name} should be installed")
        assert_true(
            package.analyzer_hashes[name] == sha256(path),
            f"{name} should retain its pinned hash",
        )
        metadata = Path(f"{path}.meta").read_text(encoding="utf-8")
        assert_true(re.search(r"(?m)^- RoslynAnalyzer$", metadata) is not None, f"{name} metadata should apply RoslynAnalyzer")
        assert_true(
            re.search(r"(?ms)^  platformData:\s+  - first:\s+      : Any\s+    second:\s+      enabled: 0$", metadata)
            is not None,
            f"{name} should use Unity's list-shaped disabled-plugin metadata",
        )
        assert_true(
            re.search(r"(?m)^    Any:$", metadata) is None,
            f"{name} should not use dictionary-shaped platform metadata",
        )
        before[name] = sha256(path)
        before[f"{name}.meta"] = sha256(Path(f"{path}.meta"))
    return before


def case_installs_verifies_and_repeats_idempotently(python: str) -> None:
    context = new_test_context()
    try:
        parent_analyzer_directory = context.host_root / "Assets/Analyzers"
        parent_analyzer_directory.mkdir(parents=True)
        parent_metadata_path = Path(f"{parent_analyzer_directory}.meta")
        parent_metadata_path.write_text("existing-parent-metadata\n", encoding="utf-8")

        _, exit_code = invoke_installer(python, context.installer, context.host_root, package_path=context.package.path)
        assert_exit_code(0, "a valid pinned package should install", exit_code)
        assert_true(
            parent_metadata_path.read_text(encoding="utf-8") == "existing-parent-metadata\n",
            "installation should preserve an existing parent folder GUID and metadata",
        )
        assert_true(
            not (context.host_root / "Assets/Analyzers/SonarAnalyzer").exists(),
            "the default analyzer set should not create SonarAnalyzer artifacts",
        )

        destination = context.host_root / "Assets/Analyzers/ErrorProne.NET"
        before = assert_analyzers_installed(destination, context, ANALYZER_NAMES, context.package)

        _, exit_code = invoke_installer(python, context.installer, context.host_root, package_path=context.package.path)
        assert_exit_code(0, "repeated installation should succeed", exit_code)
        _, exit_code = invoke_installer(python, context.installer, context.host_root, verify_only=True)
        assert_exit_code(0, "the installed analyzer set should verify without network access", exit_code)
        for item, recorded in before.items():
            assert_true(
                recorded == sha256(destination / item),
                f"{item} should be byte-identical after reinstall",
            )
    finally:
        remove_test_context(context)


def case_fails_verification_when_analyzer_payload_changes(python: str) -> None:
    context = new_test_context()
    try:
        _, exit_code = invoke_installer(python, context.installer, context.host_root, package_path=context.package.path)
        assert_exit_code(0, "the pinned package should install", exit_code)
        altered = context.host_root / "Assets/Analyzers/ErrorProne.NET/RuntimeContracts.dll"
        altered.write_text("altered", encoding="utf-8")

        output, exit_code = invoke_installer(python, context.installer, context.host_root, verify_only=True)
        assert_exit_code(1, "verification should reject an altered analyzer", exit_code)
        assert_true("hash does not match" in output, f"output should identify the hash failure, got: {output}")
    finally:
        remove_test_context(context)


def case_fails_verification_when_analyzer_metadata_changes(python: str) -> None:
    context = new_test_context()
    try:
        _, exit_code = invoke_installer(python, context.installer, context.host_root, package_path=context.package.path)
        assert_exit_code(0, "the pinned package should install", exit_code)
        metadata_path = context.host_root / "Assets/Analyzers/ErrorProne.NET/ErrorProne.NET.Core.dll.meta"
        metadata = metadata_path.read_text(encoding="utf-8").replace("- RoslynAnalyzer", "- WrongLabel")
        metadata_path.write_text(metadata, encoding="utf-8")

        output, exit_code = invoke_installer(python, context.installer, context.host_root, verify_only=True)
        assert_exit_code(1, "verification should reject changed Unity metadata", exit_code)
        assert_true(
            "does not match the pinned Unity import settings" in output,
            f"output should identify metadata drift, got: {output}",
        )
    finally:
        remove_test_context(context)


def case_fails_when_host_project_is_invalid(python: str) -> None:
    context = new_test_context()
    invalid_host = new_temp_root()
    try:
        output, exit_code = invoke_installer(
            python, context.installer, invalid_host, package_path=context.package.path
        )
        assert_exit_code(1, "a directory without Unity project markers should fail", exit_code)
        assert_true("missing its Assets directory" in output, f"output should identify the invalid project, got: {output}")
    finally:
        remove_temp_root(invalid_host)
        remove_test_context(context)


def case_fails_when_destination_is_inside_package_repository(python: str) -> None:
    context = new_test_context()
    nested_host = context.script_root / "HostProject"
    try:
        write_fixture_file(nested_host, "Assets/.keep", "")
        write_fixture_file(nested_host, "ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.4.6f1")

        output, exit_code = invoke_installer(
            python, context.installer, nested_host, package_path=context.package.path
        )
        assert_exit_code(1, "a package-repository destination should fail", exit_code)
        assert_true("must be outside" in output, f"output should identify the ownership boundary, got: {output}")
    finally:
        remove_test_context(context)


def case_fails_when_nuget_package_hash_changes(python: str) -> None:
    context = new_test_context()
    try:
        with open(context.package.path, "a", encoding="utf-8") as handle:
            handle.write("altered")

        output, exit_code = invoke_installer(
            python, context.installer, context.host_root, package_path=context.package.path
        )
        assert_exit_code(1, "an altered NuGet package should fail", exit_code)
        assert_true("NuGet package SHA-256" in output, f"output should identify package drift, got: {output}")
    finally:
        remove_test_context(context)


def case_sonar_analyzer_installs_verifies_and_repeats_idempotently(python: str) -> None:
    context = new_test_context()
    try:
        parent_analyzer_directory = context.host_root / "Assets/Analyzers"
        parent_analyzer_directory.mkdir(parents=True)
        parent_metadata_path = Path(f"{parent_analyzer_directory}.meta")
        parent_metadata_path.write_text("existing-parent-metadata\n", encoding="utf-8")

        _, exit_code = invoke_installer(
            python,
            context.installer,
            context.host_root,
            package_path=context.sonar_package.path,
            analyzer_set="SonarAnalyzer",
        )
        assert_exit_code(0, "a valid pinned SonarAnalyzer package should install", exit_code)
        assert_true(
            parent_metadata_path.read_text(encoding="utf-8") == "existing-parent-metadata\n",
            "installation should preserve an existing parent folder GUID and metadata",
        )

        destination = context.host_root / "Assets/Analyzers/SonarAnalyzer"
        before = assert_analyzers_installed(destination, context, SONAR_ANALYZER_NAMES, context.sonar_package)

        _, exit_code = invoke_installer(
            python,
            context.installer,
            context.host_root,
            package_path=context.sonar_package.path,
            analyzer_set="SonarAnalyzer",
        )
        assert_exit_code(0, "repeated installation should succeed", exit_code)
        _, exit_code = invoke_installer(
            python, context.installer, context.host_root, analyzer_set="SonarAnalyzer", verify_only=True
        )
        assert_exit_code(0, "the installed SonarAnalyzer set should verify without network access", exit_code)
        for item, recorded in before.items():
            assert_true(
                recorded == sha256(destination / item),
                f"{item} should be byte-identical after reinstall",
            )
    finally:
        remove_test_context(context)


def case_sonar_analyzer_fails_verification_when_analyzer_payload_changes(python: str) -> None:
    context = new_test_context()
    try:
        _, exit_code = invoke_installer(
            python,
            context.installer,
            context.host_root,
            package_path=context.sonar_package.path,
            analyzer_set="SonarAnalyzer",
        )
        assert_exit_code(0, "the pinned SonarAnalyzer package should install", exit_code)
        altered = context.host_root / "Assets/Analyzers/SonarAnalyzer/SonarAnalyzer.CSharp.dll"
        altered.write_text("altered", encoding="utf-8")

        output, exit_code = invoke_installer(
            python, context.installer, context.host_root, analyzer_set="SonarAnalyzer", verify_only=True
        )
        assert_exit_code(1, "verification should reject an altered SonarAnalyzer", exit_code)
        assert_true("hash does not match" in output, f"output should identify the hash failure, got: {output}")
    finally:
        remove_test_context(context)


def case_all_analyzer_set_verifies_both_installed_sets(python: str) -> None:
    context = new_test_context()
    try:
        _, exit_code = invoke_installer(python, context.installer, context.host_root, package_path=context.package.path)
        assert_exit_code(0, "the ErrorProne.NET set should install", exit_code)
        _, exit_code = invoke_installer(
            python,
            context.installer,
            context.host_root,
            package_path=context.sonar_package.path,
            analyzer_set="SonarAnalyzer",
        )
        assert_exit_code(0, "the SonarAnalyzer set should install", exit_code)

        output, exit_code = invoke_installer(
            python, context.installer, context.host_root, analyzer_set="All", verify_only=True
        )
        assert_exit_code(0, "All mode should verify both installed sets without network access", exit_code)
        assert_true(
            re.search(r"ErrorProne.NET 0\.4\.0-beta\.1 is pinned", output) is not None,
            f"output should verify ErrorProne.NET, got: {output}",
        )
        assert_true(
            re.search(r"SonarAnalyzer.CSharp 9\.32\.0\.97167 is pinned", output) is not None,
            f"output should verify SonarAnalyzer, got: {output}",
        )
    finally:
        remove_test_context(context)


def main() -> int:
    print("== host analyzer installer self-tests ==")

    python = find_python()

    run_case("Installs_Verifies_AndRepeatsIdempotently", lambda: case_installs_verifies_and_repeats_idempotently(python))
    run_case("FailsVerification_WhenAnalyzerPayloadChanges", lambda: case_fails_verification_when_analyzer_payload_changes(python))
    run_case(
        "FailsVerification_WhenAnalyzerMetadataChanges",
        lambda: case_fails_verification_when_analyzer_metadata_changes(python),
    )
    run_case("Fails_WhenHostProjectIsInvalid", lambda: case_fails_when_host_project_is_invalid(python))
    run_case(
        "Fails_WhenDestinationIsInsidePackageRepository",
        lambda: case_fails_when_destination_is_inside_package_repository(python),
    )
    run_case("Fails_WhenNuGetPackageHashChanges", lambda: case_fails_when_nuget_package_hash_changes(python))
    run_case(
        "SonarAnalyzer_Installs_Verifies_AndRepeatsIdempotently",
        lambda: case_sonar_analyzer_installs_verifies_and_repeats_idempotently(python),
    )
    run_case(
        "SonarAnalyzer_FailsVerification_WhenAnalyzerPayloadChanges",
        lambda: case_sonar_analyzer_fails_verification_when_analyzer_payload_changes(python),
    )
    run_case("All_AnalyzerSet_Verifies_BothInstalledSets", lambda: case_all_analyzer_set_verifies_both_installed_sets(python))

    failures = failure_count()
    print(f"== host analyzer installer: {failures} failure(s) ==")
    return 1 if failures > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
