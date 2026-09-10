#!/usr/bin/env python3
"""Run disposable, local Data Visualizer benchmark scenarios.

The driver deliberately keeps fixtures, generated Unity bootstrap code, raw
samples, and reports outside the package payload.  It supports either a local
Unity executable (``--mode direct``) or the existing official Unity MCP bridge
(``--mode mcp``).  The Unity-side clock is used for measured operations; Python
only orchestrates the editor and writes the final report.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable


FIXTURE_SIZES = (100, 1_000, 10_000, 50_000)
PLAY_ENTRY_CASES = ("open-idle", "open-indexing", "closed")
PLAY_ENTRY_WARMUPS = 5
PLAY_ENTRY_SAMPLES = 30
BOOTSTRAP_PATH = "Assets/Editor/__DataVisualizerBenchmarkEntryPoint.cs"
ENTRYPOINT = "WallstopStudios.DataVisualizer.Benchmark.DataVisualizerBenchmarkEntryPoint"
SCHEMA_VERSION = "1"


class BenchmarkError(RuntimeError):
    """Raised for invalid benchmark configuration or an unsuccessful run."""


def case_order_for_repetition(repetition: int) -> list[str]:
    """Return the deterministic alternating case order for a repetition."""

    if repetition < 0:
        raise BenchmarkError("repetition must be non-negative")
    cases = list(PLAY_ENTRY_CASES)
    return cases if repetition % 2 == 0 else list(reversed(cases))


def case_schedule(repetitions: int = PLAY_ENTRY_WARMUPS + PLAY_ENTRY_SAMPLES) -> list[dict[str, Any]]:
    if repetitions <= 0:
        raise BenchmarkError("repetitions must be positive")
    return [
        {"repetition": repetition, "cases": case_order_for_repetition(repetition)}
        for repetition in range(repetitions)
    ]


@dataclass(frozen=True)
class PathMapping:
    """Translate a local/container path to the path visible to the host editor."""

    local_prefix: Path
    host_prefix: str

    def translate(self, value: Path) -> str:
        local = value.expanduser().resolve()
        try:
            relative = local.relative_to(self.local_prefix)
        except ValueError as error:
            raise BenchmarkError(
                f"Path {local} is outside mapping prefix {self.local_prefix}"
            ) from error
        host_prefix = self.host_prefix.rstrip("/\\")
        if not host_prefix:
            return relative.as_posix()
        return f"{host_prefix}/{relative.as_posix()}"


@dataclass(frozen=True)
class BenchmarkConfig:
    host_project: str
    unity_version: str
    fixture_size: int
    suite: str
    output_dir: Path
    mode: str
    unity_path: str
    mcp_url: str
    mcp_token: str | None
    timeout_seconds: float
    keep_fixture: bool
    path_mapping: PathMapping | None
    revision: str
    package_version: str


def parse_path_mapping(value: str) -> PathMapping:
    local, separator, host = value.partition("=")
    if not separator or not local.strip() or not host.strip():
        raise argparse.ArgumentTypeError(
            "path mapping must use LOCAL_PREFIX=HOST_PREFIX"
        )
    return PathMapping(Path(local).expanduser().resolve(), host.strip())


def parse_fixture_size(value: str) -> int:
    try:
        parsed = int(value.replace(",", ""))
    except ValueError as error:
        raise argparse.ArgumentTypeError("fixture size must be an integer") from error
    if parsed not in FIXTURE_SIZES:
        choices = ", ".join(f"{size:,}" for size in FIXTURE_SIZES)
        raise argparse.ArgumentTypeError(f"fixture size must be one of: {choices}")
    return parsed


def resolve_token(explicit: str | None) -> str | None:
    if explicit:
        return explicit
    if os.environ.get("UNITY_MCP_TOKEN"):
        return os.environ["UNITY_MCP_TOKEN"]
    env_path = Path(".env.local")
    try:
        for line in env_path.read_text(encoding="utf-8").splitlines():
            if line.startswith("UNITY_MCP_TOKEN="):
                return line.split("=", 1)[1].strip().strip("\"'") or None
    except OSError:
        pass
    return None


def package_revision(root: Path) -> str:
    try:
        result = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=root,
            check=True,
            capture_output=True,
            text=True,
        )
    except (OSError, subprocess.CalledProcessError):
        return "unknown"
    return result.stdout.strip() or "unknown"


def package_version(root: Path) -> str:
    try:
        package = json.loads((root / "package.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise BenchmarkError(f"Cannot read package.json: {error}") from error
    version = package.get("version")
    if not isinstance(version, str) or not version:
        raise BenchmarkError("package.json must contain a non-empty version")
    return version


def csharp_string(value: str) -> str:
    """Render a normal C# string literal, including Windows paths safely."""

    return json.dumps(value, ensure_ascii=False)


def replace_bootstrap_tokens(template: str, values: dict[str, str]) -> str:
    rendered = template
    for token, value in values.items():
        rendered = rendered.replace(f"__{token}__", value)
    missing = [f"__{token}__" for token in values if f"__{token}__" in rendered]
    if missing:
        raise BenchmarkError(
            "Generated Unity bootstrap still contains token(s): " + ", ".join(missing)
        )
    return rendered


