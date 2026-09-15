#!/usr/bin/env bash
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PID_FILE="$SCRIPT_DIR/server.pid"
PORT=5210

if [ -f "$PID_FILE" ]; then
    PID=$(cat "$PID_FILE")
    if kill -0 "$PID" 2>/dev/null; then
        kill "$PID"
        echo "Stopped server (pid $PID)"
        rm -f "$PID_FILE"
        exit 0
    fi
    rm -f "$PID_FILE"
fi

PID=$(ss -ltnp 2>/dev/null | grep ":$PORT " | grep -oP 'pid=\K[0-9]+' | head -1 || true)
if [ -n "$PID" ]; then
    kill "$PID"
    echo "Stopped server (pid $PID)"
else
    echo "Server is not running"
fi
