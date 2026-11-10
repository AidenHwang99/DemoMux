import Foundation
import CryptoKit
import Darwin

struct AppError: LocalizedError {
    let errorDescription: String?
    init(_ message: String) { errorDescription = message }
}

// A deployment minimum is a lower bound, not a list of allowed major releases.
// Keep this aligned with LSMinimumSystemVersion and the build's deployment target.
enum PlatformRequirements {
    static func supports(_ version: OperatingSystemVersion) -> Bool {
        version.majorVersion >= 26
    }
}

let fileManager = FileManager.default
let support = fileManager.homeDirectoryForCurrentUser
    .appendingPathComponent("Library/Application Support/DemoMux", isDirectory: true)
let previewPort = 8081

func privateDirectory(_ url: URL) throws {
    try fileManager.createDirectory(at: url, withIntermediateDirectories: true,
                                    attributes: [.posixPermissions: 0o700])
    try fileManager.setAttributes([.posixPermissions: 0o700], ofItemAtPath: url.path)
}

func fileDigest(_ url: URL) throws -> String {
    let handle = try FileHandle(forReadingFrom: url)
    defer { try? handle.close() }
    var hash = SHA256()
    while let data = try handle.read(upToCount: 1024 * 1024), !data.isEmpty {
        hash.update(data: data)
    }
    return hash.finalize().map { String(format: "%02x", $0) }.joined()
}

enum Dependency: CaseIterable {
    case caddy, tailscale

    var name: String { self == .caddy ? "Caddy" : "Tailscale" }
    var version: String { self == .caddy ? "2.11.9" : "1.104.5" }
    var archiveDigest: String {
        switch self {
        case .caddy: return "d075afc620376679a77b453399bf7ada264cab3280b2833e4ed1372abc0c1c13"
        case .tailscale: return "17f794d909751d589a7ce7a0eb8fd9bfafa126480f7f0d1c60a1e1f05d871e9c"
        }
    }

    var directory: URL {
        support.appendingPathComponent("Components/\(name)-\(version)", isDirectory: true)
    }

    func executable(_ name: String) -> URL { directory.appendingPathComponent(name) }

    private var files: [(name: String, member: String, digest: String)] {
        switch self {
        case .caddy:
            return [("caddy", "caddy", "151fe2febcc0e7baf8a88d2e206953b813975aca3b0e35dd5573d826972caecd")]
        case .tailscale:
            return [
                ("tailscale", "tailscale/\(version)/bin/tailscale", "15ef8842cd3e65ab052d4bd062b286a4d7b1b805203634e211bb481da1a637e7"),
                ("tailscaled", "tailscale/\(version)/bin/tailscaled", "2fa8ca11046ec55a33feb028b437f0f09934cd548f5975c71e9fc937005ec5da"),
            ]
        }
    }

    var installed: Bool {
        files.allSatisfy {
            let url = executable($0.name)
            return fileManager.isExecutableFile(atPath: url.path) && (try? fileDigest(url)) == $0.digest
        }
    }

    func install() async throws {
        if installed { return }
        var request: URLRequest
        switch self {
        case .caddy:
            let address = "https://github.com/caddyserver/caddy/releases/download/v\(version)/caddy_\(version)_mac_arm64.tar.gz"
            request = URLRequest(url: URL(string: address)!)
        case .tailscale:
            let tokenURL = URL(string: "https://ghcr.io/token?service=ghcr.io&scope=repository:homebrew/core/tailscale:pull")!
            let (data, response) = try await URLSession.shared.data(from: tokenURL)
            guard (response as? HTTPURLResponse)?.statusCode == 200,
                  let token = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let bearer = token["token"] as? String else {
                throw AppError("无法取得 Tailscale 下载授权。")
            }
            let address = "https://ghcr.io/v2/homebrew/core/tailscale/blobs/sha256:\(archiveDigest)"
            request = URLRequest(url: URL(string: address)!)
            request.setValue("Bearer \(bearer)", forHTTPHeaderField: "Authorization")
        }
        request.timeoutInterval = 60
        let (download, response) = try await URLSession.shared.download(for: request)
        defer { try? fileManager.removeItem(at: download) }
        guard (response as? HTTPURLResponse)?.statusCode == 200 else {
            throw AppError("\(name) 下载失败（HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)）。")
        }
        try Task.checkCancellation()
        guard try fileDigest(download) == archiveDigest else {
            throw AppError("\(name) 下载包的 SHA-256 校验失败。")
        }

        let parent = directory.deletingLastPathComponent()
        try privateDirectory(parent)
        let stage = parent.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try privateDirectory(stage)
        defer { try? fileManager.removeItem(at: stage) }
        let payload = stage.appendingPathComponent("payload", isDirectory: true)
        try privateDirectory(payload)
        for entry in files {
            let target = payload.appendingPathComponent(entry.name)
            guard fileManager.createFile(atPath: target.path, contents: nil) else {
                throw AppError("无法写入 \(entry.name)。")
            }
            let output = try FileHandle(forWritingTo: target)
            defer { try? output.close() }
            let tar = Process()
            tar.executableURL = URL(fileURLWithPath: "/usr/bin/tar")
            tar.arguments = ["-xOf", download.path, entry.member]
            tar.standardOutput = output
            tar.standardError = FileHandle.nullDevice
            try tar.run()
            tar.waitUntilExit()
            guard tar.terminationStatus == 0, try fileDigest(target) == entry.digest else {
                throw AppError("\(entry.name) 提取或校验失败。")
            }
            try fileManager.setAttributes([.posixPermissions: 0o700], ofItemAtPath: target.path)
        }
        try Task.checkCancellation()
        if fileManager.fileExists(atPath: directory.path) {
            let backup = stage.appendingPathComponent("previous")
            try fileManager.moveItem(at: directory, to: backup)
            do { try fileManager.moveItem(at: payload, to: directory) }
            catch {
                try fileManager.moveItem(at: backup, to: directory)
                throw error
            }
        } else {
            try fileManager.moveItem(at: payload, to: directory)
        }
    }
}

