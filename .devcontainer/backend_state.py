#!/usr/bin/env python3
"""Write private gateway profiles and Claude approval state (issue #171).

Shell launchers still own credential loading, directory ownership repair,
and exec. This typed, stdlib-only helper owns file serialization and atomic
replacement. No raw credential is written to the generated profiles.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import tempfile
from pathlib import Path
from typing import Dict, Optional, Sequence, cast

CATALOG_JSON = r"""{
  "models": [
    {
      "slug": "glm-5.3",
      "display_name": "glm-5.3",
      "description": "Z.ai flagship coding model",
      "default_reasoning_level": "max",
      "supported_reasoning_levels": [
        {
          "effort": "low",
          "description": "Light reasoning"
        },
        {
          "effort": "high",
          "description": "Enhanced reasoning"
        },
        {
          "effort": "max",
          "description": "Deep reasoning"
        }
      ],
      "shell_type": "shell_command",
      "visibility": "list",
      "supported_in_api": true,
      "priority": 0,
      "base_instructions": "",
      "supports_reasoning_summaries": true,
      "default_reasoning_summary": "none",
      "support_verbosity": false,
      "apply_patch_tool_type": "freeform",
      "truncation_policy": {
        "mode": "bytes",
        "limit": 10000
      },
      "context_window": 1048576,
      "max_context_window": 1048576,
      "effective_context_window_percent": 95,
      "supports_parallel_tool_calls": true,
      "experimental_supported_tools": [],
      "input_modalities": [
        "text"
      ]
    }
  ]
}
"""

ZAI_PROFILE = r"""model_provider = "ZAI"
model = "glm-5.3"
model_reasoning_effort = "max"
model_catalog_json = "${catalog_path_toml}"

[model_providers.ZAI]
name = "ZAI"
base_url = "https://api.z.ai/api/v1"
env_key = "ZAI_API_KEY"
wire_api = "responses"
request_max_retries = 4
stream_max_retries = 5
stream_idle_timeout_ms = 300000

[shell_environment_policy]
filters = { ZAI_API_KEY = "exclude", Z_AI_API_KEY = "exclude" }
"""

OPENROUTER_PROFILE = r"""model_provider = "OPENROUTER"
model = "~openai/gpt-latest"
model_reasoning_effort = "high"

[model_providers.OPENROUTER]
name = "OpenRouter"
base_url = "https://openrouter.ai/api/v1"

[model_providers.OPENROUTER.auth]
command = "sh"
args = ["-c", "echo $OPENROUTER_API_KEY"]

[shell_environment_policy]
filters = { OPENROUTER_API_KEY = "exclude", OPEN_ROUTER_API_KEY = "exclude" }
"""


def write_private(path: Path, content: str) -> bool:
    """Replace a complete private file atomically; unchanged bytes retain mtime."""
    encoded = content.encode("utf-8")
    try:
        if not path.is_symlink() and path.is_file() and path.read_bytes() == encoded:
            path.chmod(0o600)
            return False
    except OSError:
        # Profiles are reproducible: an unreadable root-owned file can still
        # be replaced in the caller's secured, writable directory.
        pass
    with tempfile.TemporaryDirectory(prefix=".backend-state-", dir=path.parent) as staging:
        temporary = Path(staging) / path.name
        temporary.touch(mode=0o600)
        temporary.write_bytes(encoded)
        temporary.chmod(0o600)
        os.replace(temporary, path)
    return True


def write_profile(provider: str, config_dir: Path) -> int:
    """Keep the existing generated profile/catalog contract byte-for-byte."""
    if provider == "zai":
        catalog_path = config_dir / "devcontainer-zai-models.json"
        catalog = write_private(catalog_path, CATALOG_JSON)
        # Existing TOML uses a basic quoted string. Escape its path value,
        # preserving the old quote/backslash behavior and valid control escapes.
        escaped = json.dumps(str(catalog_path), ensure_ascii=False)[1:-1]
        profile = write_private(config_dir / "devcontainer-zai.config.toml", ZAI_PROFILE.replace("${catalog_path_toml}", escaped))
        return int(catalog) + int(profile)
    if provider == "openrouter":
        return int(write_private(config_dir / "devcontainer-openrouter.config.toml", OPENROUTER_PROFILE))
    raise ValueError(f"Unknown provider: {provider}")


def object_value(value: object, description: str) -> Dict[str, object]:
    if value is None:
        return {}
    if not isinstance(value, dict):
        raise ValueError(f"{description} must be a JSON object")
    return cast(Dict[str, object], value)


def read_object(path: Path) -> Dict[str, object]:
    text = path.read_text(encoding="utf-8-sig")
    if not text.strip():
        return {}
    try:
        value: object = json.loads(text)
    except ValueError as error:
        raise ValueError(f"Cannot read {path}: {error}") from error
    return object_value(value, str(path))


def seed_approvals(config_dir: Path, mcp_config: Path, project: str) -> bool:
    """Union server approvals while retaining unrelated projects and settings."""
    if not mcp_config.is_file():
        return False
    servers = object_value(read_object(mcp_config).get("mcpServers"), "mcpServers")
    state_path = config_dir / ".claude.json"
    state = read_object(state_path) if state_path.is_file() else {}
    projects = object_value(state.get("projects"), "projects")
    entry = object_value(projects.get(project), "project entry")
    existing = entry.get("enabledMcpjsonServers", [])
    if existing is None:
        existing = []
    if not isinstance(existing, list):
        raise ValueError("enabledMcpjsonServers must be an array of names")
    raw_names = cast(Sequence[object], existing)
    approved = set(servers)
    for name in raw_names:
        if not isinstance(name, str):
            raise ValueError("enabledMcpjsonServers must contain only names")
        approved.add(name)
    entry["hasTrustDialogAccepted"] = True
    entry["enabledMcpjsonServers"] = sorted(approved)
    projects[project] = entry
    state["projects"] = projects
    return write_private(state_path, json.dumps(state, ensure_ascii=False, indent=2) + "\n")


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Write private gateway profiles and approval state.")
    commands = parser.add_subparsers(dest="command", required=True)
    profile = commands.add_parser("profile")
    profile.add_argument("provider", choices=("zai", "openrouter"))
    profile.add_argument("--config-dir", required=True, type=Path)
    approve = commands.add_parser("approve")
    approve.add_argument("--config-dir", required=True, type=Path)
    approve.add_argument("--mcp-config", required=True, type=Path)
    approve.add_argument("--project", required=True)
    options = parser.parse_args(argv)
    try:
        if options.command == "profile":
            write_profile(options.provider, options.config_dir)
        else:
            seed_approvals(options.config_dir, options.mcp_config, options.project)
        return 0
    except (OSError, ValueError) as error:
        print(f"[ai-backends] ERROR: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
