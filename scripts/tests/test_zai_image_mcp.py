#!/usr/bin/env python3
"""zai-image MCP server contract tests (typed Python port of the .mjs server, issue #171).

The node server ``zai-image-mcp.mjs`` became the typed Python
``zai_image_mcp.py``; these cases pin the stdio JSON-RPC contract the
generated MCP configs depend on, exactly as the frontends drive it: the
server spawns through ``uv run --no-project`` with cwd = workspace root.
The image API is a stdlib in-process HTTP stub (kernel-assigned port), so
every case is deterministic and no real credential or network is touched.
"""

from __future__ import annotations

import base64
import http.server
import json
import os
import shutil
import subprocess
import sys
import threading
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple, cast

sys.dont_write_bytecode = True

from _test_helpers import assert_true, new_temp_root, remove_temp_root, run_cases

TESTS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TESTS_DIR.parent.parent
SERVER = REPO_ROOT / ".llm" / "mcp" / "zai_image_mcp.py"

# A real 1x1 PNG (the well-known transparent pixel); the stub serves these
# bytes and the cases assert them verbatim. The contract is byte pass-through:
# the server never inspects the image, only decodes, saves, and inlines it.
PNG_BYTES = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")
PNG_B64 = base64.b64encode(PNG_BYTES).decode("ascii")

# Placeholder, obviously not a credential: env wins over .env.local, so the
# developer's real workspace key can never leak into a stub call.
STUB_KEY = "zai-stub-placeholder-key"

# The credential and endpoint vars are read from process.env FIRST, so the
# sandbox must not inherit the developer's real values.
_SCRUBBED_KEYS = (
    "ZAI_API_KEY",
    "Z_AI_API_KEY",
    "ZAI_IMAGE_ENDPOINT",
    "ZAI_IMAGE_OUTPUT_DIR",
    "ZAI_IMAGE_MODEL",
    "ZAI_IMAGE_SIZE",
)


def assert_contains(description: str, needle: str, haystack: str) -> None:
    assert_true(needle in haystack, f"{description} (missing: [{needle}])")


def require_dict(value: Any, description: str) -> Dict[str, Any]:
    assert_true(isinstance(value, dict), description)
    return cast(Dict[str, Any], value)


def require_list(value: Any, description: str) -> List[Any]:
    assert_true(isinstance(value, list), description)
    return cast(List[Any], value)


def require_text(value: Any, description: str) -> str:
    assert_true(isinstance(value, str), description)
    return value


class ImageStub:
    """In-process image API: POST returns a JSON payload, GET serves the PNG.

    ``payload_mode`` selects the response shape the API may produce:
    ``b64`` returns {"data": [{"b64_json": ...}]}, ``url`` returns
    {"data": [{"url": ...}]} pointing at this same stub's GET handler, and
    ``empty`` returns an empty data list to drive the failure verdicts.
    """

    def __init__(self, payload_mode: str) -> None:
        self.payload_mode = payload_mode
        stub = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_POST(self) -> None:  # noqa: N802 - http.server's name
                self.rfile.read(int(self.headers.get("Content-Length", "0")))
                item: Optional[Dict[str, Any]] = None
                if stub.payload_mode == "b64":
                    item = {"b64_json": PNG_B64}
                elif stub.payload_mode == "url":
                    item = {"url": "http://127.0.0.1:%d/generated.png" % stub.port}
                body = json.dumps({"data": [] if item is None else [item]}).encode("utf-8")
                self.send_response(200)
                self.send_header("content-type", "application/json")
                self.end_headers()
                self.wfile.write(body)

            def do_GET(self) -> None:  # noqa: N802 - http.server's name
                self.send_response(200)
                self.send_header("content-type", "image/png")
                self.end_headers()
                self.wfile.write(PNG_BYTES)

            def log_message(self, format: str, *args: object) -> None:  # noqa: A002 - http.server's name
                pass

        self._server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.port = self._server.server_address[1]
        self._thread = threading.Thread(target=self._server.serve_forever, daemon=True)
        self._thread.start()

    @property
    def endpoint(self) -> str:
        return "http://127.0.0.1:%d/api/paas/v4/images/generations" % self.port

    def stop(self) -> None:
        self._server.shutdown()
        self._server.server_close()


