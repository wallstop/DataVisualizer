#!/usr/bin/env python3
"""Z.ai image-generation MCP server — zero-dependency typed Python (issue #171).

Wraps Z.ai's image generation API (POST /api/paas/v4/images/generations,
model glm-image) as an MCP stdio server so agent frontends can generate
images. Officially Z.ai ships a vision (image *understanding*) MCP server
(@z_ai/mcp-server); this complements it with image *generation*.

This is the typed-Python port of ``zai-image-mcp.mjs`` and keeps its exact
stdio JSON-RPC contract: the same initialize/tools handshake, the same
``zai_generate_image`` tool, and the same response shapes, so generated MCP
configs change only the spawn command. Frontends spawn this server through
``uv run --no-project .llm/mcp/zai_image_mcp.py`` with cwd = workspace root.

Credentials: ZAI_API_KEY (env) or a gitignored .env.local at the workspace
root, then ``~/.unity-mcp/.env.local``. Images are saved under
.artifacts/zai-images/ and returned inline.
"""

from __future__ import annotations

import base64
import json
import os
import re
import sys
import threading
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple
from urllib.error import HTTPError
from urllib.request import Request, urlopen

SCRIPT_DIR = Path(__file__).resolve().parent
WORKSPACE_ROOT = SCRIPT_DIR.parent.parent
IMAGES_DIR = Path(os.environ.get("ZAI_IMAGE_OUTPUT_DIR") or str(WORKSPACE_ROOT / ".artifacts" / "zai-images"))
DEFAULT_MODEL = os.environ.get("ZAI_IMAGE_MODEL") or "glm-image"
DEFAULT_SIZE = os.environ.get("ZAI_IMAGE_SIZE") or "1024x1024"
ENDPOINT = os.environ.get("ZAI_IMAGE_ENDPOINT") or "https://api.z.ai/api/paas/v4/images/generations"

SUPPORTED_PROTOCOL_VERSIONS: Set[str] = {"2025-06-18", "2025-11-25"}
FALLBACK_PROTOCOL_VERSION = "2025-06-18"

# stdin end waits at most this long for in-flight responses before the
# process force-exits, matching the node server's 15 second grace period.
DRAIN_GRACE_SECONDS = 15.0

_WRITE_LOCK = threading.Lock()


def _read_env_local(file_path: Path) -> Dict[str, str]:
    """Read KEY=VALUE pairs, tolerating a UTF-8 BOM, CRLF endings, and ``export `` prefixes."""
    variables: Dict[str, str] = {}
    try:
        text = file_path.read_text(encoding="utf-8")
    except OSError:
        return variables
    text = text.lstrip("\ufeff").replace("\r\n", "\n").replace("\r", "\n")
    for raw_line in text.split("\n"):
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        eq = line.find("=")
        if eq <= 0:
            continue
        key = line[:eq].strip()
        if key.startswith("export "):
            key = key[len("export ") :].strip()
        value = line[eq + 1 :].strip()
        if (value.startswith('"') and value.endswith('"')) or (value.startswith("'") and value.endswith("'")):
            value = value[1:-1]
        if key:
            variables[key] = value
    return variables


def _env_local() -> Dict[str, str]:
    variables: Dict[str, str] = {}
    variables.update(_read_env_local(WORKSPACE_ROOT / ".env.local"))
    variables.update(_read_env_local(Path(os.environ.get("HOME") or "") / ".unity-mcp" / ".env.local"))
    return variables


def resolve_api_key(env_local: Dict[str, str]) -> str:
    """Resolve the Z.ai key from process env, then the two .env.local locations."""
    key = os.environ.get("ZAI_API_KEY") or os.environ.get("Z_AI_API_KEY") or env_local.get("ZAI_API_KEY") or env_local.get("Z_AI_API_KEY") or ""
    if not key:
        raise ValueError("ZAI_API_KEY is not set. Add it to .env.local at the workspace root (see .env.local.example).")
    return key


def slugify(prompt: str) -> str:
    """Build the filename slug the node server produced for the same prompt."""
    slug = re.sub(r"[^a-z0-9]+", "-", prompt.lower()).strip("-")[:48]
    return slug or "image"