BOOTSTRAP_TEMPLATE = r'''// Generated by scripts/benchmark/benchmark_driver.py; do not commit.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;

namespace WallstopStudios.DataVisualizer.Benchmark
{
    [Serializable]
    public sealed class BenchmarkNestedData
    {
        public string key;
        public List<int> values = new();
        public BenchmarkSharedData shared;
    }

    public sealed class BenchmarkSharedData : ScriptableObject
    {
        public string identity;
    }

    public interface IBenchmarkFixture
    {
        void Configure(int index, BenchmarkSharedData shared);
        string Identity { get; }
        string Description { get; }
        int CustomOrder { get; }
        BenchmarkNestedData Nested { get; }
        BenchmarkSharedData SharedReference { get; }
    }

    public sealed class PlainData : ScriptableObject, IBenchmarkFixture
    {
        public string identity;
        public string description;
        public int customOrder;
        public BenchmarkNestedData nested = new();

        public string Identity => identity;
        public string Description => description;
        public int CustomOrder => customOrder;
        public BenchmarkNestedData Nested => nested;
        public BenchmarkSharedData SharedReference => nested?.shared;

        public void Configure(int index, BenchmarkSharedData shared)
        {
            identity = $"plain-{index:D6}";
            description = $"fixture-description-{index:D6}";
            customOrder = (index * 37 + 11) % 100000;
            nested = MakeNested(identity, shared, index);
        }

        private static BenchmarkNestedData MakeNested(string value, BenchmarkSharedData shared, int index)
        {
            return new BenchmarkNestedData
            {
                key = value,
                shared = shared,
                values = Enumerable.Range(0, 8).Select(item => item + index).ToList(),
            };
        }
    }

    public sealed class BaseData : WallstopStudios.DataVisualizer.BaseDataObject, IBenchmarkFixture
    {
        public string identity;
        public int customOrder;
        public BenchmarkNestedData nested = new();

        public string Identity => identity;
        public int CustomOrder => customOrder;
        public BenchmarkNestedData Nested => nested;
        public BenchmarkSharedData SharedReference => nested?.shared;

        public void Configure(int index, BenchmarkSharedData shared)
        {
            identity = $"base-{index:D6}";
            Description = $"fixture-description-{index:D6}";
            customOrder = (index * 37 + 11) % 100000;
            nested = new BenchmarkNestedData
            {
                key = identity,
                shared = shared,
                values = Enumerable.Range(0, 8).Select(item => item * 2 + index).ToList(),
            };
        }
    }
}

namespace WallstopStudios.DataVisualizer.Benchmark.First
{
    public sealed class Data : UnityEngine.ScriptableObject, WallstopStudios.DataVisualizer.Benchmark.IBenchmarkFixture
    {
        public string identity;
        public string description;
        public int customOrder;
        public WallstopStudios.DataVisualizer.Benchmark.BenchmarkNestedData nested = new();
        public string Identity => identity;
        public string Description => description;
        public int CustomOrder => customOrder;
        public WallstopStudios.DataVisualizer.Benchmark.BenchmarkNestedData Nested => nested;
        public WallstopStudios.DataVisualizer.Benchmark.BenchmarkSharedData SharedReference => nested?.shared;
        public void Configure(int index, WallstopStudios.DataVisualizer.Benchmark.BenchmarkSharedData shared)
        {
            identity = $"first-{index:D6}";
            description = $"fixture-description-{index:D6}";
            customOrder = (index * 37 + 11) % 100000;
            nested = new WallstopStudios.DataVisualizer.Benchmark.BenchmarkNestedData
            {
                key = identity,
                shared = shared,
                values = Enumerable.Range(0, 4).Select(item => item + index).ToList(),
            };
        }
    }
}

namespace WallstopStudios.DataVisualizer.Benchmark.Second
{
    public sealed class Data : WallstopStudios.DataVisualizer.BaseDataObject, WallstopStudios.DataVisualizer.Benchmark.IBenchmarkFixture
    {
        public string identity;
        public int customOrder;
        public WallstopStudios.DataVisualizer.Benchmark.BenchmarkNestedData nested = new();
        public string Identity => identity;
        public int CustomOrder => customOrder;
        public WallstopStudios.DataVisualizer.Benchmark.BenchmarkNestedData Nested => nested;
        public WallstopStudios.DataVisualizer.Benchmark.BenchmarkSharedData SharedReference => nested?.shared;
        public void Configure(int index, WallstopStudios.DataVisualizer.Benchmark.BenchmarkSharedData shared)
        {
            identity = $"second-{index:D6}";
            Description = $"fixture-description-{index:D6}";
            customOrder = (index * 37 + 11) % 100000;
            nested = new WallstopStudios.DataVisualizer.Benchmark.BenchmarkNestedData
            {
                key = identity,
                shared = shared,
                values = Enumerable.Range(0, 4).Select(item => item * 3 + index).ToList(),
            };
        }
    }
}

namespace WallstopStudios.DataVisualizer.Benchmark
{
    public static class DataVisualizerBenchmarkEntryPoint
    {
        private const int FixtureSize = __FIXTURE_SIZE__;
        private const string FixtureRoot = __FIXTURE_ROOT__;
        private const string ResultPath = __RESULT_PATH__;
        private const string OwnershipIdentity = __OWNERSHIP_IDENTITY__;
        private const string Revision = __REVISION__;
        private const string UnityVersionArgument = __UNITY_VERSION__;
        private const string Suite = __SUITE__;
        private const bool KeepFixture = __KEEP_FIXTURE__;
        private const bool ExitEditor = __EXIT_EDITOR__;
        private const int WarmupCount = 5;
        private const int SampleCount = 30;
        private const int MaximumWaitTicks = 300;
        private const double PlayEntryTargetMilliseconds = 20.0;
        private const int FixtureBatchSize = 500;

        private enum Phase
        {
            Idle,
            Prepare,
            StartPlayCase,
            AwaitOpenIdle,
            AwaitOpenIndexing,
            CreateFixture,
            VerifyFixture,
            AwaitPlayEntry,
            AwaitEditMode,
            Finish,
            Failed,
        }

        [Serializable]
        private sealed class SampleSet
        {
            public double[] warmups = Array.Empty<double>();
            public double[] samples = Array.Empty<double>();
            public double p95Milliseconds;
            public double medianMilliseconds;
        }

        [Serializable]
        private sealed class MetricEvidence
        {
            public string caseName;
            public string status;
            public string reason;
            public int warmupCount;
            public int sampleCount;
            public double[] warmups = Array.Empty<double>();
            public double[] samples = Array.Empty<double>();
            public double p95Milliseconds;
            public double medianMilliseconds;
            public string targetStatus;
        }

        [Serializable]
        private sealed class PlayEntryObservation
        {
            public int repetition;
            public string caseName;
            public int logicalCaseIndex;
            public int caseIndex;
            public bool warmup;
            public double elapsedMilliseconds;
        }

        [Serializable]
        private sealed class PlayModeSettingsSnapshot
        {
            public bool enabled;
            public int options;
        }

        [Serializable]
        private sealed class BenchmarkResult
        {
            public string schemaVersion = "1";
            public string status;
            public string error;
            public string packageRevision;
            public string packageVersion;
            public string unityVersion;
            public string unityVersionArgument;
            public string suite;
            public int fixtureSize;
            public int fixtureAssetCount;
            public int duplicateShortNameCount;
            public bool fixtureVerified;
            public bool cleanupCompleted;
            public string operatingSystem;
            public string cpu;
            public int memoryMegabytes;
            public string storage;
            public string graphicsDevice;
            public float dpi;
            public float pixelsPerPoint;
            public string theme;
            public string windowRect;
            public bool originalEnterPlayModeOptionsEnabled;
            public string originalEnterPlayModeOptions;
            public bool enterPlayModeOptionsEnabled;
            public string enterPlayModeOptions;
            public double fixtureGenerationMilliseconds;
            public double fixtureValidationMilliseconds;
            public double shellConstructionMilliseconds;
            public double playEntryTargetMilliseconds = PlayEntryTargetMilliseconds;
            public bool playEntryTargetMet;
            public SampleSet playEntryOpen = new();
            public SampleSet playEntryOpenIdle = new();
            public SampleSet playEntryOpenIndexing = new();
            public SampleSet playEntryClosed = new();
            public MetricEvidence[] playEntryMetrics = Array.Empty<MetricEvidence>();
            public PlayEntryObservation[] playEntryObservations = Array.Empty<PlayEntryObservation>();
            public string[] unavailableMetrics = Array.Empty<string>();
        }

        private static readonly Stopwatch Clock = new();
        private static readonly ProfilerMarker FixtureGenerationMarker = new("DataVisualizer.Benchmark.FixtureGeneration");
        private static readonly ProfilerMarker FixtureValidationMarker = new("DataVisualizer.Benchmark.FixtureValidation");
        private static readonly ProfilerMarker ShellConstructionMarker = new("DataVisualizer.Benchmark.ShellConstruction");
        private static readonly ProfilerMarker PlayEntryRequestMarker = new("DataVisualizer.Benchmark.PlayEntryRequest");
        private static readonly List<double> OpenWarmups = new();
        private static readonly List<double> OpenSamples = new();
        private static readonly List<double> OpenIndexingWarmups = new();
        private static readonly List<double> OpenIndexingSamples = new();
        private static readonly List<double> ClosedWarmups = new();
        private static readonly List<double> ClosedSamples = new();
        private static readonly List<PlayEntryObservation> PlayEntryObservations = new();
        private static BenchmarkResult _result;
        private static Phase _phase;
        private static int _repetition;
        private static int _caseIndex;
        private static int _waitTicks;
        private static long _playRequestTimestamp;
        private static bool _started;
        private static EditorWindow _window;
        private static bool _originalEnterPlayModeOptionsEnabled;
        private static EnterPlayModeOptions _originalEnterPlayModeOptions;
        private static bool _playModeSettingsCaptured;
        private static BenchmarkSharedData _fixtureShared;
        private static int _nextFixtureIndex;
        private static long _fixtureGenerationStartTimestamp;

        public static string Run()
        {
            if (_started)
            {
                return Status();
            }

            _started = true;
            OpenWarmups.Clear();
            OpenSamples.Clear();
            OpenIndexingWarmups.Clear();
            OpenIndexingSamples.Clear();
            ClosedWarmups.Clear();
            ClosedSamples.Clear();
            PlayEntryObservations.Clear();
            CaptureAndConfigurePlayModeSettings();
            _phase = Phase.Prepare;
            _result = CreateResult();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return Status();
        }

        public static string Status()
        {
            if (_result == null)
            {
                return "{\"status\":\"not-started\"}";
            }
            return JsonUtility.ToJson(_result);
        }

        public static string Cleanup()
        {
            EditorApplication.update -= Tick;
            _window?.Close();
            _window = null;
            try
            {
                bool preserveSuccessfulFixture = KeepFixture && _result?.status == "completed";
                if (preserveSuccessfulFixture)
                {
                    return "cleanup-complete-fixture-kept";
                }
                CleanupFixture();
                return "cleanup-complete";
            }
            catch (Exception error)
            {
                return "cleanup-failed: " + error.Message;
            }
            finally
            {
                RestorePlayModeSettings();
            }
        }

        private static void CaptureAndConfigurePlayModeSettings()
        {
            _originalEnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _originalEnterPlayModeOptions = EditorSettings.enterPlayModeOptions;
            string snapshot = JsonUtility.ToJson(
                new PlayModeSettingsSnapshot
                {
                    enabled = _originalEnterPlayModeOptionsEnabled,
                    options = (int)_originalEnterPlayModeOptions,
                }
            );
            string snapshotPath = ResultPath + ".playmode-settings";
            string snapshotDirectory = Path.GetDirectoryName(snapshotPath);
            if (!string.IsNullOrWhiteSpace(snapshotDirectory))
            {
                Directory.CreateDirectory(snapshotDirectory);
            }
            File.WriteAllText(snapshotPath, snapshot);
            _playModeSettingsCaptured = true;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions =
                EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        }

        private static void RestorePlayModeSettings()
        {
            if (_playModeSettingsCaptured)
            {
                EditorSettings.enterPlayModeOptionsEnabled = _originalEnterPlayModeOptionsEnabled;
                EditorSettings.enterPlayModeOptions = _originalEnterPlayModeOptions;
                _playModeSettingsCaptured = false;
                File.Delete(ResultPath + ".playmode-settings");
                return;
            }

            string snapshotPath = ResultPath + ".playmode-settings";
            if (!File.Exists(snapshotPath))
            {
                return;
            }
            PlayModeSettingsSnapshot snapshot = JsonUtility.FromJson<PlayModeSettingsSnapshot>(
                File.ReadAllText(snapshotPath)
            );
            if (snapshot == null)
            {
                throw new InvalidOperationException("Persisted Play Mode settings snapshot is invalid");
            }
            EditorSettings.enterPlayModeOptionsEnabled = snapshot.enabled;
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)snapshot.options;
            File.Delete(snapshotPath);
        }

        private static void Tick()
        {
            try
            {
                switch (_phase)
                {
                    case Phase.Prepare:
                        Prepare();
                        break;
                    case Phase.CreateFixture:
                        CreateFixtureBatch();
                        break;
                    case Phase.VerifyFixture:
                        VerifyFixtureAndContinue();
                        break;
                    case Phase.StartPlayCase:
                        StartPlayCase();
                        break;
                    case Phase.AwaitOpenIdle:
                        AwaitOpenIdle();
                        break;
                    case Phase.AwaitOpenIndexing:
                        AwaitOpenIndexing();
                        break;
                    case Phase.AwaitPlayEntry:
                        AwaitPlayEntry();
                        break;
                    case Phase.AwaitEditMode:
                        AwaitEditMode();
                        break;
                    case Phase.Finish:
                        Finish();
                        break;
                    case Phase.Failed:
                        Finish();
                        break;
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private static BenchmarkResult CreateResult()
        {
            return new BenchmarkResult
            {
                status = "running",
                packageRevision = Revision,
                packageVersion = "__PACKAGE_VERSION__",
                unityVersion = Application.unityVersion,
                unityVersionArgument = UnityVersionArgument,
                suite = Suite,
                fixtureSize = FixtureSize,
                operatingSystem = SystemInfo.operatingSystem,
                cpu = SystemInfo.processorType,
                memoryMegabytes = SystemInfo.systemMemorySize,
                storage = GetStorageDescription(),
                graphicsDevice = SystemInfo.graphicsDeviceName,
                dpi = Screen.dpi,
                pixelsPerPoint = EditorGUIUtility.pixelsPerPoint,
                theme = EditorGUIUtility.isProSkin ? "dark" : "light",
                originalEnterPlayModeOptionsEnabled = _originalEnterPlayModeOptionsEnabled,
                originalEnterPlayModeOptions = _originalEnterPlayModeOptions.ToString(),
                enterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled,
                enterPlayModeOptions = EditorSettings.enterPlayModeOptions.ToString(),
                unavailableMetrics = new[]
                {
                    "indexed-search",
                    "selection-readiness",
                    "label-filtering",
                    "scroll-frame",
                    "retained-reference-memory",
                    "package-absent-control",
                    "cold-import",
                    "warm-restart",
                    "incremental-edit",
                },
            };
        }

        private static void Prepare()
        {
            Clock.Restart();
            if (AssetDatabase.IsValidFolder(FixtureRoot))
            {
                throw new InvalidOperationException(
                    "Fixture path already exists; refusing to delete an unowned benchmark fixture"
                );
            }
            AssetDatabase.CreateFolder("Assets", "__DataVisualizerBenchmarkFixture");

            _fixtureShared = ScriptableObject.CreateInstance<BenchmarkSharedData>();
            _fixtureShared.identity = OwnershipIdentity;
            AssetDatabase.CreateAsset(_fixtureShared, FixtureRoot + "/Shared.asset");
            _nextFixtureIndex = 0;
            _fixtureGenerationStartTimestamp = Stopwatch.GetTimestamp();
            _result.status = "fixture-generating";
            _phase = Phase.CreateFixture;
        }

        private static void CreateFixtureBatch()
        {
            _fixtureShared = AssetDatabase.LoadAssetAtPath<BenchmarkSharedData>(
                FixtureRoot + "/Shared.asset"
            );
            if (_fixtureShared == null)
            {
                throw new InvalidOperationException("Shared fixture asset could not be reloaded");
            }
            Type[] types =
            {
                typeof(PlainData),
                typeof(BaseData),
                typeof(WallstopStudios.DataVisualizer.Benchmark.First.Data),
                typeof(WallstopStudios.DataVisualizer.Benchmark.Second.Data),
            };
            int end = Math.Min(_nextFixtureIndex + FixtureBatchSize, FixtureSize);
            using (FixtureGenerationMarker.Auto())
            {
                for (; _nextFixtureIndex < end; _nextFixtureIndex++)
                {
                    int index = _nextFixtureIndex;
                    Type type = types[index % types.Length];
                    var fixture = (ScriptableObject)ScriptableObject.CreateInstance(type);
                    ((IBenchmarkFixture)fixture).Configure(index, _fixtureShared);
                    string path = FixtureRoot + "/Item_" + index.ToString("D6") + ".asset";
                    AssetDatabase.CreateAsset(fixture, path);
                    AssetDatabase.SetLabels(
                        fixture,
                        new[]
                        {
                            "benchmark",
                            index % 2 == 0 ? "ordinary" : "base-data-object",
                            "group-" + (index % 8).ToString("D2"),
                        }
                    );
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (_nextFixtureIndex >= FixtureSize)
            {
                _result.fixtureGenerationMilliseconds = ElapsedMilliseconds(
                    _fixtureGenerationStartTimestamp
                );
                _phase = Phase.VerifyFixture;
            }
            else
            {
                _result.status = $"fixture-generating-{_nextFixtureIndex}/{FixtureSize}";
            }
        }

        private static void VerifyFixtureAndContinue()
        {
            long validationStart = Stopwatch.GetTimestamp();
            using (FixtureValidationMarker.Auto())
            {
                VerifyFixture();
            }
            _result.fixtureValidationMilliseconds = ElapsedMilliseconds(validationStart);
            _result.status = "fixture-verified";

            if (Suite == "fixture")
            {
                _phase = Phase.Finish;
                return;
            }

            long shellStart = Stopwatch.GetTimestamp();
            using (ShellConstructionMarker.Auto())
            {
                _window = EditorWindow.GetWindow<WallstopStudios.DataVisualizer.Editor.DataVisualizer>();
                _window?.Show();
            }
            _result.shellConstructionMilliseconds = ElapsedMilliseconds(shellStart);
            _result.windowRect = _window == null ? "unavailable" : _window.position.ToString();
            _repetition = 0;
            _caseIndex = 0;
            _phase = Phase.StartPlayCase;
        }

        private static int CurrentCaseIndex()
        {
            return _repetition % 2 == 0 ? _caseIndex : 2 - _caseIndex;
        }

        private static void StartPlayCase()
        {
            if (Suite != "all" && Suite != "play-entry")
            {
                _phase = Phase.Finish;
                return;
            }

            int caseIndex = CurrentCaseIndex();
            if (caseIndex == 0)
            {
                EnsureWindowOpen();
                _waitTicks = 0;
                _phase = Phase.AwaitOpenIdle;
                return;
            }

            if (caseIndex == 1)
            {
                _window?.Close();
                _window = null;
                EnsureWindowOpen();
                BeginBenchmarkIndexing();
                _waitTicks = 0;
                _phase = Phase.AwaitOpenIndexing;
                return;
            }

            _window?.Close();
            _window = null;
            RequestPlayEntry("closed");
        }

        private static void EnsureWindowOpen()
        {
            if (_window != null)
            {
                return;
            }

            using (ShellConstructionMarker.Auto())
            {
                _window = EditorWindow.GetWindow<WallstopStudios.DataVisualizer.Editor.DataVisualizer>();
                _window?.Show();
            }
        }

        private static void RequestPlayEntry(string caseName)
        {
            if (EditorApplication.isPlaying)
            {
                _waitTicks = 0;
                EditorApplication.isPlaying = false;
                _phase = Phase.AwaitEditMode;
                return;
            }

            _playRequestTimestamp = Stopwatch.GetTimestamp();
            using (PlayEntryRequestMarker.Auto())
            {
                EditorApplication.isPlaying = true;
            }
            _waitTicks = 0;
            _phase = Phase.AwaitPlayEntry;
            _result.status = $"starting-{caseName}-play-entry-{_repetition + 1}";
        }

        private static void AwaitOpenIdle()
        {
            _waitTicks++;
            if (IsWindowIdle())
            {
                RequestPlayEntry("open-idle");
                return;
            }

            if (_waitTicks >= MaximumWaitTicks)
            {
                Fail(new InvalidOperationException("Window did not become idle before the benchmark timeout"));
            }
        }

        private static void AwaitOpenIndexing()
        {
            _waitTicks++;
            if (IsWindowIndexing())
            {
                RequestPlayEntry("open-indexing");
                return;
            }

            if (_waitTicks % 5 == 0)
            {
                BeginBenchmarkIndexing();
            }
            if (_waitTicks >= MaximumWaitTicks)
            {
                Fail(new InvalidOperationException("Window did not enter indexing before the benchmark timeout"));
            }
        }

        private static bool IsWindowIdle()
        {
            return _window != null
                && !GetWindowBool("_isLoadingObjectsAsync")
                && !GetWindowBool("_isLoadingSearchCacheAsync")
                && !GetWindowBool("_deferredInitializationPending")
                && GetWindowBool("_isSearchCachePopulated");
        }

        private static bool IsWindowIndexing()
        {
            return _window != null
                && (
                    GetWindowBool("_isLoadingObjectsAsync")
                    || GetWindowBool("_isLoadingSearchCacheAsync")
                );
        }

        private static bool GetWindowBool(string fieldName)
        {
            if (_window == null)
            {
                return false;
            }
            var field = _window.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            );
            return field?.GetValue(_window) is bool value && value;
        }

        private static void BeginBenchmarkIndexing()
        {
            if (_window == null)
            {
                return;
            }

            var flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var windowType = _window.GetType();
            var catalog = (Dictionary<string, List<Type>>)
                windowType.GetField("_scriptableObjectTypes", flags).GetValue(_window);
            Type[] benchmarkTypes =
            {
                typeof(PlainData),
                typeof(BaseData),
                // ScriptableObject is intentionally included so the search-cache query has more
                // than one async batch even at the minimum 100-asset fixture size.
                typeof(ScriptableObject),
                typeof(WallstopStudios.DataVisualizer.Benchmark.First.Data),
                typeof(WallstopStudios.DataVisualizer.Benchmark.Second.Data),
            };
            foreach (Type type in benchmarkTypes)
            {
                if (!catalog.TryGetValue(type.Namespace, out List<Type> types))
                {
                    types = new List<Type>();
                    catalog[type.Namespace] = types;
                }
                if (!types.Contains(type))
                {
                    types.Add(type);
                }
            }

            windowType
                .GetMethod("LoadObjectTypesAsync", flags)
                .Invoke(_window, new object[] { typeof(PlainData), false });
            windowType.GetMethod("PopulateSearchCacheAsync", flags).Invoke(_window, null);
        }

        private static void AwaitPlayEntry()
        {
            _waitTicks++;
            if (EditorApplication.isPlaying)
            {
                double elapsed = ElapsedMilliseconds(_playRequestTimestamp);
                List<double> warmups;
                List<double> samples;
                int caseIndex = CurrentCaseIndex();
                string caseName;
                if (caseIndex == 0)
                {
                    warmups = OpenWarmups;
                    samples = OpenSamples;
                    caseName = "open-idle";
                }
                else if (caseIndex == 1)
                {
                    warmups = OpenIndexingWarmups;
                    samples = OpenIndexingSamples;
                    caseName = "open-indexing";
                }
                else
                {
                    warmups = ClosedWarmups;
                    samples = ClosedSamples;
                    caseName = "closed";
                }
                bool warmup = warmups.Count < WarmupCount;
                (warmup ? warmups : samples).Add(elapsed);
                PlayEntryObservations.Add(
                    new PlayEntryObservation
                    {
                        repetition = _repetition,
                        caseName = caseName,
                        logicalCaseIndex = caseIndex,
                        caseIndex = _caseIndex,
                        warmup = warmup,
                        elapsedMilliseconds = elapsed,
                    }
                );
                _waitTicks = 0;
                EditorApplication.isPlaying = false;
                _phase = Phase.AwaitEditMode;
                return;
            }

            if (_waitTicks >= MaximumWaitTicks)
            {
                _result.unavailableMetrics = _result.unavailableMetrics
                    .Concat(new[] { "play-entry: editor did not enter Play Mode" })
                    .ToArray();
                _phase = Phase.Finish;
            }
        }

        private static void AwaitEditMode()
        {
            _waitTicks++;
            if (EditorApplication.isPlaying)
            {
                if (_waitTicks >= MaximumWaitTicks)
                {
                    Fail(new InvalidOperationException("Editor did not return to Edit Mode"));
                }
                return;
            }

            _waitTicks = 0;
            _caseIndex++;
            if (_caseIndex >= 3)
            {
                _caseIndex = 0;
                _repetition++;
            }

            if (_repetition >= WarmupCount + SampleCount)
            {
                _phase = Phase.Finish;
            }
            else
            {
                _phase = Phase.StartPlayCase;
            }
        }

        private static void VerifyFixture()
        {
            BenchmarkSharedData shared = AssetDatabase.LoadAssetAtPath<BenchmarkSharedData>(
                FixtureRoot + "/Shared.asset"
            );
            if (shared == null)
            {
                throw new InvalidOperationException("Shared fixture asset is missing");
            }
            string[] guids = AssetDatabase.FindAssets("", new[] { FixtureRoot });
            int itemCount = 0;
            int duplicateShortNameCount = 0;
            int firstDataCount = 0;
            int secondDataCount = 0;
            HashSet<string> identities = new(StringComparer.Ordinal);
            HashSet<int> fixtureIndexes = new();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("/Item_", StringComparison.Ordinal))
                {
                    continue;
                }
                itemCount++;
                string itemName = Path.GetFileNameWithoutExtension(path);
                if (!int.TryParse(itemName.Replace("Item_", ""), out int itemIndex))
                {
                    throw new InvalidOperationException("Fixture item path verification failed");
                }
                if (!fixtureIndexes.Add(itemIndex))
                {
                    throw new InvalidOperationException("Fixture contains duplicate item indexes");
                }
                Type expectedType = itemIndex % 4 switch
                {
                    0 => typeof(PlainData),
                    1 => typeof(BaseData),
                    2 => typeof(WallstopStudios.DataVisualizer.Benchmark.First.Data),
                    _ => typeof(WallstopStudios.DataVisualizer.Benchmark.Second.Data),
                };
                var asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
                if (
                    asset is not IBenchmarkFixture fixture
                    || asset.GetType() != expectedType
                    || !identities.Add(fixture.Identity)
                    || fixture.Description != $"fixture-description-{itemIndex:D6}"
                    || fixture.CustomOrder != (itemIndex * 37 + 11) % 100000
                    || fixture.SharedReference != shared
                    || (fixture is WallstopStudios.DataVisualizer.BaseDataObject)
                        != (itemIndex % 2 == 1)
                )
                {
                    throw new InvalidOperationException(
                        "Fixture identity/type/shared-reference verification failed"
                    );
                }
                if (asset.GetType().Name == "Data")
                {
                    duplicateShortNameCount++;
                    if (asset.GetType() == typeof(WallstopStudios.DataVisualizer.Benchmark.First.Data))
                    {
                        firstDataCount++;
                    }
                    else
                    {
                        secondDataCount++;
                    }
                }
                string[] labels = AssetDatabase.GetLabels(asset);
                string expectedKind = itemIndex % 2 == 0 ? "ordinary" : "base-data-object";
                int expectedNestedCount = itemIndex % 4 < 2 ? 8 : 4;
                int nestedMultiplier = itemIndex % 4 switch
                {
                    1 => 2,
                    3 => 3,
                    _ => 1,
                };
                if (
                    !labels.Contains("benchmark")
                    || !labels.Contains(expectedKind)
                    || !labels.Contains("group-" + (itemIndex % 8).ToString("D2"))
                    || fixture.Nested == null
                    || fixture.Nested.key != fixture.Identity
                    || fixture.Nested.values == null
                    || fixture.Nested.values.Count != expectedNestedCount
                    || !fixture.Nested.values.SequenceEqual(
                        Enumerable.Range(0, expectedNestedCount).Select(value =>
                            value * nestedMultiplier + itemIndex
                        )
                    )
                )
                {
                    throw new InvalidOperationException("Fixture label verification failed");
                }
            }
            if (itemCount != FixtureSize)
            {
                throw new InvalidOperationException($"Expected {FixtureSize} fixture assets, found {itemCount}");
            }
            if (duplicateShortNameCount == 0)
            {
                throw new InvalidOperationException("Duplicate short-name fixture coverage is missing");
            }
            if (
                fixtureIndexes.Count != FixtureSize
                || fixtureIndexes.Min() != 0
                || fixtureIndexes.Max() != FixtureSize - 1
                || firstDataCount != (FixtureSize + 1) / 4
                || secondDataCount != FixtureSize / 4
            )
            {
                throw new InvalidOperationException("Fixture type or index coverage is incomplete");
            }
            _result.fixtureAssetCount = itemCount;
            _result.duplicateShortNameCount = duplicateShortNameCount;
            _result.fixtureVerified = true;
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            if (_result.status != "failed")
            {
                _result.status = "completed";
            }
            _result.playEntryOpen = MakeSampleSet(OpenWarmups, OpenSamples);
            _result.playEntryOpenIdle = _result.playEntryOpen;
            _result.playEntryOpenIndexing = MakeSampleSet(OpenIndexingWarmups, OpenIndexingSamples);
            _result.playEntryClosed = MakeSampleSet(ClosedWarmups, ClosedSamples);
            _result.playEntryMetrics = new[]
            {
                MakeMetricEvidence("open-idle", _result.playEntryOpenIdle, OpenWarmups, OpenSamples),
                MakeMetricEvidence(
                    "open-indexing",
                    _result.playEntryOpenIndexing,
                    OpenIndexingWarmups,
                    OpenIndexingSamples
                ),
                MakeMetricEvidence("closed", _result.playEntryClosed, ClosedWarmups, ClosedSamples),
            };
            _result.playEntryObservations = PlayEntryObservations.ToArray();
            if (Suite != "fixture" && _result.playEntryMetrics.Any(metric => metric.status != "measured"))
            {
                _result.status = "failed";
                _result.error = "One or more required Play-entry metrics did not complete.";
            }
            _result.playEntryTargetMet =
                _result.playEntryOpenIdle.samples.Length == SampleCount
                && _result.playEntryOpenIndexing.samples.Length == SampleCount
                && _result.playEntryClosed.samples.Length == SampleCount
                && _result.playEntryOpenIdle.p95Milliseconds <= PlayEntryTargetMilliseconds
                && _result.playEntryOpenIndexing.p95Milliseconds <= PlayEntryTargetMilliseconds
                && _result.playEntryClosed.p95Milliseconds <= PlayEntryTargetMilliseconds;
            try
            {
                _window?.Close();
                _window = null;
                if (!KeepFixture)
                {
                    CleanupFixture();
                    _result.cleanupCompleted = true;
                }
                WriteResult();
            }
            catch (Exception error)
            {
                _result.status = "failed";
                _result.error = error.ToString();
                try
                {
                    WriteResult();
                }
                catch
                {
                    // Preserve the original benchmark failure.
                }
            }
            finally
            {
                RestorePlayModeSettings();
            }
            if (ExitEditor)
            {
                EditorApplication.Exit(_result.status == "completed" ? 0 : 1);
            }
        }

        private static MetricEvidence MakeMetricEvidence(
            string caseName,
            SampleSet sampleSet,
            List<double> warmups,
            List<double> samples
        )
        {
            bool measured = warmups.Count >= WarmupCount && samples.Count >= SampleCount;
            string status = measured ? "measured" : Suite == "fixture" ? "unavailable" : "failed";
            return new MetricEvidence
            {
                caseName = caseName,
                status = status,
                reason = measured
                    ? "Unity Stopwatch measured the complete warm-up/sample set."
                    : Suite == "fixture"
                        ? "The fixture suite does not measure Play-entry latency."
                        : "The required Play-entry sample set did not complete.",
                warmupCount = warmups.Count,
                sampleCount = samples.Count,
                warmups = warmups.ToArray(),
                samples = samples.ToArray(),
                p95Milliseconds = sampleSet.p95Milliseconds,
                medianMilliseconds = sampleSet.medianMilliseconds,
                targetStatus = measured
                    ? sampleSet.p95Milliseconds <= PlayEntryTargetMilliseconds ? "pass" : "miss"
                    : status,
            };
        }

        private static void Fail(Exception error)
        {
            _result ??= CreateResult();
            _result.status = "failed";
            _result.error = error.ToString();
            _phase = Phase.Failed;
        }

        private static void CleanupFixture()
        {
            if (AssetDatabase.IsValidFolder(FixtureRoot))
            {
                BenchmarkSharedData shared = AssetDatabase.LoadAssetAtPath<BenchmarkSharedData>(
                    FixtureRoot + "/Shared.asset"
                );
                if (shared == null || shared.identity != OwnershipIdentity)
                {
                    throw new InvalidOperationException(
                        "refusing to delete an unowned benchmark fixture"
                    );
                }
                if (!AssetDatabase.DeleteAsset(FixtureRoot))
                {
                    throw new InvalidOperationException("Unity refused to delete the benchmark fixture");
                }
                AssetDatabase.Refresh();
                if (AssetDatabase.IsValidFolder(FixtureRoot))
                {
                    throw new InvalidOperationException("Benchmark fixture still exists after deletion");
                }
            }
            _fixtureShared = null;
        }

        private static void WriteResult()
        {
            if (string.IsNullOrWhiteSpace(ResultPath))
            {
                return;
            }
            string parent = Path.GetDirectoryName(ResultPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }
            File.WriteAllText(ResultPath, JsonUtility.ToJson(_result, true));
        }

        private static SampleSet MakeSampleSet(List<double> warmups, List<double> samples)
        {
            double[] warmupValues = warmups.ToArray();
            double[] sampleValues = samples.ToArray();
            double[] ordered = sampleValues.OrderBy(value => value).ToArray();
            return new SampleSet
            {
                warmups = warmupValues,
                samples = sampleValues,
                p95Milliseconds = ordered.Length == 0 ? 0 : ordered[Mathf.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)],
                medianMilliseconds = ordered.Length == 0 ? 0 : ordered[ordered.Length / 2],
            };
        }

        private static double ElapsedMilliseconds(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        }

        private static string GetStorageDescription()
        {
            try
            {
                string root = Path.GetPathRoot(Application.dataPath);
                if (string.IsNullOrWhiteSpace(root))
                {
                    return "unavailable";
                }
                DriveInfo drive = new(root);
                return $"{drive.Name} free={drive.AvailableFreeSpace} total={drive.TotalSize}";
            }
            catch (Exception error)
            {
                return "unavailable: " + error.GetType().Name;
            }
        }
    }
}
'''


