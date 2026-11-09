import Foundation

struct AccountProfile: Codable, Equatable, Identifiable, Sendable {
    let id: String
    let nickname: String
    let tailnet: String
    let account: String
    var selected: Bool

    var title: String { nickname.isEmpty || nickname == account ? account : nickname }
}

struct AccountIdentityError: LocalizedError {
    var errorDescription: String? { "登录身份已改变，已停止本次分享。请确认账号后重新启动 Funnel。" }
}

struct AccountNotReadyError: LocalizedError {
    var errorDescription: String? { "账号身份尚未同步。请在「管理账号」恢复连接或刷新状态，确认设备已获授权后继续；尚未进入公网校验。" }
}

struct AccountSnapshot: Codable, Equatable, Sendable {
    var profiles: [AccountProfile] = []
    var backendState = "Stopped"
    var displayName = ""
    var dnsName = ""
    var principalConfirmed = false
    enum CodingKeys: String, CodingKey { case profiles, backendState, displayName, dnsName, principalConfirmed }
    init(profiles: [AccountProfile] = [], backendState: String = "Stopped", displayName: String = "",
         dnsName: String = "", principalConfirmed: Bool = false) {
        self.profiles = profiles; self.backendState = backendState; self.displayName = displayName
        self.dnsName = dnsName; self.principalConfirmed = principalConfirmed
    }
    init(from decoder: Decoder) throws {
        let value = try decoder.container(keyedBy: CodingKeys.self)
        profiles = try value.decode([AccountProfile].self, forKey: .profiles)
        backendState = try value.decodeIfPresent(String.self, forKey: .backendState) ?? "Stopped"
        displayName = try value.decodeIfPresent(String.self, forKey: .displayName) ?? ""
        dnsName = try value.decodeIfPresent(String.self, forKey: .dnsName) ?? ""
        principalConfirmed = try value.decodeIfPresent(Bool.self, forKey: .principalConfirmed) ?? false
    }
    var current: AccountProfile? { profiles.first(where: \.selected) }
    var online: Bool { backendState == "Running" && current != nil }

    static func parse(profiles: String, status: String) throws -> Self {
        guard let list = profiles.data(using: .utf8), let state = status.data(using: .utf8) else {
            throw AppError("无法读取 Tailscale 账号信息。请重试。")
        }
        let decoded: [AccountProfile]
        do { decoded = try JSONDecoder().decode([AccountProfile].self, from: list) }
        catch { throw AppError("Tailscale 账号列表格式不兼容，请重新安装 DemoMux 指定的组件版本。") }
        guard Set(decoded.map(\.id)).count == decoded.count,
              decoded.allSatisfy({ !$0.id.isEmpty }),
              decoded.filter(\.selected).count <= 1,
              let object = try JSONSerialization.jsonObject(with: state) as? [String: Any],
              let backend = object["BackendState"] as? String else {
            throw AppError("Tailscale 账号状态无效，请刷新后重试。")
        }
        var result = Self(profiles: decoded, backendState: backend)
        if let own = object["Self"] as? [String: Any] {
            result.dnsName = (own["DNSName"] as? String ?? "").trimmingCharacters(in: CharacterSet(charactersIn: ".")).lowercased()
        }
        if let own = object["Self"] as? [String: Any], let id = own["UserID"],
           let users = object["User"] as? [String: Any],
           let user = users[String(describing: id)] as? [String: Any] {
            result.displayName = user["DisplayName"] as? String ?? ""
            if let principal = user["LoginName"] as? String, let expected = result.current?.account {
                result.principalConfirmed = !principal.isEmpty && principal.caseInsensitiveCompare(expected) == .orderedSame
            }
            if backend == "Running", let principal = user["LoginName"] as? String,
               let expected = result.current?.account, !principal.isEmpty, !expected.isEmpty,
               principal.caseInsensitiveCompare(expected) != .orderedSame { throw AccountIdentityError() }
        }
        return result
    }

