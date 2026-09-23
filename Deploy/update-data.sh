#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
SERVER_DIR="$REPO_ROOT/Server"
DLL="$SERVER_DIR/bin/Debug/net8.0/TestOptionStrategy.Server.dll"

echo "Building server..."
dotnet build "$SERVER_DIR/TestOptionStrategy.Server.csproj" -v q --nologo > /dev/null

cd "$SERVER_DIR"
exec dotnet "$DLL" --update "$@"
