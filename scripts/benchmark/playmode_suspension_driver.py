#!/usr/bin/env python3
"""Exercise Data Visualizer Play Mode suspension across reload configurations.

This is a disposable diagnostic scenario, not a package runtime entrypoint.  The
Python process owns the scenario state so Unity domain reload can be enabled; it
uses the official Unity MCP bridge only for short, explicit editor operations and
reflection-based observations.
"""

from __future__ import annotations

import argparse
import json
import os
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

from benchmark_driver import (
    BenchmarkError,
    McpClient,
    extract_unity_result,
    resolve_token,
)


FIXTURE_ROOT = "Assets/__CodexPlayModeSuspensionFixture"
WINDOW_TYPE = "WallstopStudios.DataVisualizer.Editor.DataVisualizer"
SETTINGS_TYPE = "WallstopStudios.DataVisualizer.Editor.Data.DataVisualizerSettings"
SCHEMA_VERSION = "1"


@dataclass(frozen=True)
class ReloadConfiguration:
    name: str
    domain_reload: bool
    scene_reload: bool


RELOAD_CONFIGURATIONS = (
    ReloadConfiguration("domain-and-scene-reload", True, True),
    ReloadConfiguration("domain-reload-only", True, False),
    ReloadConfiguration("scene-reload-only", False, True),
    ReloadConfiguration("no-domain-or-scene-reload", False, False),
)


@dataclass(frozen=True)
class SuspensionConfig:
    host_project: str
    unity_version: str
    output: Path
    mcp_url: str
    mcp_token: str | None
    timeout_seconds: float
    fixture_size: int


class UnityScenario:
    def __init__(self, config: SuspensionConfig) -> None:
        self.config = config
        self.client = self._connect(min(15.0, config.timeout_seconds))

    def _connect(self, timeout: float) -> McpClient:
        client = McpClient(self.config.mcp_url, self.config.mcp_token, timeout)
        client.request(
            "initialize",
            {
                "protocolVersion": "2025-06-18",
                "capabilities": {},
                "clientInfo": {"name": "data-visualizer-playmode-scenario", "version": "1"},
            },
        )
        client.notify("notifications/initialized")
        return client

    def call(
        self,
        name: str,
        arguments: dict[str, Any],
        deadline: float,
        request_timeout: float,
    ) -> Any:
        last_error: BenchmarkError | None = None
        while time.monotonic() < deadline:
            try:
                self.client.timeout = request_timeout
                return self.client.call_tool(name, arguments)
            except BenchmarkError as error:
                last_error = error
                try:
                    self.client = self._connect(min(15.0, self.config.timeout_seconds))
                except BenchmarkError as reconnect_error:
                    last_error = reconnect_error
                time.sleep(0.5)
        raise last_error or BenchmarkError(f"Unity MCP call {name} timed out")

    def eval(self, code: str, timeout_seconds: float | None = None) -> Any:
        operation_timeout = timeout_seconds or min(15.0, self.config.timeout_seconds)
        deadline = time.monotonic() + operation_timeout
        value = self.call(
            "eval",
            {"code": code, "timeout": int(operation_timeout * 1000)},
            deadline,
            request_timeout=operation_timeout,
        )
        return extract_unity_result(value)

    def wait_for(self, query: str, predicate: Callable[[Any], bool], label: str) -> Any:
        deadline = time.monotonic() + self.config.timeout_seconds
        last_value: Any = None
        while time.monotonic() < deadline:
            last_value = self.eval(query, timeout_seconds=min(10.0, self.config.timeout_seconds))
            if predicate(last_value):
                return last_value
            time.sleep(0.5)
        raise BenchmarkError(f"Timed out waiting for {label}: {last_value}")


def parse_args(argv: list[str] | None = None) -> SuspensionConfig:
    parser = argparse.ArgumentParser(
        description="Exercise Data Visualizer Play Mode suspension through Unity MCP."
    )
    parser.add_argument("--host-project", required=True, help="Unity host path, retained in the report.")
    parser.add_argument("--unity-version", required=True, help="Expected Unity editor version.")
    parser.add_argument("--output", required=True, type=Path, help="JSON report path outside the package.")
    parser.add_argument("--fixture-size", type=int, default=300, help="Temporary settings assets per scenario.")
    parser.add_argument(
        "--mcp-url",
        default=os.environ.get("UNITY_MCP_URL", "http://host.docker.internal:9020/mcp"),
    )
    parser.add_argument("--mcp-token", help="Bearer token; defaults to UNITY_MCP_TOKEN/.env.local.")
    parser.add_argument("--timeout-seconds", type=float, default=900.0)
    args = parser.parse_args(argv)
    if args.fixture_size < 201:
        parser.error("--fixture-size must be at least 201 so a load remains in flight")
    if args.timeout_seconds <= 0:
        parser.error("--timeout-seconds must be positive")
    return SuspensionConfig(
        host_project=args.host_project,
        unity_version=args.unity_version,
        output=args.output.expanduser().resolve(),
        mcp_url=args.mcp_url,
        mcp_token=resolve_token(args.mcp_token),
        timeout_seconds=args.timeout_seconds,
        fixture_size=args.fixture_size,
    )


