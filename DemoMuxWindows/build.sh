#!/usr/bin/env bash
set -euo pipefail
project="$(cd -- "$(dirname -- "$0")" && pwd)"
if ! command -v pwsh >/dev/null; then
    printf '%s\n' 'Windows 0.3.2 packaging requires PowerShell 7 on Windows: pwsh -File DemoMuxWindows/build.ps1' >&2
    exit 2
fi
exec pwsh -NoProfile -File "$project/build.ps1" "$@"