class Sandbox:
    """Hermetic spawn environment for one server case.

    The server is copied into the sandbox workspace, mirroring production:
    frontends spawn ``.llm/mcp/zai_image_mcp.py`` with cwd = workspace root,
    and the server resolves its workspace from its own location. The
    workspace .env.local lookup therefore lands in the sandbox, never in the
    developer's checkout.
    """

    def __init__(self) -> None:
        self.root = new_temp_root(prefix="zai-image-test-")
        self.home = self.root / "home"
        self.images = self.root / "images"
        self.home.mkdir()
        (self.root / ".llm" / "mcp").mkdir(parents=True)
        shutil.copyfile(SERVER, self.root / ".llm" / "mcp" / "zai_image_mcp.py")
        self.last_exit = -1
        self.responses: List[Dict[str, Any]] = []

    def spawn(self, lines: List[str], extra_env: Optional[Dict[str, str]] = None) -> None:
        """Run the server through the production launcher and collect responses."""
        env = dict(os.environ)
        for key in _SCRUBBED_KEYS:
            env.pop(key, None)
        env.update(
            {
                "ZAI_API_KEY": STUB_KEY,
                "ZAI_IMAGE_OUTPUT_DIR": str(self.images),
                "HOME": str(self.home),
            }
        )
        if os.name == "nt":
            # Windows resolves the home through USERPROFILE; keep both names
            # pointing inside the sandbox.
            env["USERPROFILE"] = str(self.home)
        if extra_env:
            env.update(extra_env)
        completed = subprocess.run(
            ["uv", "run", "--no-project", ".llm/mcp/zai_image_mcp.py"],
            cwd=str(self.root),
            env=env,
            input="\n".join(lines) + "\n",
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=120,
            check=False,
        )
        self.last_exit = completed.returncode
        self.responses = [json.loads(line) for line in (completed.stdout or "").splitlines() if line.strip()]
        if completed.returncode != 0:
            assert_true(False, "server exited %d: %s" % (completed.returncode, (completed.stderr or "")[-400:]))
        assert_true(not completed.stderr, "the server prints nothing on stderr: %s" % (completed.stderr or "")[-400:])

    def destroy(self) -> None:
        remove_temp_root(self.root)


def _request(identifier: int, method: str, params: Optional[Dict[str, Any]] = None) -> str:
    message: Dict[str, Any] = {"jsonrpc": "2.0", "id": identifier, "method": method}
    if params is not None:
        message["params"] = params
    return json.dumps(message)


def _response_payload(response: Dict[str, Any]) -> Tuple[bool, str]:
    """Return (isError, text) for a tools/call response object."""
    result = require_dict(response.get("result"), "tools/call response carries a result object")
    content = require_list(result.get("content"), "tools/call result carries content")
    assert_true(len(content) >= 1, "tools/call content is not empty")
    first = require_dict(content[0], "the text content part leads the response")
    assert_true(first.get("type") == "text", "the text content part leads the response")
    return (bool(result.get("isError")), require_text(first.get("text"), "the text part carries a message"))


def _error_code(response: Dict[str, Any]) -> Optional[int]:
    error = require_dict(response.get("error"), "the frame carries an error object")
    code = error.get("code")
    assert_true(isinstance(code, int), "the error object carries a numeric code")
    return code


def case_initialize_echoes_supported_version() -> None:
    sandbox = Sandbox()
    try:
        sandbox.spawn([_request(1, "initialize", {"protocolVersion": "2025-11-25"})])
        assert_true(len(sandbox.responses) == 1, "one initialize request produces exactly one response")
        first = require_dict(sandbox.responses[0], "the response is an object")
        assert_true(first.get("id") == 1, "the response echoes the request id")
        payload = require_dict(first.get("result"), "initialize carries a result object")
        assert_true(payload.get("protocolVersion") == "2025-11-25", "a supported version echoes verbatim")
        server_info = require_dict(payload.get("serverInfo"), "initialize carries serverInfo")
        assert_true(server_info.get("name") == "zai-image", "serverInfo names zai-image")

        sandbox.spawn([_request(2, "initialize", {"protocolVersion": "1999-01-01"})])
        payload = require_dict(require_dict(sandbox.responses[0], "the fallback response is an object").get("result"), "the fallback carries a result object")
        assert_true(payload.get("protocolVersion") == "2025-06-18", "an unknown version falls back to 2025-06-18")

        sandbox.spawn(['{"jsonrpc": "2.0", "method": "notifications/initialized"}'])
        assert_true(sandbox.responses == [], "a notification produces no response")
    finally:
        sandbox.destroy()