    func requireOnline(_ expected: String) throws {
        guard current?.id == expected else { throw AccountIdentityError() }
        guard online else { throw AppError("该账号尚未连接。请重新登录，或等待管理员批准设备。") }
    }

    func requireSynchronized(_ expected: String?, host: String? = nil) throws {
        guard let expected, !expected.isEmpty else { throw AccountNotReadyError() }
        try requireOnline(expected)
        guard principalConfirmed, let url = URL(string: "https://" + dnsName),
              url.host == dnsName, dnsName.hasSuffix(".ts.net"), url.user == nil, url.password == nil else {
            throw AccountNotReadyError()
        }
        if let host, dnsName != host.lowercased() { throw AccountIdentityError() }
    }

    var disconnected: Self {
        var value = self
        value.backendState = "Stopped"
        value.principalConfirmed = false
        value.dnsName = ""
        return value
    }
}

struct AccountClient: Sendable {
    let execute: @Sendable ([String]) async throws -> String

    init(socket: URL) {
        let binary = Dependency.tailscale.executable("tailscale")
        execute = { arguments in
            try Task.checkCancellation()
            return try await asyncCommand(binary, ["--socket=\(socket.path)"] + arguments, timeout: 20)
        }
    }

    init(execute: @escaping @Sendable ([String]) async throws -> String) { self.execute = execute }

    func snapshot() async throws -> AccountSnapshot {
        let profiles = try await execute(["switch", "--list", "--json"])
        let status = try await execute(["status", "--json", "--peers=false"])
        let settledProfiles = try await execute(["switch", "--list", "--json"])
        try Task.checkCancellation()
        let before = try AccountSnapshot.parse(profiles: profiles, status: status)
        let after = try AccountSnapshot.parse(profiles: settledProfiles, status: status)
        guard before.current?.id == after.current?.id else { throw AccountIdentityError() }
        return after
    }

    func synchronizedSnapshot(expected: String?, attempts: Int = 40,
                              sleep: (TimeInterval) async throws -> Void = { try await Task.sleep(for: .seconds($0)) }) async throws -> AccountSnapshot {
        var identity = expected
        for _ in 0..<attempts {
            try Task.checkCancellation()
            let value = try await snapshot()
            if identity == nil { identity = value.current?.id }
            if let identity, value.current?.id != identity { throw AccountIdentityError() }
            do { try value.requireSynchronized(identity); return value }
            catch is AccountIdentityError { throw AccountIdentityError() }
            catch {
                guard ["Running", "Starting", "NoState"].contains(value.backendState) else {
                    throw AppError("账号尚未连接。请恢复连接，或在浏览器完成登录与设备审批后重试。")
                }
            }
            try await sleep(0.5)
        }
        throw AccountNotReadyError()
    }

    func select(_ id: String) async throws -> AccountSnapshot {
        let before = try await snapshot()
        guard before.profiles.contains(where: { $0.id == id }) else {
            throw AppError("这个账号已不在 DemoMux 的登录列表中，请添加账号重新登录。")
        }
        _ = try await execute(["switch", id])
        let after = try await snapshot()
        guard after.current?.id == id else { throw AccountIdentityError() }
        return after
    }

    func logout() async throws { _ = try await execute(["logout"]) }
}

enum AccountAction: Equatable {
    case switchTo(String), add, reconnect, logout
}

enum AccountCache {
    static var file: URL { support.appendingPathComponent("Accounts.json") }
    static func load() -> AccountSnapshot {
        guard let data = try? Data(contentsOf: file),
              let value = try? JSONDecoder().decode(AccountSnapshot.self, from: data) else { return .init() }
        return value.disconnected
    }
    static func save(_ snapshot: AccountSnapshot) throws {
        try JSONEncoder().encode(snapshot.disconnected).write(to: file, options: .atomic)
        try fileManager.setAttributes([.posixPermissions: 0o600], ofItemAtPath: file.path)
    }
}