def render_bootstrap(config: BenchmarkConfig, result_path: str) -> str:
    values = {
        "FIXTURE_SIZE": str(config.fixture_size),
        "FIXTURE_ROOT": csharp_string("Assets/__DataVisualizerBenchmarkFixture"),
        "RESULT_PATH": csharp_string(result_path),
        "OWNERSHIP_IDENTITY": csharp_string("benchmark-owner:" + result_path),
        "REVISION": csharp_string(config.revision),
        "UNITY_VERSION": csharp_string(config.unity_version),
        "SUITE": csharp_string(config.suite),
        "KEEP_FIXTURE": "true" if config.keep_fixture else "false",
        "EXIT_EDITOR": "true" if config.mode == "direct" else "false",
        "PACKAGE_VERSION": config.package_version,
    }
    return replace_bootstrap_tokens(BOOTSTRAP_TEMPLATE, values)


def build_argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Run disposable local Data Visualizer benchmark scenarios."
    )
    parser.add_argument(
        "--host-project",
        required=True,
        help="Unity project path as visible to the editor host.",
    )
    parser.add_argument("--unity-version", required=True, help="Expected Unity editor version.")
    parser.add_argument(
        "--fixture-size",
        required=True,
        type=parse_fixture_size,
        help="Deterministic fixture size: 100, 1000, 10000, or 50000.",
    )
    parser.add_argument(
        "--suite",
        choices=("fixture", "play-entry", "all"),
        default="all",
        help="Benchmark subset to run (default: all).",
    )
    parser.add_argument(
        "--output-dir",
        required=True,
        type=Path,
        help="Local directory for the machine-readable report and raw samples.",
    )
    parser.add_argument("--mode", choices=("direct", "mcp"), default="direct")
    parser.add_argument("--unity-path", default="unity", help="Unity executable for direct mode.")
    parser.add_argument(
        "--mcp-url",
        default=os.environ.get("UNITY_MCP_URL", "http://host.docker.internal:9020/mcp"),
        help="Streamable HTTP MCP endpoint for MCP mode.",
    )
    parser.add_argument("--mcp-token", help="Bearer token; defaults to UNITY_MCP_TOKEN/.env.local.")
    parser.add_argument(
        "--timeout-seconds",
        type=float,
        default=1_800.0,
        help="Overall editor operation timeout (default: 1800 seconds).",
    )
    parser.add_argument(
        "--keep-fixture",
        action="store_true",
        help="Keep the disposable host fixture for inspection after a successful run.",
    )
    parser.add_argument(
        "--path-map",
        type=parse_path_mapping,
        help="Translate a local/container prefix to a host prefix (LOCAL_PREFIX=HOST_PREFIX).",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Validate configuration and print the planned execution without changing Unity.",
    )
    return parser