def case_tools_list_advertises_zai_generate_image() -> None:
    sandbox = Sandbox()
    try:
        sandbox.spawn([_request(1, "tools/list")])
        result = require_dict(require_dict(sandbox.responses[0], "the tools/list response is an object").get("result"), "tools/list carries a result object")
        tools = require_list(result.get("tools"), "tools/list carries a tools list")
        assert_true(len(tools) == 1, "exactly one tool is advertised")
        tool = require_dict(tools[0], "the advertised tool is an object")
        assert_true(tool.get("name") == "zai_generate_image", "the tool name is zai_generate_image")
        schema = require_dict(tool.get("inputSchema"), "the tool carries an input schema")
        assert_true(schema.get("required") == ["prompt"], "prompt is the only required argument")
        assert_true(schema.get("additionalProperties") is False, "the schema rejects unknown arguments")
    finally:
        sandbox.destroy()


def case_ping_answers_empty_result() -> None:
    sandbox = Sandbox()
    try:
        sandbox.spawn([_request(7, "ping")])
        result = require_dict(require_dict(sandbox.responses[0], "the ping response is an object").get("result"), "ping carries a result object")
        assert_true(result == {}, "ping answers an empty result")
    finally:
        sandbox.destroy()


def case_generate_saves_png_from_b64_payload() -> None:
    stub = ImageStub("b64")
    sandbox = Sandbox()
    try:
        sandbox.spawn(
            [_request(1, "tools/call", {"name": "zai_generate_image", "arguments": {"prompt": "Hello World!"}})],
            extra_env={"ZAI_IMAGE_ENDPOINT": stub.endpoint},
        )
        first = require_dict(sandbox.responses[0], "the tools/call response is an object")
        is_error, text = _response_payload(first)
        assert_true(not is_error, "a successful generation is not an error")
        assert_contains("the text names the prompt", 'Generated image for: "Hello World!"', text)
        saved_marker = text.split("Saved: ", 1)[1] if "Saved: " in text else ""
        assert_true(saved_marker and Path(saved_marker).is_file(), "the saved path points at a written file")
        assert_true(Path(saved_marker).read_bytes() == PNG_BYTES, "the saved file carries the decoded PNG bytes")
        content = require_list(require_dict(first.get("result"), "tools/call carries a result object").get("content"), "tools/call carries content parts")
        image_part = require_dict(content[1], "the inline image part follows the text part")
        assert_true(image_part.get("mimeType") == "image/png", "the inline part is a PNG")
        assert_true(
            base64.b64decode(require_text(image_part.get("data"), "the inline part carries base64 data")) == PNG_BYTES,
            "the inline part carries the same decoded PNG bytes",
        )
        files = list(sandbox.images.glob("*.png"))
        assert_true(len(files) == 1, "exactly one image file lands in the output dir")
        assert_true("hello-world" in files[0].name, "the filename carries the prompt slug")
    finally:
        stub.stop()
        sandbox.destroy()


def case_generate_downloads_url_payload() -> None:
    stub = ImageStub("url")
    sandbox = Sandbox()
    try:
        sandbox.spawn(
            [_request(1, "tools/call", {"name": "zai_generate_image", "arguments": {"prompt": "From url"}})],
            extra_env={"ZAI_IMAGE_ENDPOINT": stub.endpoint},
        )
        is_error, text = _response_payload(sandbox.responses[0])
        assert_true(not is_error, "a url payload generation is not an error")
        result = require_dict(sandbox.responses[0], "the tools/call response is an object")
        content = require_list(require_dict(result.get("result"), "tools/call carries a result object").get("content"), "tools/call carries content parts")
        image_part = require_dict(content[1], "the inline image part follows the text part")
        assert_true(image_part.get("mimeType") == "image/png", "the downloaded image part is a PNG")
        assert_true(
            base64.b64decode(require_text(image_part.get("data"), "the inline part carries base64 data")) == PNG_BYTES,
            "the inline part carries the bytes downloaded from the url",
        )
        assert_true("Saved: /" in text, "the generated image was also saved")
    finally:
        stub.stop()
        sandbox.destroy()


