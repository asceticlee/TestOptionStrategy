#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
CLIENT_DIR="$REPO_ROOT/Client"
PORT=3000
LOG_DIR="$SCRIPT_DIR/logs"
LOG="$LOG_DIR/web.log"
PID_FILE="$SCRIPT_DIR/web.pid"

mkdir -p "$LOG_DIR"

if [ -f "$PID_FILE" ] && kill -0 "$(cat "$PID_FILE")" 2>/dev/null; then
    echo "Web already running (pid $(cat "$PID_FILE"))"
    exit 0
fi

cd "$CLIENT_DIR"

if [ "${1:-}" = "--build" ] || [ ! -d ".next" ]; then
    echo "Building web..."
    npm run build
fi

echo "Starting web on http://localhost:$PORT ..."
nohup npm run start > "$LOG" 2>&1 &
echo $! > "$PID_FILE"

sleep 2
echo "Web started (pid $(cat "$PID_FILE")), log: $LOG"
