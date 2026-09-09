#!/usr/bin/env bash
# Install the current npm latest dist-tag for the coding CLIs as the container user.
# Never runs npm as root and never needs sudo: the image makes the global prefix
# group-writable through the stable nvm group, and this script refuses to proceed
# (with remediation guidance) if any required path is not writable.

set -euo pipefail

offline_ok=0
case "${1:-}" in
    "") ;;
    --offline-ok) offline_ok=1 ;;
    *)
        echo "Usage: $0 [--offline-ok]" >&2
        exit 2
        ;;
esac

install_attempts="${DATAVIZ_NPM_INSTALL_ATTEMPTS:-5}"
if ! [[ "${install_attempts}" =~ ^[1-9][0-9]*$ ]]; then
    echo "❌ DATAVIZ_NPM_INSTALL_ATTEMPTS must be a positive integer." >&2
    exit 2
fi

readonly NPM_TOOL_PACKAGES=(
    "@nanocollective/nanocoder@latest"
    "opencode-ai@latest"
    "@openai/codex@latest"
    "@anthropic-ai/claude-code@latest"
)
readonly NPM_TOOL_COMMANDS=(
    "nanocoder"
    "opencode"
    "codex"
    "claude"
)

run_with_retries() {
    local max_attempts="$1"
    shift

    local attempt=1
    local delay_seconds=2
    while true; do
        if "$@"; then
            return 0
        else
            local exit_code=$?
        fi

        if [ "${attempt}" -ge "${max_attempts}" ]; then
            return "${exit_code}"
        fi

        local next_attempt=$((attempt + 1))
        echo "   npm request attempt ${attempt}/${max_attempts} failed; retrying in ${delay_seconds}s." >&2
        sleep "${delay_seconds}"
        attempt="${next_attempt}"
        delay_seconds=$((delay_seconds * 2))
    done
}

read_installed_tree() {
    local tree
    local list_exit_code=0
    tree="$(npm list --global --depth=0 --json)" || list_exit_code=$?

    if ! node -e '
        const fs = require("node:fs");
        JSON.parse(fs.readFileSync(0, "utf8"));
    ' <<<"${tree}" >/dev/null; then
        echo "❌ npm list did not return a valid global dependency tree." >&2
        if [ "${list_exit_code}" -eq 0 ]; then
            return 1
        fi
        return "${list_exit_code}"
    fi

    if [ "${list_exit_code}" -ne 0 ]; then
        echo "⚠️  npm list exited ${list_exit_code}, but its valid dependency tree will be used." >&2
    fi
    printf '%s\n' "${tree}"
}

if [ "$(id -u)" -eq 0 ]; then
    echo "❌ Refusing to install npm tools as root; run this script as the container user." >&2
    exit 1
fi

for command_name in node npm; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "❌ Required command not found: ${command_name}" >&2
        exit 1
    fi
done

# shellcheck disable=SC2016 # Dollar syntax below belongs to JavaScript.
node -e 'const major = Number(process.versions.node.split(".")[0]); if (major < 22) { console.error(`Node.js 22+ is required; found ${process.version}.`); process.exit(1); }'

# Keep large CLI tarballs out of the user's normal npm cache. Metadata can be
# reused between no-op startup checks; successful updates clear this cache below.
export NPM_CONFIG_CACHE="${DATAVIZ_NPM_TOOL_CACHE:-${XDG_CACHE_HOME:-${HOME}/.cache}/dataviz-npm-tools}"
npm_prefix="$(npm prefix --global)"
npm_cache="$(npm config get cache)"
writable_paths=(
    "${npm_prefix}"
    "${npm_prefix}/bin"
    "${npm_prefix}/lib/node_modules"
    "${npm_cache}"
)
while IFS= read -r -d '' writable_path; do
    writable_paths+=("${writable_path}")
done < <(
    find "${npm_prefix}/lib/node_modules" -type d -print0
)
for writable_path in "${writable_paths[@]}"; do
    if [ ! -d "${writable_path}" ]; then
        if ! mkdir -p "${writable_path}"; then
            echo "❌ npm path cannot be created by $(id -un): ${writable_path}" >&2
            echo "   Rebuild the devcontainer; do not rerun npm with sudo." >&2
            exit 1
        fi
    fi
    if [ ! -w "${writable_path}" ]; then
        echo "❌ npm path is not writable by $(id -un): ${writable_path}" >&2
        echo "   Rebuild the devcontainer; do not rerun npm with sudo." >&2
        exit 1
    fi
done

echo "📦 Refreshing npm coding tools from the latest dist-tag..."
echo "   Node.js: $(node --version); npm: $(npm --version); prefix: ${npm_prefix}"