def config_from_args(args: argparse.Namespace, root: Path) -> BenchmarkConfig:
    if args.timeout_seconds <= 0:
        raise BenchmarkError("--timeout-seconds must be positive")
    host_project = str(args.host_project)
    if args.mode == "direct":
        project_path = Path(host_project).expanduser()
        if not project_path.is_dir():
            raise BenchmarkError(f"Direct Unity project does not exist: {project_path}")
        host_project = str(project_path.resolve())
    output_dir = args.output_dir.expanduser().resolve()
    if args.path_map is not None:
        args.path_map.translate(output_dir)
    return BenchmarkConfig(
        host_project=host_project,
        unity_version=args.unity_version,
        fixture_size=args.fixture_size,
        suite=args.suite,
        output_dir=output_dir,
        mode=args.mode,
        unity_path=args.unity_path,
        mcp_url=args.mcp_url,
        mcp_token=resolve_token(args.mcp_token),
        timeout_seconds=args.timeout_seconds,
        keep_fixture=args.keep_fixture,
        path_mapping=args.path_map,
        revision=package_revision(root),
        package_version=package_version(root),
    )


def report_path(config: BenchmarkConfig) -> Path:
    return config.output_dir / (
        f"data-visualizer-{config.suite}-{config.fixture_size}-{config.unity_version}.json"
    )


