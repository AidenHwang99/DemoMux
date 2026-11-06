#!/bin/bash
set -euo pipefail

source_dir="$(cd -- "$(dirname -- "$0")" && pwd)"
output_dir="$(dirname -- "$source_dir")/outputs/DemoMux"
if [[ $# -gt 0 ]]; then output_dir="$1"; fi
mkdir -p "$output_dir"
output_dir="$(cd -- "$output_dir" && pwd)"
stage="$(mktemp -d "$output_dir/.build.XXXXXXXX")"
trap 'if [[ -d "$stage" ]]; then /usr/bin/find "$stage" -depth -delete; fi' EXIT
app="$stage/payload/DemoMux.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp "$source_dir/Resources/Info.plist" "$app/Contents/Info.plist"
cp "$source_dir/Resources/AppIcon.icns" "$app/Contents/Resources/"
cp -R "$source_dir/Resources/Sample" "$app/Contents/Resources/"
/usr/bin/swiftc -O -target arm64-apple-macos26.0 \
    "$source_dir/Runtime.swift" "$source_dir/main.swift" -o "$app/Contents/MacOS/DemoMux"
/usr/bin/plutil -lint "$app/Contents/Info.plist"
/usr/bin/codesign --force --sign - "$app"
/usr/bin/codesign --verify --strict "$app"
cp "$source_dir/首次使用.md" "$stage/payload/"
archive="DemoMux-macOS-arm64.zip"
/usr/bin/ditto -c -k --sequesterRsrc "$stage/payload" "$stage/$archive"
(cd "$stage" && /usr/bin/shasum -a 256 "$archive" > "$archive.sha256")
mv -f "$stage/$archive" "$stage/$archive.sha256" "$stage/payload/首次使用.md" "$output_dir/"
printf '%s\n' "$output_dir/$archive"
