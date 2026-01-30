FROM mcr.microsoft.com/devcontainers/dotnet:1-9.0-bookworm

# FIX: Remove the broken Yarn repository list that is causing the "not signed" error
# We also run apt-get update to ensure the package list is clean for subsequent features
RUN rm -f /etc/apt/sources.list.d/yarn.list \
    && apt-get update \
    && apt-get install -y tmux \
    && rm -rf /var/lib/apt/lists/*

# Install zellij (system-wide so it's available to vscode user)
RUN curl -L https://github.com/zellij-org/zellij/releases/latest/download/zellij-x86_64-unknown-linux-musl.tar.gz | tar xz -C /usr/local/bin

# Install Claude Code for vscode user
USER vscode
RUN curl -fsSL https://claude.ai/install.sh | bash -x
USER root