def comparison_report_path(config: BenchmarkConfig) -> Path:
    return report_path(config).with_suffix(".md")


def planned_execution(config: BenchmarkConfig) -> dict[str, Any]:
    mapped_output = (
        None
        if config.path_mapping is None
        else config.path_mapping.translate(report_path(config))
    )
    return {
        "schemaVersion": SCHEMA_VERSION,
        "mode": config.mode,
        "hostProject": config.host_project,
        "unityVersion": config.unity_version,
        "fixtureSize": config.fixture_size,
        "suite": config.suite,
        "output": str(report_path(config)),
        "hostOutput": mapped_output,
        "comparisonOutput": str(comparison_report_path(config)),
        "pathMap": None
        if config.path_mapping is None
        else {
            "local": str(config.path_mapping.local_prefix),
            "host": config.path_mapping.host_prefix,
        },
        "warmups": 5,
        "samples": 30,
        "playEntryCases": list(PLAY_ENTRY_CASES),
        "playEntryCaseOrder": [
            case_order_for_repetition(0),
            case_order_for_repetition(1),
        ],
        "playEntrySchedule": case_schedule(),
        "timingBoundary": "Unity Stopwatch/Profiler-side operation timing; orchestration excluded",
    }


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def _metric_line(value: Any) -> str:
    if value is None:
        return "unavailable"
    if isinstance(value, (int, float)):
        return f"{value:.3f} ms"
    return str(value)


