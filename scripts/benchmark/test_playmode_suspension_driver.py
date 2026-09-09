import inspect
import unittest

from playmode_suspension_driver import (
    BenchmarkError,
    RELOAD_CONFIGURATIONS,
    SuspensionConfig,
    UnityScenario,
    parse_args,
    asset_mutation_code,
    prepare_asset_mutation_code,
    restore_reload_code,
    run_asset_mutation_cycle,
    start_loads_and_play_code,
    snapshot_code,
)


class PlayModeSuspensionDriverTests(unittest.TestCase):
    def test_all_reload_configurations_are_distinct(self):
        self.assertEqual(len(RELOAD_CONFIGURATIONS), 4)
        self.assertEqual(
            {(item.domain_reload, item.scene_reload) for item in RELOAD_CONFIGURATIONS},
            {(True, True), (True, False), (False, True), (False, False)},
        )

    def test_fixture_size_rejects_too_small_inflight_fixture(self):
        with self.assertRaises(SystemExit):
            parse_args(
                [
                    "--host-project",
                    "/host/DataVisualizer",
                    "--unity-version",
                    "6000.4.6f1",
                    "--output",
                    "/tmp/result.json",
                    "--fixture-size",
                    "200",
                ]
            )

    def test_snapshot_uses_explicit_lifecycle_fields(self):
        source = snapshot_code()
        for field in (
            "_isPlayModeSuspended",
            "_isLoadingObjectsAsync",
            "_isLoadingSearchCacheAsync",
            "_pendingObjectGuids",
            "_pendingSearchCacheGuids",
            "_refreshQueuedDuringPlayMode",
            "_deferredInitializationPending",
        ):
            self.assertIn(field, source)
        self.assertIn("indicator=", source)
        self.assertIn("settingsPath=", source)

    def test_load_and_search_requests_capture_work_before_editor_ticks(self):
        source = start_loads_and_play_code()
        self.assertIn('"requested|loading=" + loading', source)
        self.assertIn('"|searchLoading=" + searchLoading', source)
        self.assertIn('"|pending=" + pending', source)
        self.assertIn('"|searchPending=" + searchPending', source)
        self.assertIn('"_scriptableObjectTypes"', source)
        self.assertLess(source.index("objectMethod.Invoke"), source.index("EditorApplication.isPlaying = true"))

    def test_asset_mutations_use_real_asset_database_operations(self):
        for operation, expected in (
            ("import", "ImportAsset"),
            ("move", "MoveAsset"),
            ("delete", "DeleteAsset"),
        ):
            source = asset_mutation_code(operation)
            self.assertIn(expected, source)
            self.assertIn("return", source)
        self.assertIn("CreateFolder", prepare_asset_mutation_code())

    def test_asset_mutation_cycle_requires_queued_invalidation_and_drain(self):
        source = inspect.getsource(run_asset_mutation_cycle)
        self.assertIn("refreshQueued=True", source)
        self.assertIn("searchPending=0", source)

    def test_scenario_bootstraps_canonical_settings_before_fixture(self):
        source = inspect.getsource(__import__("playmode_suspension_driver"))
        self.assertIn("canonical host settings bootstrap", source)
        self.assertIn("settingsPath=Assets/Editor/DataVisualizerSettings.asset", source)

    def test_wait_for_retries_transient_mcp_failures_until_deadline(self):
        scenario = UnityScenario.__new__(UnityScenario)
        scenario.config = SuspensionConfig(
            host_project="/host/DataVisualizer",
            unity_version="6000.4.6f1",
            output=__import__("pathlib").Path("/tmp/result.json"),
            mcp_url="http://127.0.0.1:1/mcp",
            mcp_token=None,
            timeout_seconds=0.01,
            fixture_size=201,
        )

        def fail(*_args, **_kwargs):
            raise BenchmarkError("transient MCP failure")

        scenario.eval = fail
        with self.assertRaisesRegex(BenchmarkError, "transient MCP failure"):
            scenario.wait_for("return true;", lambda value: value is True, "test state")

    def test_restore_reload_code_handles_combined_flags(self):
        source = restore_reload_code("True|DisableDomainReload, DisableSceneReload")
        self.assertIn("enterPlayModeOptionsEnabled = true", source)
        self.assertIn("DisableDomainReload | UnityEditor.EnterPlayModeOptions.DisableSceneReload", source)

    def test_restore_reload_code_handles_none(self):
        source = restore_reload_code("False|None")
        self.assertIn("enterPlayModeOptionsEnabled = false", source)
        self.assertIn("EnterPlayModeOptions.None", source)


if __name__ == "__main__":
    unittest.main()