installed_tree="$(read_installed_tree)"
packages_to_install=()
registry_unverified=0
clean_tool_cache=0
for package_index in "${!NPM_TOOL_PACKAGES[@]}"; do
    package_spec="${NPM_TOOL_PACKAGES[package_index]}"
    command_name="${NPM_TOOL_COMMANDS[package_index]}"
    package_name="${package_spec%@latest}"
    installed_version="$(
        node -e '
            const fs = require("node:fs");
            const tree = JSON.parse(fs.readFileSync(0, "utf8"));
            process.stdout.write(tree.dependencies?.[process.argv[1]]?.version ?? "");
        ' "${package_name}" <<<"${installed_tree}"
    )"

    expected_command="${npm_prefix}/bin/${command_name}"
    if ! latest_version="$(run_with_retries "${install_attempts}" npm view "${package_spec}" version)"; then
        if [ "${offline_ok}" = "1" ] && [ -n "${installed_version}" ] && [ -x "${expected_command}" ]; then
            echo "⚠️  npm registry unavailable; keeping ${package_name}@${installed_version}." >&2
            registry_unverified=1
            continue
        fi
        echo "❌ Unable to resolve ${package_spec} from npm." >&2
        exit 1
    fi

    if [ "${installed_version}" = "${latest_version}" ] && [ -x "${expected_command}" ]; then
        echo "   Current: ${package_name}@${installed_version}"
    else
        echo "   Update:  ${package_name}@${installed_version:-missing} -> ${latest_version}"
        packages_to_install+=("${package_spec}")
    fi
done

# npm 11 blocks dependency lifecycle scripts unless explicitly trusted. OpenCode
# and Claude Code use install scripts and are the only trusted global packages.
if [ "${#packages_to_install[@]}" -gt 0 ]; then
    if run_with_retries "${install_attempts}" npm install --global \
        --allow-scripts=opencode-ai,@anthropic-ai/claude-code \
        "${packages_to_install[@]}"; then
        clean_tool_cache=1
    else
        install_exit_code=$?
        clean_tool_cache=1
        if [ "${offline_ok}" != "1" ]; then
            npm cache clean --force || true
            exit "${install_exit_code}"
        fi

        fallback_tree="$(read_installed_tree)"
        for package_index in "${!NPM_TOOL_PACKAGES[@]}"; do
            package_name="${NPM_TOOL_PACKAGES[package_index]%@latest}"
            command_name="${NPM_TOOL_COMMANDS[package_index]}"
            fallback_version="$(
                node -e '
                    const fs = require("node:fs");
                    const tree = JSON.parse(fs.readFileSync(0, "utf8"));
                    process.stdout.write(tree.dependencies?.[process.argv[1]]?.version ?? "");
                ' "${package_name}" <<<"${fallback_tree}"
            )"
            if [ -z "${fallback_version}" ] || [ ! -x "${npm_prefix}/bin/${command_name}" ]; then
                echo "❌ npm update failed and the installed ${package_name} fallback is not intact." >&2
                npm cache clean --force || true
                exit "${install_exit_code}"
            fi
        done
        echo "⚠️  npm update failed; continuing with the intact installed tool versions." >&2
    fi
elif [ "${registry_unverified}" = "1" ]; then
    echo "   No npm changes applied; latest status could not be verified."
else
    echo "   All npm coding tools are current."
fi

tool_versions=()
for package_index in "${!NPM_TOOL_COMMANDS[@]}"; do
    command_name="${NPM_TOOL_COMMANDS[package_index]}"
    expected_command="${npm_prefix}/bin/${command_name}"
    resolved_command="$(command -v "${command_name}" || true)"
    expected_target="$(readlink -f "${expected_command}" 2>/dev/null || true)"
    resolved_target="$(readlink -f "${resolved_command}" 2>/dev/null || true)"
    if [ ! -x "${expected_command}" ] || [ -z "${expected_target}" ] || [ "${resolved_target}" != "${expected_target}" ]; then
        echo "❌ Expected ${command_name} from ${expected_command}, but PATH resolved ${resolved_command:-nothing}." >&2
        exit 1
    fi
    if ! tool_version="$(timeout 30 "${resolved_command}" --version 2>&1)"; then
        echo "❌ ${command_name} is installed but failed its version check:" >&2
        echo "${tool_version}" >&2
        exit 1
    fi
    tool_versions+=("${tool_version}")
done

printf "   %-11s %s\n" "nanocoder:" "${tool_versions[0]}"
printf "   %-11s %s\n" "opencode:" "${tool_versions[1]}"
printf "   %-11s %s\n" "codex:" "${tool_versions[2]}"
printf "   %-11s %s\n" "claude:" "${tool_versions[3]}"

if [ "${clean_tool_cache}" = "1" ] || [ "${DATAVIZ_NPM_CLEAN_CACHE:-0}" = "1" ]; then
    npm cache clean --force
fi