def write_comparison_report(path: Path, config: BenchmarkConfig, result: dict[str, Any]) -> None:
    """Write a concise, reviewable companion to the machine-readable JSON report."""

    evidence = {
        item.get("caseName"): item
        for item in result.get("playEntryMetrics", [])
        if isinstance(item, dict) and item.get("caseName")
    }
    open_idle_result = (
        evidence.get("open-idle")
        or result.get("playEntryOpenIdle")
        or result.get("playEntryOpen")
        or {}
    )
    open_indexing_result = evidence.get("open-indexing") or result.get("playEntryOpenIndexing") or {}
    closed_result = evidence.get("closed") or result.get("playEntryClosed") or {}
    target = result.get("playEntryTargetMilliseconds", 20)
    evidence_complete = all(case in evidence for case in PLAY_ENTRY_CASES)
    if result.get("suite") == "fixture":
        target_summary = "not measured (fixture suite)"
    elif evidence_complete and any(
        evidence[case].get("status") == "failed" for case in PLAY_ENTRY_CASES
    ):
        target_summary = "FAILED (required metric did not complete)"
    elif evidence_complete and any(
        evidence[case].get("status") == "unavailable" for case in PLAY_ENTRY_CASES
    ):
        target_summary = "UNAVAILABLE (required metric was not measured)"
    elif evidence_complete and all(
        evidence[case].get("status") == "measured"
        and evidence[case].get("targetStatus") == "pass"
        for case in PLAY_ENTRY_CASES
    ):
        target_summary = f"PASS (all three p95 values <= {target} ms)"
    elif evidence_complete and all(
        evidence[case].get("status") == "measured" for case in PLAY_ENTRY_CASES
    ):
        target_summary = f"MISS (measured p95 values must be <= {target} ms)"
    elif result.get("playEntryOpen") or result.get("playEntryOpenIdle"):
        target_summary = f"MISS (measured p95 values must be <= {target} ms)"
    else:
        target_summary = "INCOMPLETE (required metric evidence is missing)"
    unavailable = result.get("unavailableMetrics") or []

    lines = [
        "# Data Visualizer benchmark comparison",
        "",
        f"- Status: `{result.get('status', 'unknown')}`",
        f"- Suite: `{result.get('suite', config.suite)}`",
        f"- Fixture size: `{result.get('fixtureSize', config.fixture_size):,}` assets",
        f"- Package revision: `{result.get('packageRevision', config.revision)}`",
        f"- Package version: `{result.get('packageVersion', config.package_version)}`",
        f"- Unity version: `{result.get('unityVersion', 'unknown')}` (requested `{config.unity_version}`)",
        f"- Host: `{result.get('operatingSystem', 'unknown')}`, `{result.get('cpu', 'unknown')}`, `{result.get('memoryMegabytes', 'unknown')} MB RAM`",
        f"- Storage: `{result.get('storage', 'unknown')}`",
        f"- Display: `{result.get('graphicsDevice', 'unknown')}`, DPI `{result.get('dpi', 'unknown')}`, pixels-per-point `{result.get('pixelsPerPoint', 'unknown')}`, theme `{result.get('theme', 'unknown')}`",
        f"- Reload settings: enabled `{result.get('enterPlayModeOptionsEnabled', 'unknown')}`, options `{result.get('enterPlayModeOptions', 'unknown')}`",
        "",
        "## Fixture and shell timings",
        "",
        "| Operation | Unity-side time |",
        "| --- | ---: |",
        f"| Fixture generation | {_metric_line(result.get('fixtureGenerationMilliseconds'))} |",
        f"| Fixture validation | {_metric_line(result.get('fixtureValidationMilliseconds'))} |",
        f"| Shell construction | {_metric_line(result.get('shellConstructionMilliseconds'))} |",
        f"| Verified asset count | `{result.get('fixtureAssetCount', 'unknown')}` |",
        f"| Duplicate short-name assets | `{result.get('duplicateShortNameCount', 'unknown')}` |",
        "",
        "## Play-entry comparison",
        "",
        "The measured interval starts immediately before the Unity Play request and ends when Unity reports Play Mode. MCP polling and Python orchestration are excluded.",
        "",
        f"- Target: `<= {target} ms` p95 for all three cases",
        f"- Acceptance: **{target_summary}**",
        "",
        "| Case | Status | Warmups | Samples | Median | p95 |",
        "| --- | --- | ---: | ---: | ---: | ---: |",
        f"| Window open / idle | `{evidence.get('open-idle', {}).get('status', 'legacy')}` | `{len(open_idle_result.get('warmups') or [])}` | `{len(open_idle_result.get('samples') or [])}` | {_metric_line(open_idle_result.get('medianMilliseconds'))} | {_metric_line(open_idle_result.get('p95Milliseconds'))} |",
        f"| Window open / indexing | `{evidence.get('open-indexing', {}).get('status', 'legacy')}` | `{len(open_indexing_result.get('warmups') or [])}` | `{len(open_indexing_result.get('samples') or [])}` | {_metric_line(open_indexing_result.get('medianMilliseconds'))} | {_metric_line(open_indexing_result.get('p95Milliseconds'))} |",
        f"| Window closed | `{evidence.get('closed', {}).get('status', 'legacy')}` | `{len(closed_result.get('warmups') or [])}` | `{len(closed_result.get('samples') or [])}` | {_metric_line(closed_result.get('medianMilliseconds'))} | {_metric_line(closed_result.get('p95Milliseconds'))} |",
        "",
        "## Unavailable metrics",
        "",
    ]
    if unavailable:
        lines.extend(f"- `{item}`" for item in unavailable)
    else:
        lines.append("- None reported.")
    lines.extend(
        [
            "",
            "Raw samples and all metadata are in the adjacent JSON report.",
            "",
        ]
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines), encoding="utf-8")


def validate_result(config: BenchmarkConfig, result: dict[str, Any]) -> dict[str, Any]:
    if result.get("schemaVersion") != SCHEMA_VERSION:
        raise BenchmarkError("Unity benchmark result has an unsupported schema version")
    if result.get("status") != "completed":
        raise BenchmarkError(f"Unity benchmark did not complete: {result}")
    actual_version = result.get("unityVersion")
    if actual_version != config.unity_version:
        raise BenchmarkError(
            f"Unity version mismatch: requested {config.unity_version}, got {actual_version}"
        )
    if result.get("fixtureSize") != config.fixture_size:
        raise BenchmarkError(
            f"Fixture size mismatch: requested {config.fixture_size}, got {result.get('fixtureSize')}"
        )
    if result.get("fixtureVerified") is not True:
        raise BenchmarkError("Unity benchmark did not verify its fixture contents")
    if result.get("fixtureAssetCount") != config.fixture_size:
        raise BenchmarkError(
            "Fixture asset count mismatch: "
            f"expected {config.fixture_size}, got {result.get('fixtureAssetCount')}"
        )
    if not config.keep_fixture and result.get("cleanupCompleted") is not True:
        raise BenchmarkError("Unity benchmark did not report completed fixture cleanup")
    validate_metric_evidence(config, result)
    return result


