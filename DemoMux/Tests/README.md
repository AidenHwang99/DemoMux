# DemoMux macOS regression checks

These are developer checks. DemoMux users do not need Swift, Python, or Node.js.

First open the packaged app once so that its pinned components are downloaded. Then quit DemoMux to release port 8081 before running the routing test. The tests create their own synthetic content and loopback backend; they do not log in or publish real projects.

From the DemoMux source directory:

```sh
bash Tests/run.sh
```

Or run the individual model checks:

```sh
qa_dir=$(mktemp -d /tmp/demomux-regression.XXXXXXXX)
swiftc -O -target arm64-apple-macos26.0 Runtime.swift Projects.swift Gateway.swift Tests/RoutingSmoke.swift -o "$qa_dir/routing"
"$qa_dir/routing"
swiftc -O -target arm64-apple-macos26.0 Runtime.swift Projects.swift Accounts.swift Tests/AccountsSmoke.swift -o "$qa_dir/accounts"
"$qa_dir/accounts"
python3 Tests/SupervisorSmoke.py /Applications/DemoMux.app/Contents/Helpers/DemoMuxSupervisor
```

The routing test covers private previews, the three-slot limit, path and root routes, nested static assets, snapshot reload, withdrawn paths, source loss, root reuse, HTTPS forwarding headers, redirect validation, and closed-port detection.

It also verifies that publishing takes over the public guide entry, guide content is withdrawn while a project is published, the home target can change without consuming slots, explicit root ownership is preserved, and only the exact known homepage redirect is accepted.

The supervisor test terminates only its own synthetic parent and component tree, using both SIGKILL and SIGTERM. It checks that a grandchild also exits and that normal component exit codes propagate.

The account test uses synthetic daemon responses to cover same-email accounts in different tailnets, expired keys, logout, invalid JSON, cancellation, identity changes during status reads, scoped root-link history, and migration from the 0.2 catalog. It does not use real credentials or revoke a real login.

The offscreen AppKit test creates hidden views with synthetic account data. Its temporary test build injects a delayed account lookup without changing production code or reading real accounts. It checks card/button geometry at the minimum and default panel sizes, the single inline confirmation, and refusal to log out a different identity after confirmation. It does not show windows or replace unlocked-desktop visual and interaction acceptance.

Real browser interaction, authentication, component downloads, and public Funnel verification remain separate acceptance steps; these tests do not claim to replace those checks.

## Public verification regressions (macOS 0.3.2)

The probe test uses a loopback-only Python HTTP fixture to simulate a slow 1.4 MB response which ignores Range. It checks that validation completes after final response headers, without waiting for the body; rejects partial headers and stale markers; handles 1xx responses; distinguishes connection, timeout, handshake and certificate failures; and promptly cancels owned curl processes.

The AppKit state test also verifies that project errors do not overwrite account messages, old onboarding details stay out of project progress, failed public checks have a distinct state, and a retry clears old errors before the delayed identity lookup. Cancelling that lookup releases its pending publication state.

These fixture tests are not evidence of Internet access. For this release, separate public DNS/HTTPS marker checks, a full synthetic HTML download, browser rendering, a port publication, and failure/retry flows were performed on the development Mac. A second clean Mac, mobile-network device, reboot and real second-account validation remain outside this round. See [the macOS release notes](../CHANGELOG.md).

## Windows parity regressions (macOS 0.3.3)

`VerificationSmoke.swift` checks the 2/4/8/15-second policy, countdown events, fatal certificate/marker/redirect/HTTP failures, cancellation, five factual setup states, synchronized identity/hostname gates, legacy caches and all shared SVG source hashes. Account commands are also cancelled while running.

The AppKit test checks fixed 36-point SVG buttons in five positions across stopped, running, published and verifying states; disabled copy keeps its slot. It verifies that cancelling setup verification stops its synthetic Funnel child, preserves the private preview process and marks the step paused. It also checks stable activity-view identity during countdown text updates.

For **actual unlocked desktop inspection** build the isolated fixtures:

```sh
bash DemoMux/Tests/build-interaction.sh
```

Open either generated App path in Finder. Layout QA uses only synthetic profiles/projects. Retry QA displays unexecuted prerequisites honestly and simulates a timeout; stop/continue work through the retry engine, and Cmd+R simulates recovery. Its support path is replaced only in the temporary test source, and it uses injected account/probe dependencies. It does not load real account caches, log in or connect to public ingress. These fixture Apps are not included in the distribution ZIP.

See [Windows-to-Mac parity](../WINDOWS-PARITY.md). A rendered fixture or offscreen test is not evidence that the installed app authenticated or reached Funnel from an external device.

## Responsive layout regressions (macOS 0.3.4)

The native layout checks resize both pages through 810/920/1000/1440/1920 points and back. They verify the bounded centered canvas, a separate footer/scroll viewport, section breakpoints, compact project actions, untruncated long diagnostics and preserved retry/activity objects. Native QA Apps additionally offer Cmd+1 (small), Cmd+2 (default) and Cmd+3 (wide) for visible breakpoint testing; these shortcuts are fixture-only and are absent from the shipped App.

## Settings and uninstall checks (macOS 0.3.4)

`MaintenanceSmoke.swift` uses a temporary home, mock app and injected recoverable trash directory. It covers full and keep-data scope, old component versions, original-project/system preservation, permission and preference failures, rollback conflicts, app identity, symlink boundaries and storage sizing. Settings geometry checks cover 640/720/1200 points, preference persistence, diagnostic redaction and the single uninstall review. The native QA host uses its own bundle identity, isolated home and preference suite; confirming uninstall moves only that running QA bundle and synthetic data into the actual macOS Trash. Its parent directory receives `uninstall-result.json` before the QA app exits. Never point a test context at the installed app or a real home.