final class Child {
    let process = Process()
    private let pipe = Pipe()
    private let lock = NSLock()
    private var captured = Data()

    var running: Bool { process.isRunning }
    var output: String {
        lock.lock()
        defer { lock.unlock() }
        return String(decoding: captured, as: UTF8.self)
    }

    init(_ executable: URL, _ arguments: [String], environment: [String: String]? = nil) throws {
        if Bundle.main.bundleURL.pathExtension == "app" {
            let supervisor = Bundle.main.bundleURL.appendingPathComponent("Contents/Helpers/DemoMuxSupervisor")
            guard fileManager.isExecutableFile(atPath: supervisor.path) else {
                throw AppError("应用包缺少进程保护组件，请重新安装完整的 DemoMux。")
            }
            process.executableURL = supervisor
            process.arguments = [executable.path] + arguments
        } else {
            process.executableURL = executable
            process.arguments = arguments
        }
        process.environment = environment
        process.standardInput = FileHandle.nullDevice
        process.standardOutput = pipe
        process.standardError = pipe
        pipe.fileHandleForReading.readabilityHandler = { [weak self] handle in
            let data = handle.availableData
            guard let self, !data.isEmpty else {
                handle.readabilityHandler = nil
                return
            }
            self.lock.lock()
            self.captured.append(data)
            if self.captured.count > 64 * 1024 {
                self.captured.removeFirst(self.captured.count - 64 * 1024)
            }
            self.lock.unlock()
        }
        do {
            try process.run()
            try pipe.fileHandleForWriting.close()
        } catch {
            pipe.fileHandleForReading.readabilityHandler = nil
            throw error
        }
    }

    func stop() {
        guard running else { return }
        process.terminate()
        for _ in 0..<20 {
            if !running { return }
            Thread.sleep(forTimeInterval: 0.025)
        }
        if running { kill(process.processIdentifier, SIGKILL) }
        process.waitUntilExit()
    }

    deinit {
        stop()
        pipe.fileHandleForReading.readabilityHandler = nil
    }
}

func componentFailure(_ output: String) -> String {
    let lines = output.split(whereSeparator: \.isNewline).map(String.init)
    for line in lines.reversed() {
        if line.hasPrefix("Error:") { return line }
        if let data = line.data(using: .utf8),
           let entry = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           let level = entry["level"] as? String, ["error", "fatal", "panic"].contains(level) {
            return entry["error"] as? String ?? entry["msg"] as? String ?? line
        }
    }
    return lines.last ?? output
}

func command(_ executable: URL, _ arguments: [String], timeout: TimeInterval = 8) throws -> String {
    let child = try Child(executable, arguments)
    let deadline = Date().addingTimeInterval(timeout)
    while child.running, Date() < deadline { Thread.sleep(forTimeInterval: 0.025) }
    guard !child.running else {
        child.stop()
        throw AppError("\(executable.lastPathComponent) 响应超时。")
    }
    // Allow the pipe handler to drain the final bytes after the child exits.
    Thread.sleep(forTimeInterval: 0.035)
    let output = child.output
    guard child.process.terminationStatus == 0 else {
        throw AppError(output.isEmpty ? "\(executable.lastPathComponent) 执行失败。" : output)
    }
    return output
}