def validate_metric_evidence(config: BenchmarkConfig, result: dict[str, Any]) -> None:
    """Require auditable status, samples, and alternating observations."""

    metric_items = result.get("playEntryMetrics")
    if not isinstance(metric_items, list):
        raise BenchmarkError("Benchmark metric evidence must be an array")
    if any(not isinstance(item, dict) for item in metric_items):
        raise BenchmarkError("Benchmark metric evidence entries must be objects")
    if any(not isinstance(item.get("caseName"), str) or not item["caseName"].strip() for item in metric_items):
        raise BenchmarkError("Benchmark metric evidence entries need a caseName")
    metrics = {item["caseName"]: item for item in metric_items}
    if len(metrics) != len(metric_items):
        raise BenchmarkError("Benchmark reported duplicate metric evidence case names")
    unknown = sorted(set(metrics) - set(PLAY_ENTRY_CASES))
    if unknown:
        raise BenchmarkError(f"Benchmark reported unknown metric evidence: {', '.join(unknown)}")
    missing = [case for case in PLAY_ENTRY_CASES if case not in metrics]
    if missing:
        raise BenchmarkError(f"Benchmark did not report metric evidence for: {', '.join(missing)}")

    for case in PLAY_ENTRY_CASES:
        metric = metrics[case]
        status = metric.get("status")
        if status not in {"measured", "unavailable", "failed"}:
            raise BenchmarkError(f"Metric {case} has an invalid status: {status}")
        if not isinstance(metric.get("reason"), str) or not metric["reason"].strip():
            raise BenchmarkError(f"Metric {case} must include a reason")
        if config.suite == "fixture":
            if status != "unavailable":
                raise BenchmarkError(f"Fixture-only metric {case} must be unavailable")
            continue
        if status != "measured":
            raise BenchmarkError(f"Required Play-entry metric {case} is {status}")
        if metric.get("warmupCount") != PLAY_ENTRY_WARMUPS:
            raise BenchmarkError(f"Metric {case} must contain exactly five warm-ups")
        if metric.get("sampleCount") != PLAY_ENTRY_SAMPLES:
            raise BenchmarkError(f"Metric {case} must contain exactly thirty samples")
        if len(metric.get("warmups") or []) != PLAY_ENTRY_WARMUPS:
            raise BenchmarkError(f"Metric {case} omitted or added warm-up observations")
        if len(metric.get("samples") or []) != PLAY_ENTRY_SAMPLES:
            raise BenchmarkError(f"Metric {case} omitted or added measured observations")
        if metric.get("warmupCount") != len(metric.get("warmups") or []):
            raise BenchmarkError(f"Metric {case} has an inconsistent warm-up count")
        if metric.get("sampleCount") != len(metric.get("samples") or []):
            raise BenchmarkError(f"Metric {case} has an inconsistent sample count")
        if metric.get("targetStatus") not in {"pass", "miss"}:
            raise BenchmarkError(f"Metric {case} has an invalid target status")
        p95 = metric.get("p95Milliseconds")
        target = result.get("playEntryTargetMilliseconds", 20)
        if not isinstance(p95, (int, float)) or isinstance(p95, bool) or not math.isfinite(p95):
            raise BenchmarkError(f"Metric {case} has an invalid p95 value")
        if not isinstance(target, (int, float)) or isinstance(target, bool) or not math.isfinite(target):
            raise BenchmarkError("Benchmark target must be a finite number")
        expected_target_status = "pass" if p95 <= target else "miss"
        if metric.get("targetStatus") != expected_target_status:
            raise BenchmarkError(f"Metric {case} has an inconsistent target status")
        for sample in (metric.get("warmups") or []) + (metric.get("samples") or []):
            if not isinstance(sample, (int, float)) or isinstance(sample, bool) or not math.isfinite(sample) or sample < 0:
                raise BenchmarkError(f"Metric {case} contains an invalid elapsed time")

    if config.suite == "fixture":
        return

    observations = result.get("playEntryObservations")
    if not isinstance(observations, list) or not observations:
        raise BenchmarkError("Measured Play-entry results must include labeled observations")
    by_repetition: dict[int, list[str]] = {}
    counts: dict[tuple[str, bool], int] = {}
    for observation in observations:
        if not isinstance(observation, dict):
            raise BenchmarkError("Play-entry observations must be objects")
        repetition = observation.get("repetition")
        case = observation.get("caseName")
        if (
            not isinstance(repetition, int)
            or isinstance(repetition, bool)
            or case not in PLAY_ENTRY_CASES
        ):
            raise BenchmarkError("Play-entry observations need repetition and caseName")
        case_index = observation.get("caseIndex")
        logical_case_index = observation.get("logicalCaseIndex")
        if (
            not isinstance(case_index, int)
            or isinstance(case_index, bool)
            or not isinstance(logical_case_index, int)
            or isinstance(logical_case_index, bool)
            or not isinstance(observation.get("warmup"), bool)
        ):
            raise BenchmarkError("Play-entry observations need caseIndex and warmup")
        if logical_case_index != PLAY_ENTRY_CASES.index(case):
            raise BenchmarkError("Play-entry observation logical case index is invalid")
        elapsed = observation.get("elapsedMilliseconds")
        if (
            not isinstance(elapsed, (int, float))
            or isinstance(elapsed, bool)
            or not math.isfinite(elapsed)
            or elapsed < 0
        ):
            raise BenchmarkError("Play-entry observations need elapsedMilliseconds")
        warmup = observation["warmup"]
        if warmup != (repetition < PLAY_ENTRY_WARMUPS):
            raise BenchmarkError("Play-entry observation has an inconsistent warm-up label")
        expected_order = case_order_for_repetition(repetition)
        if case_index < 0 or case_index >= len(expected_order) or expected_order[case_index] != case:
            raise BenchmarkError("Play-entry observation caseIndex does not match the planned order")
        by_repetition.setdefault(repetition, []).append(case)
        key = (case, observation["warmup"])
        counts[key] = counts.get(key, 0) + 1
    expected_repetitions = set(range(PLAY_ENTRY_WARMUPS + PLAY_ENTRY_SAMPLES))
    if set(by_repetition) != expected_repetitions:
        raise BenchmarkError("Play-entry observations must contain all planned repetitions")
    for repetition, cases in by_repetition.items():
        if cases != case_order_for_repetition(repetition):
            raise BenchmarkError(f"Play-entry repetition {repetition} is not in the planned case order")
    for case in PLAY_ENTRY_CASES:
        if counts.get((case, True), 0) < PLAY_ENTRY_WARMUPS:
            raise BenchmarkError(f"Play-entry case {case} has fewer than five labeled warm-ups")
        if counts.get((case, False), 0) < PLAY_ENTRY_SAMPLES:
            raise BenchmarkError(f"Play-entry case {case} has fewer than thirty labeled samples")


def parse_mcp_tool_result(payload: dict[str, Any]) -> Any:
    result = payload.get("result", {})
    if payload.get("isError") or (
        isinstance(result, dict) and result.get("isError")
    ):
        content = (
            payload.get("content", [])
            if payload.get("content")
            else result.get("content", [])
            if isinstance(result, dict)
            else []
        )
        detail = content[0].get("text") if content else "unknown MCP tool error"
        raise BenchmarkError(f"MCP tool error: {detail}")
    if "error" in payload:
        raise BenchmarkError(f"MCP error: {payload['error']}")
    content = result.get("content", []) if isinstance(result, dict) else []
    for item in content:
        if item.get("type") == "text":
            text = item.get("text", "")
            try:
                return json.loads(text)
            except json.JSONDecodeError:
                return text
    return result


