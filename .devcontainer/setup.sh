#!/bin/bash

# Configure tmux
cat >> ~/.tmux.conf << 'EOF'
set -g mouse on
set -g status-style 'bg=#333333 fg=#888888'
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

# Add mobile tmux helper to bashrc
cat >> ~/.bashrc << 'EOF'

# Start tmux mobile session with claude
mobile() {
    if tmux has-session -t mobile 2>/dev/null; then
        tmux -u attach -t mobile
    else
        tmux -u new -s mobile \; \
            set-environment VSCODE_IPC_HOOK_CLI "$VSCODE_IPC_HOOK_CLI" \; \
            set-environment CLAUDE_CODE_SSE_PORT "$CLAUDE_CODE_SSE_PORT" \; \
            send-keys 'claude' Enter
    fi
}
EOF