def set_reload_configuration(scenario: UnityScenario, configuration: ReloadConfiguration) -> None:
    domain = "true" if configuration.domain_reload else "false"
    scene = "true" if configuration.scene_reload else "false"
    scenario.eval(
        "UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = true; "
        "UnityEditor.EditorSettings.enterPlayModeOptions = "
        f"({domain} ? UnityEditor.EnterPlayModeOptions.None : UnityEditor.EnterPlayModeOptions.DisableDomainReload) | "
        f"({scene} ? UnityEditor.EnterPlayModeOptions.None : UnityEditor.EnterPlayModeOptions.DisableSceneReload); "
        "return UnityEditor.EditorSettings.enterPlayModeOptions.ToString();"
    )


def fixture_folder_code() -> str:
    return f'''var root = "{FIXTURE_ROOT}";
if (UnityEditor.AssetDatabase.IsValidFolder(root))
{{
    UnityEditor.AssetDatabase.DeleteAsset(root);
    UnityEditor.AssetDatabase.Refresh();
}}
UnityEditor.AssetDatabase.CreateFolder("Assets", "__CodexPlayModeSuspensionFixture");
return "created";'''


def fixture_chunk_code(start: int, count: int) -> str:
    return f'''var root = "{FIXTURE_ROOT}";
for (int index = {start}; index < {start + count}; index++)
{{
    var asset = UnityEngine.ScriptableObject.CreateInstance<{SETTINGS_TYPE}>();
    UnityEditor.AssetDatabase.CreateAsset(asset, root + "/Item_" + index.ToString("D6") + ".asset");
    UnityEditor.AssetDatabase.SetLabels(
        asset, new[] {{ "playmode-suspension", "group-" + (index % 8).ToString("D2") }}
    );
}}
UnityEditor.AssetDatabase.SaveAssets();
return "chunk=" + {count};'''


def fixture_refresh_code() -> str:
    return f'''UnityEditor.AssetDatabase.Refresh();
return UnityEditor.AssetDatabase.FindAssets("", new[] {{ "{FIXTURE_ROOT}" }}).Length.ToString();'''


def cleanup_code() -> str:
    return f'''var root = "{FIXTURE_ROOT}";
bool existed = UnityEditor.AssetDatabase.IsValidFolder(root);
if (existed)
{{
    UnityEditor.AssetDatabase.DeleteAsset(root);
    UnityEditor.AssetDatabase.Refresh();
}}
return existed ? "deleted" : "absent";'''


def close_window_code() -> str:
    return f'''var windows = UnityEngine.Resources.FindObjectsOfTypeAll<{WINDOW_TYPE}>();
for (int index = 0; index < windows.Length; index++)
{{
    windows[index].Close();
}}
return windows.Length.ToString();'''


def open_window_code() -> str:
    return f'''var window = UnityEditor.EditorWindow.GetWindow<{WINDOW_TYPE}>("Data Visualizer");
window.Show();
return window != null ? "opened" : "missing";'''


def snapshot_code() -> str:
    return f'''var windows = UnityEngine.Resources.FindObjectsOfTypeAll<{WINDOW_TYPE}>();
var window = windows.Length == 0 ? null : windows[0];
if (window == null) return "window=0";
var type = window.GetType();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var suspended = (bool)type.GetField("_isPlayModeSuspended", flags).GetValue(window);
var loading = (bool)type.GetField("_isLoadingObjectsAsync", flags).GetValue(window);
var searchLoading = (bool)type.GetField("_isLoadingSearchCacheAsync", flags).GetValue(window);
var searchReady = (bool)type.GetField("_isSearchCachePopulated", flags).GetValue(window);
var refreshQueued = (bool)type.GetField("_refreshQueuedDuringPlayMode", flags).GetValue(window);
var deferred = (bool)type.GetField("_deferredInitializationPending", flags).GetValue(window);
var pending = ((System.Collections.ICollection)type.GetField("_pendingObjectGuids", flags).GetValue(window)).Count;
var indicator = type.GetField("_objectLoadingIndicator", flags).GetValue(window) as UnityEngine.UIElements.Label;
var search = type.GetField("_searchField", flags).GetValue(window) as UnityEngine.UIElements.VisualElement;
var create = type.GetField("_createObjectButton", flags).GetValue(window) as UnityEngine.UIElements.VisualElement;
return "window=1" +
    "|playing=" + UnityEditor.EditorApplication.isPlaying +
    "|suspended=" + suspended +
    "|loading=" + loading +
    "|searchLoading=" + searchLoading +
    "|searchReady=" + searchReady +
    "|refreshQueued=" + refreshQueued +
    "|deferred=" + deferred +
    "|pending=" + pending +
    "|indicator=" + (indicator == null ? "missing" : indicator.text) +
    "|searchEnabled=" + (search == null ? "missing" : search.enabledSelf) +
    "|createEnabled=" + (create == null ? "missing" : create.enabledSelf) +
    "|children=" + window.rootVisualElement.childCount;'''