class McpClient:
    def __init__(self, url: str, token: str | None, timeout: float) -> None:
        self.url = url
        self.token = token
        self.timeout = timeout
        self.request_id = 0

    def request(self, method: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        self.request_id += 1
        body = {
            "jsonrpc": "2.0",
            "id": self.request_id,
            "method": method,
            "params": params or {},
        }
        headers = {
            "Accept": "application/json, text/event-stream",
            "Content-Type": "application/json",
        }
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        request = urllib.request.Request(
            self.url,
            data=json.dumps(body).encode("utf-8"),
            headers=headers,
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                return json.loads(response.read().decode("utf-8"))
        except (OSError, urllib.error.URLError, json.JSONDecodeError) as error:
            raise BenchmarkError(f"MCP request {method} failed: {error}") from error

    def notify(self, method: str, params: dict[str, Any] | None = None) -> None:
        self.request_id += 1
        body = {"jsonrpc": "2.0", "method": method, "params": params or {}}
        headers = {"Accept": "application/json, text/event-stream", "Content-Type": "application/json"}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        request = urllib.request.Request(
            self.url,
            data=json.dumps(body).encode("utf-8"),
            headers=headers,
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout):
                return
        except OSError as error:
            raise BenchmarkError(f"MCP notification {method} failed: {error}") from error

    def call_tool(self, name: str, arguments: dict[str, Any]) -> Any:
        payload = self.request("tools/call", {"name": name, "arguments": arguments})
        return parse_mcp_tool_result(payload)


def extract_unity_result(value: Any) -> Any:
    """Normalize the nested result returned by Unity's eval tool."""

    for _ in range(4):
        if isinstance(value, dict) and "result" in value:
            value = value["result"]
            continue
        if isinstance(value, dict) and isinstance(value.get("contents"), str):
            value = value["contents"]
            continue
        if isinstance(value, str):
            try:
                value = json.loads(value)
                continue
            except json.JSONDecodeError:
                pass
        break
    return value


def run_direct(config: BenchmarkConfig, bootstrap: str, output: Path) -> dict[str, Any]:
    project = Path(config.host_project)
    source = project / BOOTSTRAP_PATH
    if source.exists():
        raise BenchmarkError(f"Refusing to overwrite existing bootstrap: {source}")
    source.parent.mkdir(parents=True, exist_ok=True)
    source.write_text(bootstrap, encoding="utf-8", newline="\n")
    try:
        command = direct_unity_command(config, output)
        process = subprocess.run(
            command,
            cwd=project,
            timeout=config.timeout_seconds,
            check=False,
        )
        if not output.is_file():
            raise BenchmarkError(
                f"Unity exited {process.returncode} without writing {output}"
            )
        result = json.loads(output.read_text(encoding="utf-8"))
        if process.returncode != 0 or result.get("status") != "completed":
            raise BenchmarkError(
                f"Direct Unity benchmark failed (exit {process.returncode}): {result}"
            )
        return result
    except (BenchmarkError, OSError, ValueError, subprocess.TimeoutExpired) as error:
        try:
            recover_direct_cleanup(config, output)
        except BenchmarkError as cleanup_error:
            raise BenchmarkError(
                f"Benchmark failed: {error}; recovery cleanup failed: {cleanup_error}"
            ) from error
        raise
    finally:
        source.unlink(missing_ok=True)
        source.with_suffix(source.suffix + ".meta").unlink(missing_ok=True)


def direct_unity_command(config: BenchmarkConfig, output: Path) -> list[str]:
    """Build the direct command without -quit, since Unity exits from Finish()."""

    return [
        config.unity_path,
        "-batchmode",
        "-projectPath",
        config.host_project,
        "-executeMethod",
        ENTRYPOINT + ".Run",
        "-logFile",
        str(output.with_suffix(".unity.log")),
    ]


def direct_cleanup_unity_command(config: BenchmarkConfig, output: Path) -> list[str]:
    """Build a bounded recovery command that runs in a fresh Unity process."""

    return [
        config.unity_path,
        "-batchmode",
        "-projectPath",
        config.host_project,
        "-executeMethod",
        ENTRYPOINT + ".Cleanup",
        "-quit",
        "-logFile",
        str(output.with_suffix(".cleanup.unity.log")),
    ]


def recover_direct_cleanup(config: BenchmarkConfig, output: Path) -> None:
    """Run recovery cleanup and prove that the disposable fixture is gone."""

    try:
        cleanup = subprocess.run(
            direct_cleanup_unity_command(config, output),
            cwd=config.host_project,
            timeout=min(config.timeout_seconds, 120.0),
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        raise BenchmarkError(f"Unity cleanup process failed: {error}") from error
    project = Path(config.host_project)
    fixture = project / "Assets" / "__DataVisualizerBenchmarkFixture"
    fixture_meta = project / "Assets" / "__DataVisualizerBenchmarkFixture.meta"
    failures = []
    if cleanup.returncode != 0:
        failures.append(f"Unity cleanup exited {cleanup.returncode}")
    if fixture.exists() or fixture_meta.exists():
        failures.append(f"fixture remains at {fixture}")
    if failures:
        raise BenchmarkError("; ".join(failures))


def run_mcp(config: BenchmarkConfig, bootstrap: str, output: Path) -> dict[str, Any]:
    request_timeout = min(config.timeout_seconds, 180.0)

    def connect(timeout: float = request_timeout) -> McpClient:
        connected = McpClient(config.mcp_url, config.mcp_token, timeout)
        connected.request(
            "initialize",
            {
                "protocolVersion": "2025-06-18",
                "capabilities": {},
                "clientInfo": {"name": "data-visualizer-benchmark", "version": "1"},
            },
        )
        connected.notify("notifications/initialized")
        return connected

    client = connect()
    remote_result_path = "Assets/__DataVisualizerBenchmarkResult.json"
    print("benchmark: connected to Unity MCP", file=sys.stderr, flush=True)
    deadline = time.monotonic() + config.timeout_seconds

    def call_with_reconnect(
        name: str, arguments: dict[str, Any], deadline: float
    ) -> Any:
        nonlocal client
        last_error: BenchmarkError | None = None
        while time.monotonic() < deadline:
            try:
                return client.call_tool(name, arguments)
            except BenchmarkError as error:
                last_error = error
                time.sleep(1.0)
                try:
                    client = connect()
                except BenchmarkError as reconnect_error:
                    last_error = reconnect_error
        raise last_error or BenchmarkError(f"MCP call {name} timed out")

    try:
        client.call_tool(
            "write_text_file",
            {"path": BOOTSTRAP_PATH, "contents": bootstrap, "confirm": False},
        )
        print("benchmark: bootstrap written", file=sys.stderr, flush=True)
        call_with_reconnect("recompile", {"focus": False}, deadline)
        print("benchmark: recompile requested", file=sys.stderr, flush=True)
        stable_recompile_statuses = 0
        while True:
            try:
                status = extract_unity_result(client.call_tool("recompile_status", {}))
            except BenchmarkError:
                stable_recompile_statuses = 0
                try:
                    client = connect(5.0)
                except BenchmarkError:
                    pass
                if time.monotonic() >= deadline:
                    raise
                time.sleep(1.0)
                continue
            status_name = status if isinstance(status, str) else status.get("status")
            if status_name in {"completed", "up_to_date"}:
                stable_recompile_statuses += 1
                if stable_recompile_statuses >= 2:
                    break
            else:
                stable_recompile_statuses = 0
            if time.monotonic() >= deadline:
                raise BenchmarkError(f"Unity recompile did not complete: {status}")
            time.sleep(0.5)
        print("benchmark: recompile completed", file=sys.stderr, flush=True)

        client.call_tool(
            "eval",
            {
                "code": f"return {ENTRYPOINT}.Run();",
                "timeout": 10_000,
            },
        )
        print("benchmark: Unity benchmark started", file=sys.stderr, flush=True)
        while True:
            try:
                value = extract_unity_result(
                    client.call_tool(
                        "read_text_file",
                        {"path": remote_result_path, "max_bytes": 1_048_576},
                    )
                )
            except BenchmarkError:
                value = extract_unity_result(
                    call_with_reconnect(
                        "eval",
                        {
                            "code": f"return {ENTRYPOINT}.Status();",
                            "timeout": 10_000,
                        },
                        deadline,
                    )
                )
            if isinstance(value, dict) and value.get("status") in {"completed", "failed"}:
                if value.get("status") != "completed":
                    raise BenchmarkError(f"MCP Unity benchmark failed: {value}")
                write_json(output, value)
                print("benchmark: report received", file=sys.stderr, flush=True)
                return value
            if time.monotonic() >= deadline:
                raise BenchmarkError(f"MCP Unity benchmark timed out: {value}")
            time.sleep(0.5)
    finally:
        cleanup_errors: list[str] = []
        cleanup_client = McpClient(config.mcp_url, config.mcp_token, 5.0)
        cleanup_deadline = time.monotonic() + 30.0
        try:
            cleanup_client = connect(5.0)
            cleanup_result = call_with_reconnect(
                "eval",
                {
                    "code": f"return {ENTRYPOINT}.Cleanup();",
                    "timeout": 10_000,
                },
                cleanup_deadline,
            )
            cleanup_client = client
            if isinstance(cleanup_result, str) and cleanup_result.startswith("cleanup-failed"):
                raise BenchmarkError(cleanup_result)
        except BenchmarkError as error:
            cleanup_errors.append(f"fixture: {error}")
        try:
            cleanup_client.call_tool(
                "delete_asset", {"asset": BOOTSTRAP_PATH, "confirm": True}
            )
        except BenchmarkError as error:
            cleanup_errors.append(f"bootstrap: {error}")
        try:
            cleanup_client.call_tool(
                "delete_asset", {"asset": remote_result_path, "confirm": True}
            )
        except BenchmarkError:
            try:
                cleanup_client.call_tool(
                    "eval",
                    {
                        "code": (
                            'string path = UnityEngine.Application.dataPath + "/__DataVisualizerBenchmarkResult.json"; '
                            "bool existed = System.IO.File.Exists(path); "
                            "if (existed) System.IO.File.Delete(path); "
                            "UnityEditor.AssetDatabase.Refresh(); "
                            'return existed ? "result-file-deleted" : "result-file-absent";'
                        ),
                        "timeout": 5_000,
                    },
                )
            except BenchmarkError as error:
                cleanup_errors.append(f"result: {error}")
        if cleanup_errors:
            raise BenchmarkError("MCP cleanup failed: " + "; ".join(cleanup_errors))


def run(config: BenchmarkConfig, root: Path) -> dict[str, Any]:
    output = report_path(config)
    output.parent.mkdir(parents=True, exist_ok=True)
    result_path = (
        str(output)
        if config.mode == "direct"
        else "Assets/__DataVisualizerBenchmarkResult.json"
    )
    bootstrap = render_bootstrap(config, result_path)
    if config.mode == "direct":
        result = run_direct(config, bootstrap, output)
    else:
        result = run_mcp(config, bootstrap, output)
    validate_result(config, result)
    write_comparison_report(comparison_report_path(config), config, result)
    return result


def main(argv: Iterable[str] | None = None) -> int:
    parser = build_argument_parser()
    args = parser.parse_args(list(argv) if argv is not None else None)
    root = Path(__file__).resolve().parents[2]
    try:
        config = config_from_args(args, root)
        plan = planned_execution(config)
        if args.dry_run:
            print(json.dumps(plan, indent=2, sort_keys=True))
            return 0
        result = run(config, root)
        print(json.dumps(result, indent=2, sort_keys=True))
        return 0
    except BenchmarkError as error:
        print(f"benchmark error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
