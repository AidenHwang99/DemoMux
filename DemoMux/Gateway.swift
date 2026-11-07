import Darwin
import Foundation

let guidePath = "/.demomux/"
let verificationHeader = "X-DemoMux-Verification"

struct GatewayConfiguration {
    let admin: URL
    let guide: URL
    let marker: String
    let localPort: Int
    let roots: [Int: Int]
    let projects: [Project]
    let active: [UUID: Content]
    let publications: PublicationSlots
    var publicHost: String? = nil

    func data() throws -> Data {
        var servers: [String: [String: Any]] = [:]
        let guideContent = Content.files(guide, "index.html")
        let home = projects.first { $0.id == publications.homeProjectID && active[$0.id] != nil }
        let homeShare = home.flatMap { publications.shares[$0.id] }
        let homeTarget: String?
        if let home, let homeShare {
            switch homeShare.publication {
            case .path: homeTarget = home.path
            case .root(443): homeTarget = "/"
            case .root:
                guard let publicHost else { throw AppError("公网首页缺少已验证的域名。") }
                homeTarget = homeShare.url(host: publicHost, project: home).absoluteString
            }
        } else { homeTarget = nil }
        let publicGuideAllowed = publications.shares.isEmpty
        let primaryGuide = publicGuideAllowed
            ? routes(for: guideContent, prefix: guidePath, marker: marker)
            : homeTarget.map { homeRedirect($0, paths: [String(guidePath.dropLast()), guidePath + "*"], marker: homeShare!.marker) }
                ?? [route([String(guidePath.dropLast()), guidePath + "*"], [["handler": "static_response", "status_code": 404]])]
        var primary = primaryGuide

        var local: [[String: Any]] = []
        for project in projects {
            guard let content = active[project.id] else { continue }
            if case .files = content {
                local += routes(for: content, prefix: project.localPath)
            }
            guard let share = publications.shares[project.id] else { continue }
            switch share.publication {
            case .path:
                primary += routes(for: content, prefix: project.path, marker: share.marker)
            case .root(let port) where port != 443:
                guard let listener = roots[port] else {
                    throw AppError("公网入口 \(port) 缺少本机转发端口。")
                }
                servers["root-\(port)"] = server(listener, routes(for: content, prefix: "/", marker: share.marker))
            case .root:
                break
            }
        }

        // A stopped or unknown project path must not fall through to a root project.
        primary.append(route(["/p", "/p/*", "/.demomux/*"], [["handler": "static_response", "status_code": 404]]))
        if let rootProject = projects.first(where: { publications.shares[$0.id]?.publication == .root(443) }),
           let content = active[rootProject.id], let share = publications.shares[rootProject.id] {
            primary += routes(for: content, prefix: "/", marker: share.marker)
        } else if let homeTarget, let homeShare {
            primary += homeRedirect(homeTarget, paths: nil, marker: homeShare.marker)
        } else if publicGuideAllowed {
            primary += routes(for: guideContent, prefix: "/", marker: marker)
        } else {
            primary.append(route(nil, [["handler": "static_response", "status_code": 404]]))
        }
        servers["primary"] = server(previewPort, primary)
        local.append(route(nil, [["handler": "static_response", "status_code": 404]]))
        servers["local-only"] = server(localPort, local)
        return try JSONSerialization.data(withJSONObject: [
            "admin": ["listen": "unix/\(admin.path)", "config": ["persist": false]],
            "apps": ["http": ["servers": servers]],
        ], options: [.sortedKeys])
    }

    private func server(_ port: Int, _ routes: [[String: Any]]) -> [String: Any] {
        ["listen": ["127.0.0.1:\(port)"], "routes": routes]
    }

    private func route(_ paths: [String]?, _ handlers: [[String: Any]]) -> [String: Any] {
        var result: [String: Any] = ["handle": handlers, "terminal": true]
        if let paths { result["match"] = [["path": paths]] }
        return result
    }

    private func routes(for content: Content, prefix: String, marker: String? = nil) -> [[String: Any]] {
        var result: [[String: Any]] = []
        var headers: [String: [String]] = ["Cache-Control": ["no-store"]]
        if let marker { headers[verificationHeader] = [marker] }
        let header: [String: Any] = ["handler": "headers", "response": ["deferred": true, "set": headers]]
        let root = prefix == "/"
        let trimmed = String(prefix.dropLast())

        if !root {
            result.append(route([trimmed], [header, redirect(prefix)]))
        }
        var handlers = [header]
        switch content {
        case .files(let directory, let entry):
            if entry != "index.html" {
                var location = URLComponents()
                location.path = prefix + entry
                result.append(route([prefix], [header, redirect(location.percentEncodedPath)]))
            }
            if !root { handlers.append(["handler": "rewrite", "strip_path_prefix": trimmed]) }
            handlers.append(["handler": "file_server", "root": directory.path])
        case .proxy(let port):
            handlers.append([
                "handler": "reverse_proxy",
                "upstreams": [["dial": "127.0.0.1:\(port)"]],
                "headers": ["request": ["set": [
                    "Host": ["127.0.0.1:\(port)"],
                    "X-Forwarded-Proto": ["https"],
                ]]],
            ])
        }
        result.append(route(root ? nil : [prefix + "*"], handlers))
        return result
    }