def start_loads_and_play_code() -> str:
    return f'''var windows = UnityEngine.Resources.FindObjectsOfTypeAll<{WINDOW_TYPE}>();
var window = windows.Length == 0 ? null : windows[0];
if (window == null) return "window-missing";
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var objectMethod = typeof({WINDOW_TYPE}).GetMethod("LoadObjectTypesAsync", flags);
objectMethod.Invoke(window, new object[] {{ typeof({SETTINGS_TYPE}), false }});
var searchMethod = typeof({WINDOW_TYPE}).GetMethod("PopulateSearchCacheAsync", flags);
searchMethod.Invoke(window, null);
UnityEditor.EditorApplication.isPlaying = true;
var type = window.GetType();
var loading = (bool)type.GetField("_isLoadingObjectsAsync", flags).GetValue(window);
var searchLoading = (bool)type.GetField("_isLoadingSearchCacheAsync", flags).GetValue(window);
var pending = ((System.Collections.ICollection)type.GetField("_pendingObjectGuids", flags).GetValue(window)).Count;
return "requested|loading=" + loading + "|searchLoading=" + searchLoading + "|pending=" + pending;'''


def signal_refresh_code() -> str:
    return f'''{WINDOW_TYPE}.SignalRefresh();
return "signalled";'''


def restore_reload_code(original: Any) -> str:
    original_text = str(original)
    enabled, separator, options = original_text.partition("|")
    if not separator:
        raise BenchmarkError(f"Invalid reload settings returned by Unity: {original_text}")
    option_names = [item.strip() for item in options.split(",") if item.strip()]
    if not option_names or option_names == ["None"]:
        option_expression = "UnityEditor.EnterPlayModeOptions.None"
    else:
        allowed = {"DisableDomainReload", "DisableSceneReload"}
        if any(item not in allowed for item in option_names):
            raise BenchmarkError(f"Unsupported reload settings returned by Unity: {original_text}")
        option_expression = " | ".join(
            f"UnityEditor.EnterPlayModeOptions.{item}" for item in option_names
        )
    return (
        "UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = "
        + enabled.lower()
        + "; UnityEditor.EditorSettings.enterPlayModeOptions = "
        + option_expression
        + "; return UnityEditor.EditorSettings.enterPlayModeOptionsEnabled + \"|\" + "
        + "UnityEditor.EditorSettings.enterPlayModeOptions;"
    )


def wait_editor_state(scenario: UnityScenario, playing: bool) -> Any:
    return scenario.wait_for(
        "return UnityEditor.EditorApplication.isPlaying;",
        lambda value: value is playing or value == str(playing),
        "Play Mode" if playing else "Edit Mode",
    )


def run_open_cycle(scenario: UnityScenario, cycle: int) -> dict[str, Any]:
    started_at = time.monotonic()
    pre_play = scenario.eval(start_loads_and_play_code())
    if not (
        isinstance(pre_play, str)
        and "loading=True" in pre_play
        and "pending=0" not in pre_play
    ):
        raise BenchmarkError(f"Object load did not remain in flight: {pre_play}")
    wait_editor_state(scenario, True)
    playing = scenario.wait_for(
        snapshot_code(),
        lambda value: isinstance(value, str) and "suspended=True" in value,
        "suspended open window",
    )
    scenario.eval(signal_refresh_code())
    invalidated = scenario.wait_for(
        snapshot_code(),
        lambda value: isinstance(value, str) and "refreshQueued=True" in value,
        "queued invalidation",
    )
    scenario.eval("UnityEditor.EditorApplication.isPlaying = false; return \"requested\";")
    wait_editor_state(scenario, False)
    resumed = scenario.wait_for(
        snapshot_code(),
        lambda value: isinstance(value, str) and "suspended=False" in value,
        "resumed open window",
    )
    return {
        "cycle": cycle,
        "transitionSeconds": time.monotonic() - started_at,
        "prePlay": pre_play,
        "playing": playing,
        "invalidated": invalidated,
        "resumed": resumed,
    }


