#!/bin/bash
set -euo pipefail

version=1.104.5
commit=73a48057aaf5f60e4f770b398e9b7af576b01dbe
project="$(cd -- "$(dirname -- "$0")" && pwd)"
output="$(dirname -- "$project")/outputs/DemoMuxWindows"
mkdir -p "$output"
stage="$(mktemp -d "$output/.tailscale.XXXXXXXX")"
trap 'find "$stage" -depth -delete' EXIT

mkdir -p "$stage/source" "$stage/payload/licenses"
curl -fsSL --retry 2 "https://codeload.github.com/tailscale/tailscale/tar.gz/$commit" -o "$stage/source.tar.gz"
tar -xzf "$stage/source.tar.gz" --strip-components 1 -C "$stage/source"
flags="-s -w -X tailscale.com/version.shortStamp=$version -X tailscale.com/version.longStamp=$version-t73a48057a -X tailscale.com/version.gitCommitStamp=$commit"
(
    cd "$stage/source"
    export GOOS=windows GOARCH=amd64 CGO_ENABLED=0 GOTOOLCHAIN=local
    go build -mod=readonly -trimpath -ldflags "$flags" -o "$stage/payload/tailscale.exe" ./cmd/tailscale
    go build -mod=readonly -trimpath -ldflags "$flags" -o "$stage/payload/tailscaled.exe" ./cmd/tailscaled
    go list -mod=readonly -deps -f '{{with .Module}}{{.Path}}{{"\t"}}{{.Version}}{{"\t"}}{{.Dir}}{{end}}' \
        ./cmd/tailscale ./cmd/tailscaled | sort -u > "$stage/modules.tsv"
)
cp "$stage/source/LICENSE" "$stage/payload/licenses/Tailscale.txt"
cp "$(go env GOROOT)/LICENSE" "$stage/payload/licenses/Go.txt"
python3 - "$stage/modules.tsv" "$stage/payload/licenses" <<'PY'
from pathlib import Path
from shutil import copyfile
import sys

table, destination = map(Path, sys.argv[1:])
for row in table.read_text().splitlines():
    if not row:
        continue
    module, version, path = row.split("\t")
    if not version:
        continue
    names = {"LICENSE", "LICENCE", "NOTICE", "COPYING", "COPYRIGHT"}
    files = [
        entry for entry in Path(path).iterdir()
        if entry.is_file() and entry.name.upper().split(".")[0].split("-")[0] in names
    ]
    module_directory = destination / (module + "@" + version)
    for entry in files:
        module_directory.mkdir(parents=True, exist_ok=True)
        copyfile(entry, module_directory / entry.name)
PY
printf 'Source: https://github.com/tailscale/tailscale/tree/%s\nTag: v%s\nBuild: Windows amd64, CGO disabled.\n' \
    "$commit" "$version" > "$stage/payload/SOURCE.txt"
archive="Tailscale-$version-Windows-x64.zip"
(cd "$stage/payload" && zip -q -r "$stage/$archive" .)
(cd "$stage" && shasum -a 256 "$archive" > "$archive.sha256")
mv -f "$stage/$archive" "$stage/$archive.sha256" "$output/"
printf '%s\n' "$output/$archive"