    private func redirect(_ location: String) -> [String: Any] {
        ["handler": "static_response", "status_code": 307, "headers": ["Location": [location]]]
    }

    private func homeRedirect(_ location: String, paths: [String]?, marker: String) -> [[String: Any]] {
        [route(paths, [
            ["handler": "headers", "response": ["deferred": true,
                "set": [verificationHeader: [marker], "Cache-Control": ["no-store"]]]],
            redirect(location),
        ])]
    }
}

enum PublicCheckError: LocalizedError {
    case dns, connection, tls, handshake, timeout, page(Int), marker, redirect

    var errorDescription: String? {
        switch self {
        case .dns: return "公共 DNS 尚未返回入口。Funnel 首次发布可能需要数分钟，请等待或重试。"
        case .connection: return "无法连接公网入口。请检查网络是否可访问 Tailscale Funnel，再重试。"
        case .tls: return "公网证书校验未通过。请检查系统时间、网络代理及 Funnel 证书状态后重试；应用不会绕过证书校验。"
        case .handshake: return "公网 HTTPS 握手中断，尚不能判断为证书问题。请检查网络或代理后重试。"
        case .timeout: return "公网入口响应超时。请检查网络或代理后重试；本机预览不受影响。"
        case .page(let code): return "公网入口返回 HTTP \(code)。请检查项目首页和转发配置。"
        case .marker: return "公网入口未返回本次发布标记。请确认域名已指向这台设备，再重试。"
        case .redirect: return "项目跳转到了公网入口以外。请检查应用的基础路径和对外地址，或改用独立入口。"
        }
    }

    var canPropagate: Bool {
        switch self {
        case .dns, .connection, .tls, .handshake, .timeout, .marker: return true
        case .page(let code): return [404, 421, 502, 503, 504].contains(code)
        case .redirect: return false
        }
    }
}

func publicTransportFailure(_ code: Int32) -> PublicCheckError {
    switch code {
    case 6: return .dns
    case 28: return .timeout
    case 35: return .handshake
    case 51, 58, 60, 77, 83, 90, 91: return .tls
    default: return .connection
    }
}

// Inspect a completed final header block, never a partial block or a 1xx response.
// GET is used because some development servers do not implement HEAD. The body
// is deliberately discarded and the owned curl exits once headers are verified.
func completedPublicHeaders(_ output: String) -> String? {
    let blocks = output.replacingOccurrences(of: "\r\n", with: "\n").components(separatedBy: "\n\n")
    for block in blocks.dropLast() {
        let first = block.split(separator: "\n").first?.split(separator: " ") ?? []
        guard first.count >= 2, first[0].hasPrefix("HTTP/"), let status = Int(first[1]), status >= 200 else { continue }
        return block + "\n\n\(status)"
    }
    return nil
}

func publicHeaderRequest(_ arguments: [String], timeout: TimeInterval = 12) async throws -> String {
    let child = try Child(URL(fileURLWithPath: "/usr/bin/curl"), arguments)
    defer { child.stop() }
    let end = Date().addingTimeInterval(timeout)
    while true {
        try Task.checkCancellation()
        if let headers = completedPublicHeaders(child.output) { return headers }
        if !child.running {
            // Drain the final pipe callback before classifying the exit status.
            try await Task.sleep(for: .milliseconds(50))
            if let headers = completedPublicHeaders(child.output) { return headers }
            throw publicTransportFailure(child.process.terminationStatus)
        }
        guard Date() < end else { throw PublicCheckError.timeout }
        try await Task.sleep(for: .milliseconds(25))
    }
}

private struct DNSAnswer: Decodable {
    let type: Int
    let data: String
}
private struct DNSResponse: Decodable {
    let Answer: [DNSAnswer]?
}

private func publicAddress(_ text: String, type: Int) -> Bool {
    if type == 1 {
        var address = in_addr()
        guard inet_pton(AF_INET, text, &address) == 1 else { return false }
        let bytes = text.split(separator: ".").compactMap { Int($0) }
        guard bytes.count == 4 else { return false }
        let (a, b, c) = (bytes[0], bytes[1], bytes[2])
        return a > 0 && a < 224 && a != 10 && a != 127 &&
            !(a == 100 && (64...127).contains(b)) && !(a == 169 && b == 254) &&
            !(a == 172 && (16...31).contains(b)) && !(a == 192 && [0, 168].contains(b)) &&
            !(a == 198 && [18, 19].contains(b)) && !(a == 198 && b == 51 && c == 100) &&
            !(a == 203 && b == 0 && c == 113)
    }
    guard type == 28 else { return false }
    var address = in6_addr()
    guard inet_pton(AF_INET6, text, &address) == 1 else { return false }
    return withUnsafeBytes(of: &address) { $0[0] & 0xe0 == 0x20 }
}