def _http_post_json(url: str, api_key: str, body: Dict[str, Any]) -> str:
    request = Request(
        url,
        data=json.dumps(body).encode("utf-8"),
        headers={"content-type": "application/json", "authorization": "Bearer " + api_key},
    )
    try:
        with urlopen(request) as response:
            return response.read().decode("utf-8")
    except HTTPError as error:
        # urllib raises before the error body is read, but the node server
        # reported the API's status and payload verbatim; auth, quota, and
        # content-policy rejections are only diagnosable from that body.
        payload = error.read().decode("utf-8", errors="replace")
        raise ValueError("Z.ai image API " + str(error.code) + ": " + payload[:500]) from error


def _http_get_bytes(url: str) -> Tuple[bytes, str]:
    try:
        with urlopen(url) as response:
            return response.read(), (response.headers.get("content-type") or "image/png")
    except HTTPError as error:
        raise ValueError("Failed to download generated image: " + str(error.code)) from error


def generate_image(prompt: str, size: Optional[str], model: Optional[str]) -> Dict[str, str]:
    """Call the Z.ai image API and return inline base64 PNG data for the result."""
    api_key = resolve_api_key(_env_local())
    body = {"model": model or DEFAULT_MODEL, "prompt": str(prompt), "size": size or DEFAULT_SIZE}
    text = _http_post_json(ENDPOINT, api_key, body)
    try:
        payload = json.loads(text)
    except json.JSONDecodeError:
        raise ValueError("Z.ai image API returned non-JSON: " + text[:200])
    data = payload.get("data") if isinstance(payload, dict) else None
    item = data[0] if isinstance(data, list) and data else None
    if not isinstance(item, dict):
        raise ValueError("Z.ai image API returned no data: " + text[:300])
    if item.get("b64_json"):
        return {"base64": str(item["b64_json"]), "mimeType": "image/png"}
    url = item.get("url")
    if url:
        blob, mime_type = _http_get_bytes(str(url))
        return {"base64": base64.b64encode(blob).decode("ascii"), "mimeType": mime_type}
    raise ValueError("Z.ai image API returned neither b64_json nor url: " + text[:300])


def save_image(base64_data: str, prompt: str) -> str:
    """Persist the decoded PNG under the images directory with owner-only permissions."""
    blob = base64.b64decode(base64_data, validate=False)
    IMAGES_DIR.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).isoformat().replace(":", "-").replace(".", "-")
    file_path = IMAGES_DIR / (stamp + "-" + slugify(prompt) + ".png")
    flags = os.O_WRONLY | os.O_CREAT | os.O_TRUNC
    if hasattr(os, "O_BINARY"):
        # Windows os.open defaults to text mode, which would translate the
        # PNG's 0x0A bytes; the low-level fd must be explicitly binary there.
        flags |= os.O_BINARY
    target = os.open(str(file_path), flags, 0o600)
    try:
        os.write(target, blob)
    finally:
        os.close(target)
    return str(file_path)


TOOL_DEFINITIONS: List[Dict[str, Any]] = [
    {
        "name": "zai_generate_image",
        "description": (
            "Generate an image with Z.ai's glm-image model from a text prompt. "
            "Returns the PNG inline and saves it under .artifacts/zai-images/."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "prompt": {"type": "string", "description": "What to render."},
                "size": {
                    "type": "string",
                    "description": "Image dimensions, e.g. 1024x1024 (default), 1152x864, 864x1152.",
                },
                "model": {"type": "string", "description": "Z.ai image model id (default glm-image)."},
            },
            "required": ["prompt"],
            "additionalProperties": False,
        },
    },
]


def call_tool(name: str, arguments: Dict[str, Any]) -> Dict[str, Any]:
    """Run one tool call and return the MCP result object (isError on failure)."""
    if name != "zai_generate_image":
        return {"content": [{"type": "text", "text": "Unknown tool: " + name}], "isError": True}
    prompt = str(arguments.get("prompt") or "").strip()
    if not prompt:
        return {"content": [{"type": "text", "text": "prompt is required."}], "isError": True}
    image = generate_image(prompt, arguments.get("size"), arguments.get("model"))
    try:
        saved_path = save_image(image["base64"], prompt)
    except Exception:  # noqa: BLE001 - the node server's bare catch kept the inline image on any save failure
        saved_path = "(not saved)"
    return {
        "content": [
            {"type": "text", "text": 'Generated image for: "' + prompt + '". Saved: ' + saved_path},
            {"type": "image", "data": image["base64"], "mimeType": image["mimeType"]},
        ],
        "isError": False,
    }


