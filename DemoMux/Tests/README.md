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
