import argparse
import json
import re
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from benchmark_matrix import build_argument_parser as build_matrix_argument_parser
from benchmark_matrix import (
    DEFAULT_TIMEOUT_SECONDS,
    display_command,
    driver_command,
    matrix_paths,
    parse_sizes,
    run_matrix,
)
from benchmark_driver import (
    BenchmarkError,
    PathMapping,
    build_argument_parser,
    comparison_report_path,
    config_from_args,
    direct_unity_command,
    direct_cleanup_unity_command,
    case_order_for_repetition,
    case_schedule,
    extract_unity_result,
    parse_mcp_tool_result,
    parse_fixture_size,
    planned_execution,
    render_bootstrap,
    validate_metric_evidence,
    validate_result,
    write_comparison_report,
)


class BenchmarkDriverTests(unittest.TestCase):
    def _all_suite_config(self, temporary):
        parser = build_argument_parser()
        root = Path(__file__).resolve().parents[2]
        arguments = parser.parse_args(
            [
                "--host-project",
                "/host/DataVisualizer",
                "--unity-version",
                "6000.4.6f1",
                "--fixture-size",
                "100",
                "--output-dir",
                temporary,
                "--mode",
                "mcp",
            ]
        )
        return config_from_args(arguments, root)

    def _measured_metric_result(self):
        cases = ("open-idle", "open-indexing", "closed")
        observations = []
        for repetition in range(35):
            for case_index, case in enumerate(case_order_for_repetition(repetition)):
                observations.append(
                    {
                        "repetition": repetition,
                        "caseName": case,
                        "logicalCaseIndex": cases.index(case),
                        "caseIndex": case_index,
                        "warmup": repetition < 5,
                        "elapsedMilliseconds": 10.0,
                    }
                )
        return {
            "schemaVersion": "1",
            "suite": "all",
            "playEntryMetrics": [
                {
                    "caseName": case,
                    "status": "measured",
                    "reason": "measured by Unity Stopwatch",
                    "warmupCount": 5,
                    "sampleCount": 30,
                    "warmups": [10.0] * 5,
                    "samples": [10.0] * 30,
                    "medianMilliseconds": 10.0,
                    "p95Milliseconds": 10.0,
                    "targetStatus": "pass",
                }
                for case in cases
            ],
            "playEntryObservations": observations,
        }

    def test_case_order_and_schedule_are_deterministic(self):
        self.assertEqual(case_order_for_repetition(0), ["open-idle", "open-indexing", "closed"])
        self.assertEqual(case_order_for_repetition(1), ["closed", "open-indexing", "open-idle"])
        schedule = case_schedule()
        self.assertEqual(len(schedule), 35)
        self.assertEqual(schedule[0]["cases"], case_order_for_repetition(0))
        self.assertEqual(schedule[-1]["cases"], case_order_for_repetition(34))

    def test_validate_metric_evidence_requires_labeled_observations(self):
        with tempfile.TemporaryDirectory() as temporary:
            config = self._all_suite_config(temporary)
            result = self._measured_metric_result()
            validate_metric_evidence(config, result)
            result["playEntryObservations"][0].pop("caseName")
            with self.assertRaises(BenchmarkError):
                validate_metric_evidence(config, result)

    def test_validate_metric_evidence_rejects_missing_metrics(self):
        with tempfile.TemporaryDirectory() as temporary:
            config = self._all_suite_config(temporary)
            with self.assertRaises(BenchmarkError):
                validate_metric_evidence(config, {"suite": "all"})

    def test_validate_metric_evidence_rejects_wrong_case_index(self):
        with tempfile.TemporaryDirectory() as temporary:
            config = self._all_suite_config(temporary)
            result = self._measured_metric_result()
            result["playEntryObservations"][0]["caseIndex"] = 2
            with self.assertRaises(BenchmarkError):
                validate_metric_evidence(config, result)

    def test_validate_metric_evidence_rejects_wrong_warmup_label(self):
        with tempfile.TemporaryDirectory() as temporary:
            config = self._all_suite_config(temporary)
            result = self._measured_metric_result()
            result["playEntryObservations"][0]["warmup"] = False
            with self.assertRaises(BenchmarkError):
                validate_metric_evidence(config, result)

    def test_validate_metric_evidence_rejects_inconsistent_target_status(self):
        with tempfile.TemporaryDirectory() as temporary:
            config = self._all_suite_config(temporary)
            result = self._measured_metric_result()
            result["playEntryMetrics"][0]["targetStatus"] = "miss"
            with self.assertRaises(BenchmarkError):
                validate_metric_evidence(config, result)

    def test_matrix_parser_preserves_requested_sizes_and_versions(self):
        parser = build_matrix_argument_parser()
        arguments = parser.parse_args(
            [
                "--host-project",
                "/host/DataVisualizer",
                "--unity-version",
                "6000.4.6f1",
                "--unity-version",
                "2022.3.50f1",
                "--sizes",
                "100,50000",
                "--output-dir",
                "/tmp/benchmark",
            ]
        )
        self.assertEqual(arguments.unity_version, ["6000.4.6f1", "2022.3.50f1"])
        self.assertEqual(arguments.sizes, (100, 50_000))
        self.assertEqual(arguments.timeout_seconds, DEFAULT_TIMEOUT_SECONDS)
        command = driver_command(arguments, arguments.unity_version[0], 50_000)
        self.assertIn("benchmark_driver.py", command[1])
        self.assertIn("--fixture-size", command)
        self.assertIn("50000", command)

    def test_matrix_display_command_redacts_mcp_token(self):
        displayed = display_command(["python", "--mcp-token", "secret", "--mode", "mcp"])
        self.assertEqual(displayed, ["python", "--mcp-token", "REDACTED", "--mode", "mcp"])

    def test_matrix_persists_partial_failure_evidence(self):
        parser = build_matrix_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            arguments = parser.parse_args(
                [
                    "--host-project",
                    "/host/DataVisualizer",
                    "--unity-version",
                    "6000.4.6f1",
                    "--sizes",
                    "100",
                    "--output-dir",
                    temporary,
                ]
            )
            with patch(
                "benchmark_matrix.subprocess.run",
                return_value=subprocess.CompletedProcess([], 1),
            ):
                with self.assertRaises(BenchmarkError):
                    run_matrix(arguments)
            matrix_path, _ = matrix_paths(Path(temporary).resolve(), "fixture")
            matrix = json.loads(matrix_path.read_text(encoding="utf-8"))
            self.assertEqual(matrix["status"], "failed")
            self.assertEqual(matrix["runs"][0]["status"], "failed")
            self.assertEqual(
                [lane["status"] for lane in matrix["controlLanes"]],
                ["planned", "not-run", "not-run", "not-run"],
            )
            self.assertEqual(
                [item["unityLine"] for item in matrix["compatibility"]],
                ["2021.3", "2022.3", "6000.4.6f1"],
            )

    def test_matrix_parser_rejects_duplicate_sizes(self):
        with self.assertRaises(argparse.ArgumentTypeError):
            parse_sizes("100,100")

    def test_validate_result_requires_fixture_and_cleanup_evidence(self):
        parser = build_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(__file__).resolve().parents[2]
            arguments = parser.parse_args(
                [
                    "--host-project",
                    "/host/DataVisualizer",
                    "--unity-version",
                    "6000.4.6f1",
                    "--fixture-size",
                    "100",
                    "--output-dir",
                    temporary,
                    "--mode",
                    "mcp",
                ]
            )
            config = config_from_args(arguments, root)
            result = {
                "schemaVersion": "1",
                "status": "completed",
                "unityVersion": "6000.4.6f1",
                "fixtureSize": 100,
                "fixtureVerified": False,
                "fixtureAssetCount": 100,
                "cleanupCompleted": True,
            }
            with self.assertRaises(BenchmarkError):
                validate_result(config, result)
            result["fixtureVerified"] = True
            result["cleanupCompleted"] = False
            with self.assertRaises(BenchmarkError):
                validate_result(config, result)

    def test_validate_result_allows_kept_fixture_without_cleanup(self):
        parser = build_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(__file__).resolve().parents[2]
            arguments = parser.parse_args(
                [
                    "--host-project",
                    "/host/DataVisualizer",
                    "--unity-version",
                    "6000.4.6f1",
                    "--fixture-size",
                    "100",
                    "--output-dir",
                    temporary,
                    "--mode",
                    "mcp",
                    "--suite",
                    "fixture",
                    "--keep-fixture",
                ]
            )
            result = {
                "schemaVersion": "1",
                "status": "completed",
                "unityVersion": "6000.4.6f1",
                "fixtureSize": 100,
                    "fixtureVerified": True,
                    "fixtureAssetCount": 100,
                    "cleanupCompleted": False,
                    "playEntryMetrics": [
                        {
                            "caseName": case,
                            "status": "unavailable",
                            "reason": "fixture suite does not measure Play-entry latency",
                        }
                        for case in ("open-idle", "open-indexing", "closed")
                    ],
                }
            self.assertEqual(validate_result(config_from_args(arguments, root), result), result)

    def test_parse_fixture_size_accepts_supported_values(self):
        self.assertEqual(parse_fixture_size("10,000"), 10_000)

    def test_parse_fixture_size_rejects_unsupported_values(self):
        with self.assertRaises(argparse.ArgumentTypeError):
            parse_fixture_size("500")

    def test_path_mapping_translates_nested_path(self):
        with tempfile.TemporaryDirectory() as temporary:
            local_root = Path(temporary).resolve()
            value = local_root / "results" / "run.json"
            mapping = PathMapping(local_root, "/Users/wallstop/bench")
            self.assertEqual(
                mapping.translate(value), "/Users/wallstop/bench/results/run.json"
            )

    def test_path_mapping_rejects_path_outside_prefix(self):
        with tempfile.TemporaryDirectory() as temporary:
            mapping = PathMapping(Path(temporary).resolve(), "/host")
            with self.assertRaises(BenchmarkError):
                mapping.translate(Path(temporary).resolve().parent / "run.json")

    def test_render_bootstrap_has_no_unresolved_tokens(self):
        parser = build_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(__file__).resolve().parents[2]
            arguments = parser.parse_args(
                [
                    "--host-project",
                    "/host/DataVisualizer",
                    "--unity-version",
                    "6000.4.6f1",
                    "--fixture-size",
                    "100",
                    "--suite",
                    "fixture",
                    "--output-dir",
                    temporary,
                    "--mode",
                    "mcp",
                ]
            )
            config = config_from_args(arguments, root)
            source = render_bootstrap(config, "")
            self.assertIsNone(re.search(r"__[A-Z_]+__", source))
            self.assertIn("private const int FixtureSize = 100;", source)
            self.assertIn("private const int FixtureBatchSize = 500;", source)
            self.assertIn('private const string Suite = "fixture";', source)

    def test_large_fixture_bootstrap_uses_cooperative_batches(self):
        parser = build_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(__file__).resolve().parents[2]
            arguments = parser.parse_args(
                [
                    "--host-project",
                    "/host/DataVisualizer",
                    "--unity-version",
                    "6000.4.6f1",
                    "--fixture-size",
                    "50,000",
                    "--suite",
                    "fixture",
                    "--output-dir",
                    temporary,
                    "--mode",
                    "mcp",
                ]
            )
            source = render_bootstrap(config_from_args(arguments, root), "")
            self.assertIn("private const int FixtureSize = 50000;", source)
            self.assertIn("private static void CreateFixtureBatch()", source)
            self.assertIn("AssetDatabase.Refresh();", source)
            self.assertIn("SharedReference", source)
            self.assertIn("Shared fixture asset could not be reloaded", source)
            self.assertIn("logicalCaseIndex", source)
            self.assertIn("expectedType", source)
            self.assertIn("_phase = Phase.VerifyFixture;", source)

    def test_planned_execution_records_measurement_boundary(self):
        parser = build_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(__file__).resolve().parents[2]
            arguments = parser.parse_args(
                [
                    "--host-project",
                    "/host/DataVisualizer",
                    "--unity-version",
                    "6000.4.6f1",
                    "--fixture-size",
                    "1000",
                    "--output-dir",
                    temporary,
                    "--mode",
                    "mcp",
                    "--path-map",
                    f"{temporary}=/host/results",
                ]
            )
            config = config_from_args(arguments, root)
            plan = planned_execution(config)
            self.assertEqual(plan["samples"], 30)
            self.assertEqual(plan["warmups"], 5)
            self.assertIn("Unity Stopwatch", plan["timingBoundary"])
            self.assertEqual(plan["playEntryCases"], ["open-idle", "open-indexing", "closed"])
            self.assertEqual(
                plan["playEntryCaseOrder"][1], ["closed", "open-indexing", "open-idle"]
            )
            self.assertEqual(
                plan["hostOutput"], f"/host/results/data-visualizer-all-1000-6000.4.6f1.json"
            )
            self.assertEqual(plan["comparisonOutput"], str(comparison_report_path(config)))
            json.dumps(plan)

    def test_bootstrap_refuses_unowned_fixture_and_alternates_cases(self):
        parser = build_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(__file__).resolve().parents[2]
            arguments = parser.parse_args(
                [
                    "--host-project",
                    "/host/DataVisualizer",
                    "--unity-version",
                    "6000.4.6f1",
                    "--fixture-size",
                    "100",
                    "--output-dir",
                    temporary,
                    "--mode",
                    "mcp",
                ]
            )
            source = render_bootstrap(config_from_args(arguments, root), "")
            self.assertIn("refusing to delete an unowned benchmark fixture", source)
            self.assertIn("return _repetition % 2 == 0 ? _caseIndex : 2 - _caseIndex;", source)
            self.assertIn("originalEnterPlayModeOptionsEnabled", source)
            self.assertIn("Unity refused to delete the benchmark fixture", source)

    def test_direct_cleanup_command_is_bounded_and_uses_cleanup_entrypoint(self):
        parser = build_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(__file__).resolve().parents[2]
            arguments = parser.parse_args(
                [
                    "--host-project",
                    temporary,
                    "--unity-version",
                    "6000.4.6f1",
                    "--fixture-size",
                    "100",
                    "--output-dir",
                    temporary,
                    "--mode",
                    "direct",
                ]
            )
            command = direct_cleanup_unity_command(
                config_from_args(arguments, root), Path(temporary) / "result.json"
            )
            self.assertIn(".Cleanup", command[command.index("-executeMethod") + 1])
            self.assertIn("-quit", command)
            self.assertEqual(command[command.index("-projectPath") + 1], str(Path(temporary).resolve()))

    def test_comparison_report_preserves_target_miss_and_raw_sample_counts(self):
        parser = build_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(__file__).resolve().parents[2]
            arguments = parser.parse_args(
                [
                    "--host-project",
                    "/host/DataVisualizer",
                    "--unity-version",
                    "6000.4.6f1",
                    "--fixture-size",
                    "100",
                    "--suite",
                    "play-entry",
                    "--output-dir",
                    temporary,
                    "--mode",
                    "mcp",
                ]
            )
            config = config_from_args(arguments, root)
            result = {
                "status": "completed",
                "suite": "play-entry",
                "fixtureSize": 100,
                "packageRevision": "abc",
                "packageVersion": "1.0.0",
                "unityVersion": "6000.4.6f1",
                "operatingSystem": "test",
                "cpu": "test",
                "memoryMegabytes": 1,
                "playEntryTargetMilliseconds": 20,
                "playEntryTargetMet": False,
                "playEntryOpenIdle": {
                    "warmups": [1] * 5,
                    "samples": [25] * 30,
                    "medianMilliseconds": 25,
                    "p95Milliseconds": 25,
                },
                "playEntryOpenIndexing": {
                    "warmups": [3] * 5,
                    "samples": [23] * 30,
                    "medianMilliseconds": 23,
                    "p95Milliseconds": 23,
                },
                "playEntryClosed": {
                    "warmups": [2] * 5,
                    "samples": [21] * 30,
                    "medianMilliseconds": 21,
                    "p95Milliseconds": 21,
                },
                "unavailableMetrics": ["indexed-search"],
            }
            report = comparison_report_path(config)
            write_comparison_report(report, config, result)
            contents = report.read_text(encoding="utf-8")
            self.assertIn("MISS", contents)
            self.assertIn("`30` | 25.000 ms | 25.000 ms", contents)
            self.assertIn("Window open / indexing", contents)
            self.assertIn("`indexed-search`", contents)

    def test_comparison_report_marks_unavailable_metric_explicitly(self):
        parser = build_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(__file__).resolve().parents[2]
            arguments = parser.parse_args(
                [
                    "--host-project",
                    "/host/DataVisualizer",
                    "--unity-version",
                    "6000.4.6f1",
                    "--fixture-size",
                    "100",
                    "--suite",
                    "play-entry",
                    "--output-dir",
                    temporary,
                    "--mode",
                    "mcp",
                ]
            )
            config = config_from_args(arguments, root)
            result = {
                "status": "completed",
                "suite": "play-entry",
                "fixtureSize": 100,
                "playEntryMetrics": [
                    {
                        "caseName": "open-idle",
                        "status": "measured",
                        "targetStatus": "pass",
                        "warmups": [1] * 5,
                        "samples": [1] * 30,
                        "medianMilliseconds": 1,
                        "p95Milliseconds": 1,
                    },
                    {
                        "caseName": "open-indexing",
                        "status": "unavailable",
                        "reason": "window unavailable",
                    },
                    {
                        "caseName": "closed",
                        "status": "failed",
                        "reason": "play request failed",
                    },
                ],
            }
            report = comparison_report_path(config)
            write_comparison_report(report, config, result)
            self.assertIn("FAILED", report.read_text(encoding="utf-8"))

    def test_comparison_report_uses_all_new_metric_evidence_rows(self):
        parser = build_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(__file__).resolve().parents[2]
            arguments = parser.parse_args(
                [
                    "--host-project",
                    "/host/DataVisualizer",
                    "--unity-version",
                    "6000.4.6f1",
                    "--fixture-size",
                    "100",
                    "--suite",
                    "all",
                    "--output-dir",
                    temporary,
                    "--mode",
                    "mcp",
                ]
            )
            config = config_from_args(arguments, root)
            result = self._measured_metric_result()
            result.update(
                {
                    "fixtureSize": 100,
                    "playEntryTargetMilliseconds": 20,
                    "playEntryTargetMet": True,
                }
            )
            report = comparison_report_path(config)
            write_comparison_report(report, config, result)
            contents = report.read_text(encoding="utf-8")
            self.assertEqual(contents.count("10.000 ms"), 6)
            self.assertIn("PASS", contents)

    def test_direct_command_waits_for_deferred_finish(self):
        parser = build_argument_parser()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(__file__).resolve().parents[2]
            arguments = parser.parse_args(
                [
                    "--host-project",
                    str(root),
                    "--unity-version",
                    "6000.4.6f1",
                    "--fixture-size",
                    "100",
                    "--output-dir",
                    temporary,
                    "--mode",
                    "direct",
                ]
            )
            config = config_from_args(arguments, root)
            command = direct_unity_command(config, Path(temporary) / "result.json")
            self.assertNotIn("-quit", command)
            self.assertIn("-executeMethod", command)

    def test_mcp_error_result_is_not_treated_as_running_status(self):
        with self.assertRaises(BenchmarkError):
            parse_mcp_tool_result(
                {
                    "isError": True,
                    "content": [{"type": "text", "text": "missing file"}],
                }
            )

    def test_extract_unity_result_unwraps_read_file_contents(self):
        value = extract_unity_result(
            {
                "assetPath": "Assets/result.json",
                "contents": '{"status":"completed"}',
            }
        )
        self.assertEqual(value, {"status": "completed"})


if __name__ == "__main__":
    unittest.main()