private func resolve(_ host: String, record: String) async -> [String] {
    for resolver in ["https://cloudflare-dns.com/dns-query", "https://dns.google/resolve"] {
        var parts = URLComponents(string: resolver)!
        parts.queryItems = [URLQueryItem(name: "name", value: host), URLQueryItem(name: "type", value: record)]
        var request = URLRequest(url: parts.url!, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 5)
        request.setValue("application/dns-json", forHTTPHeaderField: "Accept")
        if let (data, response) = try? await URLSession.shared.data(for: request),
           (response as? HTTPURLResponse)?.statusCode == 200,
           let answer = try? JSONDecoder().decode(DNSResponse.self, from: data) {
            let records = (answer.Answer ?? []).filter { publicAddress($0.data, type: $0.type) }.map(\.data)
            if !records.isEmpty { return records }
        }
    }
    return []
}

func validatePublic(_ url: URL, marker: String, allowRedirect: Bool, expectedRedirect: URL? = nil) async throws {
    guard url.scheme == "https", let host = url.host, host.hasSuffix(".ts.net"),
          url.user == nil, url.password == nil else {
        throw AppError("Funnel 返回了无效的公网地址。")
    }
    async let ipv4 = resolve(host, record: "A")
    async let ipv6 = resolve(host, record: "AAAA")
    let addresses = await ipv4 + ipv6
    try Task.checkCancellation()
    guard !addresses.isEmpty else { throw PublicCheckError.dns }
    let failure = await withTaskGroup(of: PublicCheckError?.self) { group in
        for address in addresses.prefix(4) {
            group.addTask {
                let pinned = address.contains(":") ? "[\(address)]" : address
                let port = url.port ?? 443
                do {
                    let response = try await publicHeaderRequest([
                        "-q", "--noproxy", "*", "--resolve", "\(host):\(port):\(pinned)",
                        "--silent", "--show-error", "--no-buffer", "--connect-timeout", "5", "--max-time", "12",
                        "--range", "0-0", "--dump-header", "-", "--output", "/dev/null", url.absoluteString,
                    ])
                    try Task.checkCancellation()
                    try validatePublicResponse(response, url: url, marker: marker, allowRedirect: allowRedirect,
                                               expectedRedirect: expectedRedirect)
                    return nil
                } catch let error as PublicCheckError { return error }
                catch { return .connection }
            }
        }
        var failures: [PublicCheckError] = []
        for await failure in group {
            guard let failure else { group.cancelAll(); return nil as PublicCheckError? }
            failures.append(failure)
        }
        // A valid HTTP response is stronger evidence than an unreachable IPv6
        // relay. Never replace its diagnostic with a later transport failure.
        return failures.first { if case .page = $0 { return true }; return !$0.canPropagate }
            ?? failures.first { if case .marker = $0 { return true }; return false }
            ?? failures.first { if case .tls = $0 { return true }; return false }
            ?? failures.first { if case .handshake = $0 { return true }; return false }
            ?? failures.first ?? .connection
    }
    try Task.checkCancellation()
    if let failure { throw failure }
}


func validatePublicResponse(_ response: String, url: URL, marker: String, allowRedirect: Bool, expectedRedirect: URL? = nil) throws {
    let lines = response.split(whereSeparator: \.isNewline)
    let status = lines.last.flatMap { Int($0) } ?? 0
    guard (200...(allowRedirect ? 399 : 299)).contains(status) else {
        throw PublicCheckError.page(status)
    }
    func header(_ key: String) -> String? {
        lines.reversed().compactMap { line -> String? in
            let pair = line.split(separator: ":", maxSplits: 1)
            guard pair.count == 2, pair[0].caseInsensitiveCompare(key) == .orderedSame else { return nil }
            return pair[1].trimmingCharacters(in: .whitespaces)
        }.first
    }
    guard header(verificationHeader) == marker else { throw PublicCheckError.marker }
    if (300...399).contains(status) {
        if let expectedRedirect {
            guard expectedRedirect.scheme == "https", expectedRedirect.host == url.host,
                  [443, 8443, 10000].contains(expectedRedirect.port ?? 443),
                  let location = header("Location"),
                  let target = URL(string: location, relativeTo: url)?.absoluteURL,
                  target == expectedRedirect else { throw PublicCheckError.redirect }
            return
        }
        guard let location = header("Location"),
              let target = URL(string: location, relativeTo: url)?.absoluteURL,
              target.scheme == url.scheme, target.host == url.host,
              (target.port ?? 443) == (url.port ?? 443),
              !url.path.hasPrefix("/p/") ||
                target.path.hasPrefix(url.path.hasSuffix("/") ? url.path : url.path + "/") else {
            throw PublicCheckError.redirect
        }
    }
}