def case_api_failure_reports_is_error() -> None:
    stub = ImageStub("empty")
    sandbox = Sandbox()
    try:
        sandbox.spawn(
            [_request(1, "tools/call", {"name": "zai_generate_image", "arguments": {"prompt": "nope"}})],
            extra_env={"ZAI_IMAGE_ENDPOINT": stub.endpoint},
        )
        is_error, text = _response_payload(sandbox.responses[0])
        assert_true(is_error, "an empty API payload surfaces as an error result")
        assert_contains("the error names the contract gap", "returned no data", text)
    finally:
        stub.stop()
        sandbox.destroy()


def case_missing_key_reports_is_error() -> None:
    stub = ImageStub("b64")
    sandbox = Sandbox()
    try:
        sandbox.spawn(
            [_request(1, "tools/call", {"name": "zai_generate_image", "arguments": {"prompt": "no key"}})],
            extra_env={"ZAI_API_KEY": "", "ZAI_IMAGE_ENDPOINT": stub.endpoint},
        )
        is_error, text = _response_payload(sandbox.responses[0])
        assert_true(is_error, "a missing credential surfaces as an error result")
        assert_contains("the error points at .env.local", ".env.local", text)
        assert_true(STUB_KEY not in text, "the error never echoes the credential value")
    finally:
        stub.stop()
        sandbox.destroy()


def case_empty_prompt_reports_is_error() -> None:
    sandbox = Sandbox()
    try:
        sandbox.spawn([_request(1, "tools/call", {"name": "zai_generate_image", "arguments": {"prompt": "   "}})])
        is_error, text = _response_payload(sandbox.responses[0])
        assert_true(is_error, "a blank prompt surfaces as an error result")
        assert_true(text == "prompt is required.", "the blank-prompt message matches the node server")
    finally:
        sandbox.destroy()


def case_parse_error_and_unknown_method() -> None:
    sandbox = Sandbox()
    try:
        sandbox.spawn(["not json", _request(3, "nope"), '{"jsonrpc": "2.0", "id": 4, "method": "ping"}'])
        assert_true(len(sandbox.responses) == 3, "each bad frame still gets exactly one verdict line")
        parse_error = require_dict(sandbox.responses[0], "the parse-error response is an object")
        assert_true(parse_error.get("id") is None, "a parse error answers with id null")
        assert_true(_error_code(parse_error) == -32700, "a parse error is -32700")
        unknown = require_dict(sandbox.responses[1], "the unknown-method response is an object")
        assert_true(_error_code(unknown) == -32601, "an unknown method is -32601")
        assert_true(unknown.get("id") == 3, "the unknown-method error echoes the request id")
        after = require_dict(sandbox.responses[2], "the post-parse-error response is an object")
        assert_true(after.get("id") == 4, "frames after a parse error still process")
    finally:
        sandbox.destroy()


def case_unknown_tool_reports_is_error() -> None:
    sandbox = Sandbox()
    try:
        sandbox.spawn([_request(1, "tools/call", {"name": "bogus", "arguments": {}})])
        is_error, text = _response_payload(sandbox.responses[0])
        assert_true(is_error, "an unknown tool call surfaces as an error result")
        assert_true(text == "Unknown tool: bogus", "the unknown-tool message matches the node server")
    finally:
        sandbox.destroy()


CASES = (
    case_initialize_echoes_supported_version,
    case_tools_list_advertises_zai_generate_image,
    case_ping_answers_empty_result,
    case_generate_saves_png_from_b64_payload,
    case_generate_downloads_url_payload,
    case_api_failure_reports_is_error,
    case_missing_key_reports_is_error,
    case_empty_prompt_reports_is_error,
    case_parse_error_and_unknown_method,
    case_unknown_tool_reports_is_error,
)


def main() -> int:
    assert_true(SERVER.is_file(), f"server not found at {SERVER}")
    print("== zai-image MCP server contract tests ==")
    if shutil.which("uv") is None:
        print("    SKIP: uv not found on PATH; the generated configs spawn the server through uv.")
        return 0
    failures = run_cases([(case.__name__, case) for case in CASES])
    print(f"== zai-image MCP server: {failures} failure(s) ==")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
