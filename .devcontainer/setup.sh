#!/bin/bash

# Install zellij
curl -L https://github.com/zellij-org/zellij/releases/latest/download/zellij-x86_64-unknown-linux-musl.tar.gz | tar xz -C ~/.local/bin

# Create zellij config for mobile session (runs claude as default shell)
mkdir -p ~/.config/zellij
cat > ~/.config/zellij/mobile-config.kdl << 'EOF'
default_shell "/home/vscode/.local/bin/claude"
EOF

# Set UTF-8 locale
cat >> ~/.bashrc << 'EOF'
export LANG=C.UTF-8
export LC_ALL=C.UTF-8
EOF

# Install global npm packages
npm install -g opencode-ai@latest

# Install Claude Code
curl -fsSL https://claude.ai/install.sh | bash

# Add mobile zellij helper to bashrc
cat >> ~/.bashrc << 'EOF'

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
