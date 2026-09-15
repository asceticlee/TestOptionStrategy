#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
SERVER_DIR="$REPO_ROOT/Server"
DLL="$SERVER_DIR/bin/Debug/net8.0/TestOptionStrategy.Server.dll"
PORT=5210
LOG_DIR="$SCRIPT_DIR/logs"
LOG="$LOG_DIR/server.log"
PID_FILE="$SCRIPT_DIR/server.pid"

mkdir -p "$LOG_DIR"

if [ -f "$PID_FILE" ] && kill -0 "$(cat "$PID_FILE")" 2>/dev/null; then
    echo "Server already running (pid $(cat "$PID_FILE"))"
    exit 0
fi

echo "Building server..."
dotnet build "$SERVER_DIR/TestOptionStrategy.Server.csproj" -v q --nologo > /dev/null

echo "Starting server on http://localhost:$PORT ..."
cd "$SERVER_DIR"
nohup dotnet "$DLL" --urls "http://localhost:$PORT" > "$LOG" 2>&1 &
echo $! > "$PID_FILE"

sleep 1
if kill -0 "$(cat "$PID_FILE")" 2>/dev/null; then
    echo "Server started (pid $(cat "$PID_FILE")), log: $LOG"
else
    echo "Server failed to start; check $LOG"
    rm -f "$PID_FILE"
    exit 1
fi