func asyncCommand(_ executable: URL, _ arguments: [String], timeout: TimeInterval = 20) async throws -> String {
    try Task.checkCancellation()
    let child = try Child(executable, arguments)
    defer { child.stop() }
    let deadline = Date().addingTimeInterval(timeout)
    while child.running {
        try Task.checkCancellation()
        guard Date() < deadline else { throw AppError("\(executable.lastPathComponent) 响应超时。") }
        try await Task.sleep(for: .milliseconds(25))
    }
    try await Task.sleep(for: .milliseconds(50))
    try Task.checkCancellation()
    guard child.process.terminationStatus == 0 else {
        throw AppError(child.output.isEmpty ? "\(executable.lastPathComponent) 执行失败。" : child.output)
    }
    return child.output
}

func curl(_ arguments: [String]) throws -> String {
    try command(URL(fileURLWithPath: "/usr/bin/curl"), [
        "-q", "--noproxy", "*", "--silent", "--show-error", "--max-time", "2",
    ] + arguments)
}

func localHTTPStatus(port: Int, path: String = "/") throws -> Int {
    let result = try curl(["--output", "/dev/null", "--write-out", "%{http_code}",
                           "http://127.0.0.1:\(port)\(path)"])
    guard let code = Int(result), (100...599).contains(code) else {
        throw AppError("端口 \(port) 未返回 HTTP 响应。")
    }
    return code
}

// An explicit refusal indicates a closed local port. Ignore transient errors
// and timeouts so a busy server keeps its publication.
func loopbackConnectionRefused(_ port: Int) -> Bool {
    let fd = socket(AF_INET, SOCK_STREAM, 0)
    guard fd >= 0 else { return false }
    defer { close(fd) }
    guard fcntl(fd, F_SETFL, O_NONBLOCK) != -1 else { return false }
    var address = sockaddr_in()
    let size = socklen_t(MemoryLayout<sockaddr_in>.size)
    address.sin_len = UInt8(size)
    address.sin_family = sa_family_t(AF_INET)
    address.sin_port = in_port_t(port).bigEndian
    address.sin_addr.s_addr = inet_addr("127.0.0.1")
    let result = withUnsafePointer(to: &address) {
        $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { connect(fd, $0, size) }
    }
    guard result != 0 else { return false }
    if errno == ECONNREFUSED { return true }
    guard errno == EINPROGRESS else { return false }
    var event = pollfd(fd: fd, events: Int16(POLLOUT), revents: 0)
    guard poll(&event, 1, 250) == 1 else { return false }
    var failure: Int32 = 0
    var length = socklen_t(MemoryLayout<Int32>.size)
    return getsockopt(fd, SOL_SOCKET, SO_ERROR, &failure, &length) == 0 && failure == ECONNREFUSED
}

func loopbackPort(_ preferred: Int = 0) throws -> Int {
    let fd = socket(AF_INET, SOCK_STREAM, 0)
    guard fd >= 0 else { throw AppError("无法检查本机端口。") }
    defer { close(fd) }
    var reuse: Int32 = 1
    _ = setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &reuse, socklen_t(MemoryLayout<Int32>.size))
    var address = sockaddr_in()
    let size = socklen_t(MemoryLayout<sockaddr_in>.size)
    address.sin_len = UInt8(size)
    address.sin_family = sa_family_t(AF_INET)
    address.sin_port = in_port_t(preferred).bigEndian
    address.sin_addr.s_addr = inet_addr("127.0.0.1")
    let result = withUnsafePointer(to: &address) { pointer in
        pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) {
            bind(fd, $0, size)
        }
    }
    guard result == 0 else {
        throw AppError("本机端口 \(preferred) 已被占用。请释放端口后重试。")
    }
    var length = size
    let named = withUnsafeMutablePointer(to: &address) { pointer in
        pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) {
            getsockname(fd, $0, &length)
        }
    }
    guard named == 0 else { throw AppError("无法取得本机端口。") }
    return Int(UInt16(bigEndian: address.sin_port))
}

func availableLoopbackPort(excluding reserved: Set<Int>) throws -> Int {
    for _ in 0..<10 {
        let port = try loopbackPort()
        if !reserved.contains(port) { return port }
    }
    throw AppError("未能分配空闲的本机转发端口。请重试。")
}

func firstURL(in text: String, where matches: (URL) -> Bool) -> URL? {
    text.split(whereSeparator: \.isWhitespace).lazy
        .compactMap { URL(string: $0.trimmingCharacters(in: CharacterSet(charactersIn: "\"',<>"))) }
        .first { $0.scheme == "https" && matches($0) }
}
