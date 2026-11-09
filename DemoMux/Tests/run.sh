#!/bin/bash
set -euo pipefail
source_dir="$(cd -- "$(dirname -- "$0")/.." && pwd)"
if /usr/sbin/lsof -nP -iTCP:8081 -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Exit DemoMux and release loopback port 8081 before running regression checks." >&2
    exit 2
fi
test_dir="$(mktemp -d /tmp/demomux-regression.XXXXXXXX)"
trap '/usr/bin/find "$test_dir" -depth -delete' EXIT
swift_flags=(-O -warnings-as-errors -target arm64-apple-macos26.0)
core=("$source_dir/Runtime.swift" "$source_dir/Projects.swift")
/usr/bin/swiftc "${swift_flags[@]}" "${core[@]}" "$source_dir/Gateway.swift" \
    "$source_dir/Tests/RoutingSmoke.swift" -o "$test_dir/routing"
"$test_dir/routing"
/usr/bin/swiftc "${swift_flags[@]}" "${core[@]}" "$source_dir/Gateway.swift" \
    "$source_dir/Tests/PublicProbeSmoke.swift" -o "$test_dir/probe"
"$test_dir/probe" "$source_dir/Tests/SlowHTTPFixture.py"
/usr/bin/swiftc "${swift_flags[@]}" "${core[@]}" "$source_dir/Gateway.swift" "$source_dir/Accounts.swift" \
    "$source_dir/Verification.swift" "$source_dir/Tests/VerificationSmoke.swift" -o "$test_dir/verification"
"$test_dir/verification" "$source_dir/../Shared/Icons"
/usr/bin/swiftc "${swift_flags[@]}" "${core[@]}" "$source_dir/Accounts.swift" \
    "$source_dir/Tests/AccountsSmoke.swift" -o "$test_dir/accounts"
"$test_dir/accounts"
python3 - "$source_dir/main.swift" "$test_dir/Controller.swift" <<'PY2'
from pathlib import Path
import sys
Path(sys.argv[2]).write_text(Path(sys.argv[1]).read_text().rsplit("MainActor.assumeIsolated {", 1)[0])
PY2
/usr/bin/swiftc "${swift_flags[@]}" "${core[@]}" "$source_dir/Gateway.swift" "$source_dir/Accounts.swift" \
    "$source_dir/Verification.swift" "$source_dir/NativeControls.swift" "$source_dir/SetupInterface.swift" \
    "$source_dir/AccountController.swift" "$source_dir/AccountInterface.swift" "$source_dir/Interface.swift" \
    "$test_dir/Controller.swift" "$source_dir/Tests/AccountLayoutSmoke.swift" -o "$test_dir/layout"
"$test_dir/layout"
/usr/bin/clang -O2 -Wall -Wextra -Werror -target arm64-apple-macos26.0 \
    "$source_dir/Supervisor.c" -o "$test_dir/supervisor"
python3 "$source_dir/Tests/SupervisorSmoke.py" "$test_dir/supervisor"
