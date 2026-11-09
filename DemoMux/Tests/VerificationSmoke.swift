import Foundation

actor SynchronizationFixture {
    var reads = 0
    let changeIdentity: Bool
    init(changeIdentity: Bool = false) { self.changeIdentity = changeIdentity }
    func execute(_ arguments: [String]) throws -> String {
        if arguments.first == "switch" {
            let id = changeIdentity && reads > 1 ? "changed" : "fixture"
            return "[{\"id\":\"\(id)\",\"nickname\":\"Demo\",\"tailnet\":\"fixture.ts.net\",\"account\":\"demo@example.test\",\"selected\":true}]"
        }
        reads += 1
        if reads == 1 { return #"{"BackendState":"NoState"}"# }
        return #"{"BackendState":"Running","Self":{"DNSName":"device.fixture.ts.net.","UserID":42},"User":{"42":{"DisplayName":"Demo","LoginName":"demo@example.test"}}}"#
    }
}

@main
struct VerificationSmoke {
    static func main() async throws {
        let startupLogs = "{\"level\":\"info\",\"msg\":\"loading\"}\n{\"level\":\"error\",\"msg\":\"failed\",\"error\":\"socket bind failed\"}\n"
        precondition(componentFailure(startupLogs) == "socket bind failed")
        var attempts = 0
        var progress: [VerificationProgress] = []
        var delays: [TimeInterval] = []
        try await VerificationRetry.run(check: {
            attempts += 1
            if attempts < 5 { throw PublicCheckError.connection }
        }, report: { progress.append($0) }, sleep: { delays.append($0) })
        precondition(attempts == 5 && delays.count == 2 + 4 + 8 + 15)
        precondition(progress.filter { $0.retryInSeconds == nil }.map(\.attempt) == [1, 2, 3, 4, 5])
        precondition(progress.filter { $0.attempt == 3 }.compactMap(\.retryInSeconds) == [8, 7, 6, 5, 4, 3, 2, 1])
        for error in [PublicCheckError.tls, .marker, .redirect, .page(404), .page(503)] {
            var calls = 0
            do {
                try await VerificationRetry.run(check: { calls += 1; throw error }, report: { _ in },
                                                sleep: { _ in fatalError("Fatal errors must not auto-retry") })
                fatalError("Expected fatal validation error")
            } catch is PublicCheckError { precondition(calls == 1) }
        }
        let pending = Task {
            try await VerificationRetry.run(check: { throw PublicCheckError.timeout }, report: { _ in })
        }
        try await Task.sleep(for: .milliseconds(100))
        let cancelStart = Date()
        pending.cancel()
        do { try await pending.value; fatalError("Expected stopped retry") } catch is CancellationError { }
        precondition(Date().timeIntervalSince(cancelStart) < 1)
        let command = Task { try await asyncCommand(URL(fileURLWithPath: "/bin/sleep"), ["30"]) }
        try await Task.sleep(for: .milliseconds(100))
        command.cancel()
        do { _ = try await command.value; fatalError("Expected stopped CLI") } catch is CancellationError { }
        precondition(Date().timeIntervalSince(cancelStart) < 2)

        let startup = SynchronizationFixture()
        let syncClient = AccountClient { try await startup.execute($0) }
        let synchronized = try await syncClient.synchronizedSnapshot(expected: "fixture", attempts: 3, sleep: { _ in })
        try synchronized.requireSynchronized("fixture")
        let changed = SynchronizationFixture(changeIdentity: true)
        let changedClient = AccountClient { try await changed.execute($0) }
        do {
            _ = try await changedClient.synchronizedSnapshot(expected: "fixture", attempts: 3, sleep: { _ in })
            fatalError("Must stop when identity changes during startup")
        } catch is AccountIdentityError { }
        var setup = SetupProgress()
        setup.begin(.publicEntry)
        precondition(!setup.mayVerify && setup[.account] == .pending)
        for stage in SetupStage.allCases where stage != .publicEntry { setup.complete(stage) }
        precondition(setup.mayVerify)
        setup.pause()
        precondition(setup[.publicEntry] == .paused)
        setup.begin(.account)
        precondition(!setup.mayVerify && setup[.funnel] == .pending)

        let profiles = #"[{"id":"fixture","nickname":"Demo","tailnet":"fixture.ts.net","account":"demo@example.test","selected":true}]"#
        let status = #"{"BackendState":"Running","Self":{"DNSName":"device.fixture.ts.net.","UserID":42},"User":{"42":{"DisplayName":"Demo","LoginName":"demo@example.test"}}}"#
        let account = try AccountSnapshot.parse(profiles: profiles, status: status)
        try account.requireSynchronized("fixture", host: "device.fixture.ts.net")
        precondition((try? account.requireSynchronized(nil)) == nil)
        precondition((try? account.requireSynchronized("other")) == nil)
        precondition((try? account.requireSynchronized("fixture", host: "other.fixture.ts.net")) == nil)
        let unsynced = try AccountSnapshot.parse(profiles: profiles, status: #"{"BackendState":"Running"}"#)
        precondition(unsynced.online && (try? unsynced.requireSynchronized("fixture")) == nil)
        precondition((try? account.disconnected.requireSynchronized("fixture")) == nil)
        let legacy = #"{"profiles":[],"backendState":"Stopped","displayName":"Old cache"}"#.data(using: .utf8)!
        let oldCache = try JSONDecoder().decode(AccountSnapshot.self, from: legacy)
        precondition(oldCache.principalConfirmed == false)
        let identityChange = Task {
            try await VerificationRetry.run(check: {
                try account.requireSynchronized("fixture")
                try account.requireSynchronized("changed-account")
            }, report: { _ in }, sleep: { _ in fatalError("Identity errors cannot retry") })
        }
        do { try await identityChange.value; fatalError("Expected changed identity") } catch is AccountIdentityError { }

        let icons = URL(fileURLWithPath: CommandLine.arguments[1])
        let manifest = try JSONSerialization.jsonObject(with: Data(contentsOf: icons.appendingPathComponent("source.json"))) as! [String: Any]
        for icon in manifest["icons"] as! [[String: String]] {
            let digest = try fileDigest(icons.appendingPathComponent("lucide/" + icon["file"]!))
            precondition(digest == icon["sha256"]!)
        }
        precondition(fileManager.fileExists(atPath: icons.appendingPathComponent("lucide/LICENSE").path))
        print("Passed: 2/4/8/15 retry countdown, fatal-error policy, stop retry, CLI cancellation, factual five-step states, fresh identity/host gates, legacy cache and shared SVG checksums")
    }
}
