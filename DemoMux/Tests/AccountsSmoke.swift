import Foundation

actor FakeAccountDaemon {
    var selected = "profile-a"
    var expired = false
    var calls: [[String]] = []
    var badSchema = false
    var wrongSwitch = false
    var changeDuringRead = false
    var reads = 0
    func execute(_ arguments: [String]) throws -> String {
        calls.append(arguments)
        if arguments == ["switch", "--list", "--json"] {
            reads += 1
            if changeDuringRead && reads > 1 { selected = "profile-b" }
            if badSchema { return "not json" }
            return """
            [{"id":"profile-a","nickname":"Personal","tailnet":"personal.ts.net","account":"same@example.test","selected":\(selected == "profile-a")},
             {"id":"profile-b","nickname":"Team","tailnet":"team.ts.net","account":"same@example.test","selected":\(selected == "profile-b")}]
            """
        }
        if arguments == ["status", "--json", "--peers=false"] {
            return """
            {"BackendState":"\(expired ? "NeedsLogin" : "Running")","Self":{"UserID":42},"User":{"42":{"DisplayName":"Test User"}}}
            """
        }
        if arguments.first == "switch", arguments.count == 2 {
            if !wrongSwitch { selected = arguments[1] }
            return "Success"
        }
        if arguments == ["logout"] { expired = true; return "" }
        throw AppError("Unexpected mock command")
    }
    func setExpired() { expired = true }
    func setBadSchema() { badSchema = true }
    func setWrongSwitch() { wrongSwitch = true }
    func setChangeDuringRead() { changeDuringRead = true }
    func log() -> [[String]] { calls }
}

@main
enum AccountsSmoke {
    static func main() async throws {
        let fake = FakeAccountDaemon()
        let client = AccountClient(execute: { try await fake.execute($0) })
        let initial = try await client.snapshot()
        precondition(initial.current?.id == "profile-a" && initial.online)
        precondition(initial.displayName == "Test User")
        precondition(!initial.disconnected.online)
        let changed = try await client.select("profile-b")
        try changed.requireOnline("profile-b")
        precondition(changed.current?.tailnet == "team.ts.net")
        do {
            try changed.requireOnline("profile-a")
            preconditionFailure("wrong account accepted")
        } catch is AccountIdentityError {}
        do {
            _ = try await client.select("missing-profile")
            preconditionFailure("missing account accepted")
        } catch {}
        await fake.setExpired()
        let expired = try await client.select("profile-a")
        precondition(!expired.online) // A successful CLI exit does not prove authentication.
        do {
            try expired.requireOnline("profile-a")
            preconditionFailure("expired key accepted")
        } catch {}
        try await client.logout()
        let log = await fake.log()
        precondition(log.contains(["logout"]))
        precondition(!log.contains(["switch", "same@example.test"])) // IDs, not ambiguous email.
        await fake.setBadSchema()
        do {
            _ = try await client.snapshot()
            preconditionFailure("bad JSON accepted")
        } catch {}
        let wrong = FakeAccountDaemon()
        await wrong.setWrongSwitch()
        let wrongClient = AccountClient(execute: { try await wrong.execute($0) })
        do {
            _ = try await wrongClient.select("profile-b")
            preconditionFailure("wrong profile accepted")
        } catch is AccountIdentityError {}
        let racing = FakeAccountDaemon()
        await racing.setChangeDuringRead()
        let racingClient = AccountClient(execute: { try await racing.execute($0) })
        do {
            _ = try await racingClient.snapshot()
            preconditionFailure("interleaved identity accepted")
        } catch is AccountIdentityError {}
        let profileText = """
        [{"id":"profile-a","nickname":"","tailnet":"personal.ts.net","account":"same@example.test","selected":true}]
        """
        do {
            _ = try AccountSnapshot.parse(profiles: profileText, status:
                #"{"BackendState":"Running","Self":{"UserID":42},"User":{"42":{"LoginName":"other@example.test"}}}"#)
            preconditionFailure("different principal accepted")
        } catch is AccountIdentityError {}
        let cancelled = Task {
            try Task.checkCancellation()
            _ = try await client.snapshot()
        }
        cancelled.cancel()
        do { try await cancelled.value; preconditionFailure("cancellation ignored") }
        catch is CancellationError {}
        var catalog = Catalog()
        let project = catalog.add(.port(5173), name: "Shared project")
        catalog.previousRootOwners[8443] = project.id
        precondition(catalog.reusesRoot(8443, project: project.id, profile: "profile-a", host: "a.ts.net"))
        catalog.rememberRoot(8443, project: project.id, profile: "profile-a", host: "a.ts.net")
        precondition(!catalog.reusesRoot(8443, project: project.id, profile: "profile-b", host: "b.ts.net"))
        precondition(catalog.reusesRoot(8443, project: UUID(), profile: "profile-b", host: "a.ts.net"))
        precondition(!catalog.reusesRoot(8443, project: project.id, profile: "profile-a", host: "a.ts.net"))
        precondition(catalog.reusesRoot(8443, project: UUID(), profile: "profile-a", host: "a.ts.net"))
        let legacy = """
        {"projects":[],"previousRootOwners":{"8443":"\(project.id.uuidString)"}}
        """
        let old = try JSONDecoder().decode(Catalog.self, from: Data(legacy.utf8))
        precondition(old.accountRootOwners.isEmpty && old.previousRootOwners[8443] == project.id)
        let roundtrip = try JSONDecoder().decode(Catalog.self, from: JSONEncoder().encode(catalog))
        precondition(roundtrip.projects.first?.id == project.id && !roundtrip.accountRootOwners.isEmpty)
        print("Passed: identity display, same-email accounts, switching, expired auth, logout, cancellation, invalid schema, wrong identity rejection, scoped history and legacy migration.")
    }
}
