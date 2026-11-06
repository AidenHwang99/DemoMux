#!/bin/bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
    printf '%s\n' 'Usage: build.sh <Tailscale component ZIP> <fixed public GitHub release asset URL>' >&2
    exit 2
fi
project="$(cd -- "$(dirname -- "$0")" && pwd)"
component="$(cd -- "$(dirname -- "$1")" && pwd)/$(basename -- "$1")"
release_url="$2"
output="$(dirname -- "$project")/outputs/DemoMuxWindows"
mkdir -p "$output"
stage="$(mktemp -d "$output/.build.XXXXXXXX")"
trap 'find "$stage" -depth -delete' EXIT

caddy_url='https://github.com/caddyserver/caddy/releases/download/v2.11.9/caddy_2.11.9_windows_amd64.zip'
if [[ -n "${DEMOMUX_CADDY_ARCHIVE:-}" ]]; then
    cp "$DEMOMUX_CADDY_ARCHIVE" "$stage/caddy.zip"
else
    curl -fsSL --retry 2 "$caddy_url" -o "$stage/caddy.zip"
fi
python3 - "$project" "$component" "$release_url" "$stage/caddy.zip" "$caddy_url" <<'PY'
from hashlib import sha256
import json
from pathlib import Path
import re
import sys
from zipfile import ZipFile

project, ts_zip, ts_url, caddy_zip, caddy_url = sys.argv[1:]
if not re.fullmatch(r"https://github\.com/[\w.-]+/[\w.-]+/releases/download/[\w.-]+/[\w.-]+\.zip", ts_url):
    raise SystemExit("Use a fixed GitHub Releases asset URL")
def entry(name, version, url, archive, executables):
    with ZipFile(archive) as zip_file:
        files = {exe: sha256(zip_file.read(exe)).hexdigest() for exe in executables}
    return dict(name=name, version=version, url=url, sha256=sha256(Path(archive).read_bytes()).hexdigest(), files=files)

caddy = entry("Caddy", "2.11.9", caddy_url, caddy_zip, ["caddy.exe"])
if caddy["sha256"] != "ad93112d335ee687100c4c9f11bced16ca590377976b7fe7883882b9e797e472":
    raise SystemExit("Caddy archive SHA-256 mismatch")
tailscale = entry("Tailscale", "1.104.5", ts_url, ts_zip, ["tailscale.exe", "tailscaled.exe"])
Path(project, "dependencies.json").write_text(json.dumps([caddy, tailscale], indent=2) + "\n")
PY
dotnet publish "$project/DemoMux.csproj" -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o "$stage/payload"
cp "$project/首次使用.md" "$stage/payload/"
archive=DemoMux-Windows-x64.zip
(cd "$stage/payload" && zip -q -r "$stage/$archive" .)
(cd "$stage" && shasum -a 256 "$archive" > "$archive.sha256")
mv -f "$stage/$archive" "$stage/$archive.sha256" "$output/"
cp "$project/首次使用.md" "$output/"
printf '%s\n' "$output/$archive"
