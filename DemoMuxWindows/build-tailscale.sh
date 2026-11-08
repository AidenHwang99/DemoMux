#!/usr/bin/env bash
set -euo pipefail
project="$(cd -- "$(dirname -- "$0")" && pwd)"
if ! command -v pwsh >/dev/null; then
    printf '%s\n' 'Use PowerShell 7 and Go 1.27.3 on Windows: pwsh -File DemoMuxWindows/build-tailscale.ps1' >&2
    exit 2
fi
exec pwsh -NoProfile -File "$project/build-tailscale.ps1" "$@"
