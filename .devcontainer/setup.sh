#!/bin/bash

# This script only contains user-specific config that can't be baked into the Docker image.
# All downloads/installs have been moved to the Dockerfile for caching.

echo "=== Configuring user environment ==="

# Start SSH server for remote access
echo ">>> Starting SSH server..."
sudo service ssh start

# Install Playwright with Chromium browser
# Note: This must be done here (not in Dockerfile) because Node comes from a devcontainer feature
echo ">>> Installing Playwright..."
npx playwright install chromium --with-deps

# Playwright MCP expects Chrome at /opt/google/chrome/chrome
# Playwright installs it elsewhere, so create a symlink
echo ">>> Setting up Playwright Chrome symlink..."
CHROME_PATH=$(find ~/.cache/ms-playwright -name "chrome" -path "*/chrome-linux/*" 2>/dev/null | head -1)
if [ -n "$CHROME_PATH" ]; then
    sudo mkdir -p /opt/google/chrome
    sudo ln -sf "$CHROME_PATH" /opt/google/chrome/chrome
    echo "    Linked $CHROME_PATH -> /opt/google/chrome/chrome"
else
    echo "    Warning: Playwright Chrome not found, skipping symlink"
fi

# npm is only available after the node devcontainer feature runs, so this can't be in Dockerfile
echo ">>> Installing opencode-ai..."
npm install -g opencode-ai@latest

echo ">>> Creating zellij config..."
mkdir -p ~/.config/zellij
cat > ~/.config/zellij/mobile-config.kdl << 'EOF'
default_shell "/home/vscode/.local/bin/claude"
EOF

echo ">>> Configuring bashrc..."
cat >> ~/.bashrc << 'EOF'
export LANG=C.UTF-8
export LC_ALL=C.UTF-8

# Start zellij mobile session with claude
mobile() {
    # Try to attach to existing session, or create new one
    if ! zellij attach mobile 2>/dev/null; then
        # Session doesn't exist or is dead - clean up and create fresh
        zellij delete-session mobile --force 2>/dev/null
        zellij --config ~/.config/zellij/mobile-config.kdl --session mobile
    fi
}
EOF

echo "=== User environment configured ==="