def handle_json_rpc_message(message: Any) -> Optional[Dict[str, Any]]:
    """Build the response object for one JSON-RPC message, or None for notifications.

    ``tools/call`` never reaches here: the serve loop runs it in a worker
    thread so a slow image request cannot block the read loop, matching the
    node server's promise-based call path.
    """
    if not isinstance(message, dict):
        # The node server destructured ``message || {}`` and ignored every
        # non-object frame that carried no id.
        return None
    method = str(message.get("method") or "")
    raw_params = message.get("params")
    params = raw_params if isinstance(raw_params, dict) else {}

    def response(result: Dict[str, Any]) -> Optional[Dict[str, Any]]:
        if "id" in message:
            return {"jsonrpc": "2.0", "id": message["id"], "result": result}
        return None

    if method == "initialize":
        requested = str(params.get("protocolVersion") or "")
        return response(
            {
                "protocolVersion": requested if requested in SUPPORTED_PROTOCOL_VERSIONS else FALLBACK_PROTOCOL_VERSION,
                "capabilities": {"tools": {"listChanged": False}},
                "serverInfo": {"name": "zai-image", "version": "1.0.0"},
            }
        )
    if method == "tools/list":
        return response({"tools": TOOL_DEFINITIONS})
    if method == "ping":
        return response({})
    if "id" not in message:
        return None
    return {
        "jsonrpc": "2.0",
        "id": message["id"],
        "error": {"code": -32601, "message": "Method not found: " + method},
    }


def write_message(message: Dict[str, Any]) -> None:
    """Emit one JSON-RPC response line atomically on stdout."""
    line = json.dumps(message) + "\n"
    with _WRITE_LOCK:
        sys.stdout.write(line)
        sys.stdout.flush()


def _internal_error_response(message: Any, error: Exception) -> Dict[str, Any]:
    identifier = message.get("id") if isinstance(message, dict) and "id" in message else None
    return {"jsonrpc": "2.0", "id": identifier, "error": {"code": -32603, "message": str(error) or repr(error)}}


def _run_tool_call(message: Any, params: Dict[str, Any], in_flight: List[int]) -> None:
    """Run one tools/call in its worker thread and write exactly one response.

    Every failure inside the call becomes an ``isError`` result, matching the
    node server's promise ``catch``; ``-32603`` stays reserved for response
    write failures.
    """
    name = str(params.get("name") or "")
    raw_arguments = params.get("arguments")
    arguments = raw_arguments if isinstance(raw_arguments, dict) else {}
    try:
        result = call_tool(name, arguments)
    except Exception as error:  # noqa: BLE001 - every failure still reaches the verdict
        result = {"content": [{"type": "text", "text": str(error) or repr(error)}], "isError": True}
    try:
        if isinstance(message, dict) and "id" in message:
            write_message({"jsonrpc": "2.0", "id": message["id"], "result": result})
    except OSError as error:
        write_message(_internal_error_response(message, error))
    finally:
        in_flight[0] -= 1


def serve() -> int:
    """Serve MCP requests on stdin until EOF, then drain in-flight responses."""
    # The node server pinned UTF-8 on both streams; without this, a Windows
    # host decodes stdin with the locale codec and corrupts non-ASCII prompts.
    # Responses are JSON with ensure_ascii, so stdout is ASCII-safe regardless.
    if hasattr(sys.stdin, "reconfigure"):
        sys.stdin.reconfigure(encoding="utf-8", errors="replace")
    in_flight = [0]
    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue
        try:
            message = json.loads(line)
        except json.JSONDecodeError as error:
            write_message({"jsonrpc": "2.0", "id": None, "error": {"code": -32700, "message": "Parse error: " + str(error)}})
            continue
        if isinstance(message, dict) and message.get("method") == "tools/call":
            raw_params = message.get("params")
            params = raw_params if isinstance(raw_params, dict) else {}
            in_flight[0] += 1
            worker = threading.Thread(target=_run_tool_call, args=(message, params, in_flight), daemon=True)
            worker.start()
            continue
        try:
            response = handle_json_rpc_message(message)
        except Exception as error:  # noqa: BLE001 - every failure still reaches the verdict
            write_message(_internal_error_response(message, error))
            continue
        if response is not None:
            write_message(response)
    # stdin end: exit as soon as every in-flight response finished, at most
    # after the node server's grace period (its setTimeout force-exit).
    deadline = time.monotonic() + DRAIN_GRACE_SECONDS
    while in_flight[0] > 0 and time.monotonic() < deadline:
        time.sleep(0.05)
    return 0


def main() -> int:
    return serve()


if __name__ == "__main__":
    sys.exit(main())
