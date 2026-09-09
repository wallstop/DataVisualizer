import unittest

from playmode_suspension_driver import (
    RELOAD_CONFIGURATIONS,
    parse_args,
    restore_reload_code,
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
            "_pendingObjectGuids",
            "_refreshQueuedDuringPlayMode",
            "_deferredInitializationPending",
        ):
            self.assertIn(field, source)
        self.assertIn("indicator=", source)

    def test_load_and_search_requests_capture_work_before_editor_ticks(self):
        source = start_loads_and_play_code()
        self.assertIn('"requested|loading=" + loading', source)
        self.assertIn('"|searchLoading=" + searchLoading', source)
        self.assertIn('"|pending=" + pending', source)
        self.assertLess(source.index("objectMethod.Invoke"), source.index("EditorApplication.isPlaying = true"))

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