def run_first_enable_cycle(scenario: UnityScenario) -> dict[str, Any]:
    scenario.eval(close_window_code())
    started_at = time.monotonic()
    scenario.eval("UnityEditor.EditorApplication.isPlaying = true; return \"requested\";")
    wait_editor_state(scenario, True)
    scenario.eval(open_window_code())
    first_enable = scenario.wait_for(
        snapshot_code(),
        lambda value: isinstance(value, str) and "|playing=True" in value,
        "window first enabled during Play Mode",
    )
    scenario.eval("UnityEditor.EditorApplication.isPlaying = false; return \"requested\";")
    wait_editor_state(scenario, False)
    resumed = scenario.wait_for(
        snapshot_code(),
        lambda value: isinstance(value, str) and "suspended=False" in value,
        "first-enable resume",
    )
    return {
        "transitionSeconds": time.monotonic() - started_at,
        "firstEnableDuringPlay": first_enable,
        "resumed": resumed,
    }


def run(config: SuspensionConfig) -> dict[str, Any]:
    scenario = UnityScenario(config)
    original = scenario.eval(
        "return UnityEditor.EditorSettings.enterPlayModeOptionsEnabled + \"|\" + "
        "UnityEditor.EditorSettings.enterPlayModeOptions;"
    )
    report: dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "hostProject": config.host_project,
        "requestedUnityVersion": config.unity_version,
        "fixtureSize": config.fixture_size,
        "fixtureRoot": FIXTURE_ROOT,
        "originalReloadSettings": original,
        "configurations": [],
    }
    try:
        for configuration in RELOAD_CONFIGURATIONS:
            print(f"scenario: {configuration.name} setup", flush=True)
            set_reload_configuration(scenario, configuration)
            scenario.eval(fixture_folder_code(), timeout_seconds=30.0)
            chunk_size = 25
            for start in range(0, config.fixture_size, chunk_size):
                scenario.eval(
                    fixture_chunk_code(start, min(chunk_size, config.fixture_size - start)),
                    timeout_seconds=30.0,
                )
            scenario.eval(fixture_refresh_code(), timeout_seconds=60.0)
            scenario.wait_for(
                f'return UnityEditor.AssetDatabase.FindAssets("", new[] {{ "{FIXTURE_ROOT}" }}).Length;',
                lambda value: value == config.fixture_size or value == str(config.fixture_size),
                "fixture import",
            )
            scenario.eval(close_window_code())
            scenario.eval(open_window_code())
            scenario.wait_for(
                snapshot_code(),
                lambda value: isinstance(value, str) and "window=1" in value and "children=0" not in value,
                "Data Visualizer visual tree",
            )
            cycles = [run_open_cycle(scenario, cycle) for cycle in (1, 2)]
            first_enable = run_first_enable_cycle(scenario)
            scenario.eval(close_window_code())
            report["configurations"].append(
                {
                    "name": configuration.name,
                    "domainReload": configuration.domain_reload,
                    "sceneReload": configuration.scene_reload,
                    "cycles": cycles,
                    "firstEnable": first_enable,
                }
            )
            print(f"scenario: {configuration.name} complete", flush=True)
    finally:
        try:
            scenario.eval("UnityEditor.EditorApplication.isPlaying = false; return \"requested\";")
            wait_editor_state(scenario, False)
        except BenchmarkError:
            pass
        try:
            scenario.eval(close_window_code())
            scenario.eval(cleanup_code())
        finally:
            scenario.eval(restore_reload_code(original))
    restored = scenario.eval(
        "return UnityEditor.EditorSettings.enterPlayModeOptionsEnabled + \"|\" + "
        "UnityEditor.EditorSettings.enterPlayModeOptions;"
    )
    report["restoredReloadSettings"] = restored
    report["cleanupCompleted"] = scenario.eval(
        f"return UnityEditor.AssetDatabase.IsValidFolder(\"{FIXTURE_ROOT}\") ? \"leftover\" : \"clean\";"
    ) == "clean"
    report["status"] = "completed"
    config.output.parent.mkdir(parents=True, exist_ok=True)
    config.output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return report


def main(argv: list[str] | None = None) -> int:
    try:
        config = parse_args(argv)
        print(json.dumps(run(config), indent=2, sort_keys=True))
        return 0
    except (BenchmarkError, OSError, ValueError) as error:
        print(f"playmode suspension error: {error}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
