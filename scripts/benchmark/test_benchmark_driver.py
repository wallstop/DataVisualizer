import argparse
import json
import re
import tempfile
import unittest
from pathlib import Path

from benchmark_driver import (
    BenchmarkError,
    PathMapping,
    build_argument_parser,
    comparison_report_path,
    config_from_args,
    extract_unity_result,
    parse_mcp_tool_result,
    parse_fixture_size,
    planned_execution,
    render_bootstrap,
    write_comparison_report,
)


class BenchmarkDriverTests(unittest.TestCase):
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
            self.assertIn('private const string Suite = "fixture";', source)

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
            self.assertEqual(plan["comparisonOutput"], str(comparison_report_path(config)))
            json.dumps(plan)

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
                "playEntryOpen": {
                    "warmups": [1] * 5,
                    "samples": [25] * 30,
                    "medianMilliseconds": 25,
                    "p95Milliseconds": 25,
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
            self.assertIn("`indexed-search`", contents)

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
