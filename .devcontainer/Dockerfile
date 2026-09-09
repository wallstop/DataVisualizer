# Data Visualizer Dev Container Dockerfile
# Based on the official .NET devcontainer image (same proven digest as NovaSharp)
# with PowerShell, Node.js LTS via the base image's NVM setup, and the MCP tooling.
# Prioritizes deterministic cold-cache reliability and fast cached rebuilds.

FROM mcr.microsoft.com/devcontainers/dotnet@sha256:7572080d1ce09283b8bc2504e3e88cab79dede5eb7e66973d544eea86c3fa216

# Use bash with pipefail so failed downloads in pipelines are surfaced.
SHELL ["/bin/bash", "-o", "pipefail", "-c"]

# Pinned checksum for Microsoft package repository bootstrap package.
ARG MS_PACKAGES_DEB_SHA256=8434dcb8c346dc95fbd63dbece056c343704590b58b6a5c323d39acf52bf0b48
# Pinned uv release (provides uvx for the fetch/git MCP servers).
ARG UV_VERSION=0.12.10

ENV DEBIAN_FRONTEND=noninteractive

# =============================================================================
# LAYER 1: Core system packages
# =============================================================================
# Includes Claude Code's Linux credential-isolation tools (bubblewrap, socat),
# Python for uvx-based MCP servers, and a dev-friendly CLI toolkit.
RUN set -eux; \
    # Base image may include a stale Yarn repo key; remove Yarn apt source since Yarn is not required.
    rm -f /etc/apt/sources.list.d/yarn.list /etc/apt/sources.list.d/yarn.list.save; \
    rm -f /etc/apt/trusted.gpg.d/yarn*.gpg /usr/share/keyrings/yarnkey.gpg; \
    printf 'Acquire::Retries "5";\n' > /etc/apt/apt.conf.d/99-dataviz-retries; \
    printf 'Acquire::http::Timeout "30";\n' >> /etc/apt/apt.conf.d/99-dataviz-retries; \
    printf 'Acquire::https::Timeout "30";\n' >> /etc/apt/apt.conf.d/99-dataviz-retries; \
    apt-get update; \
    apt-get install -y --no-install-recommends \
        curl \
        ca-certificates \
        wget \
        unzip \
        gnupg \
        apt-transport-https \
        software-properties-common \
        # Development utilities
        ripgrep \
        jq \
        tree \
        htop \
        ncdu \
        fd-find \
        bat \
        fzf \
        zoxide \
        # Claude Code Linux sandbox support
        bubblewrap \
        socat \
        # Python for uvx-launched MCP servers (fetch, git)
        python3 \
        python3-venv \
        python3-pip; \
    # Debian package names differ from expected command names.
    ln -sf /usr/bin/fdfind /usr/local/bin/fd; \
    ln -sf /usr/bin/batcat /usr/local/bin/bat; \
    apt-get clean; \
    rm -rf /var/lib/apt/lists/*

# =============================================================================
# LAYER 2: Add Microsoft signed apt feed and install PowerShell (repo lint scripts)
# =============================================================================
RUN set -eux; \
    wget -q --tries=5 --waitretry=2 --timeout=30 "https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb" -O /tmp/packages-microsoft-prod.deb; \
    echo "${MS_PACKAGES_DEB_SHA256}  /tmp/packages-microsoft-prod.deb" | sha256sum -c -; \
    dpkg -i /tmp/packages-microsoft-prod.deb; \
    rm /tmp/packages-microsoft-prod.deb; \
    apt-get update; \
    apt-get install -y --no-install-recommends powershell; \
    apt-get clean; \
    rm -rf /var/lib/apt/lists/*

# =============================================================================
# LAYER 3: uv from PyPI (pinned)
# Provides uvx for mcp-server-fetch and mcp-server-git. PyPI is used instead
# of GitHub release tarballs because GitHub's CDN edges intermittently serve
# stale 404s to build networks; PyPI wheels carry the same pinned binaries.
# =============================================================================
RUN set -eux; \
    python3 -m pip install --no-cache-dir --break-system-packages "uv==${UV_VERSION}"; \
    uv --version; \
    uvx --version

# =============================================================================
# ENVIRONMENT CONFIGURATION
# =============================================================================
ENV DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_NOLOGO=true \
    NVM_DIR=/usr/local/share/nvm \
    NVM_SYMLINK_CURRENT=true \
    NPM_CONFIG_FETCH_RETRIES=5 \
    NPM_CONFIG_FETCH_RETRY_MINTIMEOUT=20000 \
    NPM_CONFIG_FETCH_RETRY_MAXTIMEOUT=120000 \
    NPM_CONFIG_FETCH_TIMEOUT=300000 \
    NPM_CONFIG_FUND=false \
    NPM_CONFIG_UPDATE_NOTIFIER=false \
    PATH=/usr/local/share/nvm/current/bin:${PATH}

# Install the current Node.js LTS and npm coding tools as the non-root user. The
# base image's shared NVM directory is group-writable by the vscode/nvm user.
COPY --chmod=0755 --chown=vscode:nvm install-npm-tools.sh /usr/local/bin/dataviz-install-npm-tools
USER vscode
RUN set -eux; \
    source "${NVM_DIR}/nvm.sh"; \
    umask 0002; \
    nvm install --lts; \
    nvm alias default 'lts/*'; \
    nvm cache clear
RUN DATAVIZ_NPM_CLEAN_CACHE=1 dataviz-install-npm-tools
USER root

# Dev Containers may remap vscode's UID to match the host. npm stages existing
# package contents file-by-file during replacement, so every directory in the
# global package tree must remain writable through the stable nvm group.
RUN set -eux; \
    npm_prefix="$(npm prefix --global)"; \
    chgrp nvm "${npm_prefix}" "${npm_prefix}/bin" "${npm_prefix}/lib"; \
    chmod g+rws "${npm_prefix}" "${npm_prefix}/bin" "${npm_prefix}/lib"; \
    find "${npm_prefix}/lib/node_modules" -type d \
        -exec chgrp nvm {} + -exec chmod g+rws {} +; \
    nvm_group_id="$(getent group nvm | cut -d: -f3)"; \
    setpriv --reuid 12345 --regid 12345 --groups "${nvm_group_id}" test -w "${npm_prefix}/bin"; \
    test -z "$(setpriv --reuid 12345 --regid 12345 --groups "${nvm_group_id}" \
        find "${npm_prefix}/lib/node_modules" -type d ! -writable -print -quit)"

# Configure Git defaults (useful for devcontainer users).
RUN git config --system init.defaultBranch main \
    && git config --system core.autocrlf input \
    && git config --system core.eol lf

# Initialize zoxide for bash (add to bashrc for interactive shells).
RUN echo 'eval "$(zoxide init bash)"' >> /etc/bash.bashrc

# Add helpful aliases for installed CLI tools.
RUN { \
    echo ''; \
    echo '# Data Visualizer CLI aliases'; \
    echo 'if command -v fd >/dev/null 2>&1; then alias find="fd"; fi'; \
    echo 'if command -v bat >/dev/null 2>&1; then alias cat="bat --paging=never"; fi'; \
    echo 'if command -v rg >/dev/null 2>&1; then alias grep="rg"; fi'; \
    echo 'if command -v zoxide >/dev/null 2>&1; then alias cd="z"; alias cdi="zi"; fi'; \
    echo 'alias ll="ls -lah --color=auto"'; \
    echo 'alias lt="tree -a -I .git"'; \
    } >> /etc/bash.bashrc

# Enable VS Code terminal shell integration.
RUN echo '' >> /etc/bash.bashrc \
    && echo '# VS Code Shell Integration' >> /etc/bash.bashrc \
    && echo 'if [[ "$TERM_PROGRAM" == "vscode" ]]; then' >> /etc/bash.bashrc \
    && echo '  . "$(code --locate-shell-integration-path bash 2>/dev/null)" 2>/dev/null || true' >> /etc/bash.bashrc \
    && echo 'fi' >> /etc/bash.bashrc

# =============================================================================
# VERIFICATION
# =============================================================================
RUN echo "=== Environment Verification ===" \
    && echo "--- .NET SDKs ---" && dotnet --list-sdks \
    && echo "--- Node.js Tooling ---" && node --version && npm --version \
    && nanocoder --version && opencode --version && codex --version && claude --version \
    && echo "--- PowerShell ---" && pwsh --version \
    && echo "--- MCP Server Runtimes ---" \
    && uvx --version \
    && echo "--- Core CLI Tools ---" \
    && bwrap --version && socat -V \
    && fd --version && bat --version && fzf --version && zoxide --version \
    && rg --version && jq --version && tree --version \
    && echo "=== Verification Complete ==="

# Pre-create the agent home directories that back Docker named volumes
# (devcontainer.json mounts). When the image lacks these paths, Docker
# initializes fresh volumes as root:root and every agent CLI fails to chmod
# them; a directory with correct ownership in the image is copied into the
# new volume, so mounts start writable.
RUN set -eux; \
    for dir in \
        /home/vscode/.codex \
        /home/vscode/.claude \
        /home/vscode/.claude-zai \
        /home/vscode/.claude-openrouter \
        /home/vscode/.local/share/opencode \
        /home/vscode/.config/nanocoder; \
    do mkdir -p "${dir}" && chown vscode:vscode "${dir}" && chmod 700 "${dir}"; done

USER vscode
