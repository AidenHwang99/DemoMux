#!/bin/bash
set -euo pipefail
source_dir="$(cd -- "$(dirname -- "$0")/.." && pwd)"
qa_dir="$(mktemp -d /tmp/demomux-interaction.XXXXXXXX)"
python3 - "$source_dir" "$qa_dir" <<'PY'
from pathlib import Path
import sys
source=Path(sys.argv[1]);qa=Path(sys.argv[2])
text=(source/'Runtime.swift').read_text()
old='let support = fileManager.homeDirectoryForCurrentUser\n    .appendingPathComponent("Library/Application Support/DemoMux", isDirectory: true)'
assert text.count(old)==1
text=text.replace(old,'let support = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("DemoMux-IsolatedUI-" + ProcessInfo.processInfo.globallyUniqueString, isDirectory: true)')
(qa/'Runtime.swift').write_text(text)
(qa/'Controller.swift').write_text((source/'main.swift').read_text().rsplit('MainActor.assumeIsolated {',1)[0])
PY
/usr/bin/swiftc -O -warnings-as-errors -target arm64-apple-macos26.0 \
    "$qa_dir/Runtime.swift" "$source_dir/Projects.swift" "$source_dir/Gateway.swift" "$source_dir/Accounts.swift" \
    "$source_dir/Verification.swift" "$source_dir/NativeControls.swift" "$source_dir/SetupInterface.swift" \
    "$source_dir/AccountController.swift" "$source_dir/AccountInterface.swift" "$source_dir/Interface.swift" \
    "$qa_dir/Controller.swift" "$source_dir/Tests/InteractionSmoke.swift" -o "$qa_dir/DemoMuxQA"
/usr/bin/clang -O2 -Wall -Wextra -Werror -target arm64-apple-macos26.0 "$source_dir/Supervisor.c" -o "$qa_dir/Supervisor"
for kind in Layout Retry; do
    qa_app="$qa_dir/DemoMux${kind}QA.app"
    mkdir -p "$qa_app/Contents/MacOS" "$qa_app/Contents/Helpers" "$qa_app/Contents/Resources"
    cp "$qa_dir/DemoMuxQA" "$qa_app/Contents/MacOS/DemoMuxQA"
    cp "$qa_dir/Supervisor" "$qa_app/Contents/Helpers/DemoMuxSupervisor"
    cp -R "$source_dir/Resources/Sample" "$qa_app/Contents/Resources/"
    cp -R "$source_dir/../Shared/Icons" "$qa_app/Contents/Resources/Icons"
    cat > "$qa_app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?><plist version="1.0"><dict>
<key>CFBundleName</key><string>DemoMux ${kind} QA</string>
<key>CFBundleIdentifier</key><string>app.demomux.qa.${kind}</string>
<key>CFBundleExecutable</key><string>DemoMuxQA</string>
<key>CFBundlePackageType</key><string>APPL</string>
</dict></plist>
PLIST
    codesign --force --sign - "$qa_app/Contents/Helpers/DemoMuxSupervisor"
    codesign --force --sign - "$qa_app"
    printf '%s\n' "$qa_app"
done
