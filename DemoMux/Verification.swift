import Foundation

enum SetupStage: Int, CaseIterable, Sendable {
    case caddy = 1, tailscale, account, funnel, publicEntry
    var title: String {
        switch self {
        case .caddy: return "准备预览引擎"
        case .tailscale: return "准备连接组件"
        case .account: return "连接你的账号"
        case .funnel: return "启用公网访问"
        case .publicEntry: return "验证公网入口"
        }
    }
    var subtitle: String {
        switch self {
        case .caddy: return "检查 Caddy 与本机预览"
        case .tailscale: return "校验 DemoMux 私有 Tailscale"
        case .account: return "同步账号、设备身份与所属网络"
        case .funnel: return "确认 HTTPS 和 Funnel 权限"
        case .publicEntry: return "验证公共 DNS、HTTPS 与页面标记"
        }
    }
}

enum SetupStatus: Equatable, Sendable { case pending, active, complete, paused, failed }
struct SetupProgress: Equatable, Sendable {
    private(set) var states: [SetupStage: SetupStatus] = [:]
    private(set) var current: SetupStage?
    subscript(_ stage: SetupStage) -> SetupStatus { states[stage] ?? .pending }
    var mayVerify: Bool { SetupStage.allCases.filter { $0 != .publicEntry }.allSatisfy { self[$0] == .complete } }
    mutating func begin(_ stage: SetupStage) {
        current = stage
        for next in SetupStage.allCases where next.rawValue > stage.rawValue { states[next] = .pending }
        states[stage] = .active
    }
    mutating func complete(_ stage: SetupStage) { states[stage] = .complete }
    mutating func pause() { if let current { states[current] = .paused } }
    mutating func fail() { if let current { states[current] = .failed } }
    mutating func invalidate(from stage: SetupStage) {
        for next in SetupStage.allCases where next.rawValue >= stage.rawValue { states[next] = .pending }
        current = stage
    }
}

struct VerificationProgress: Equatable, Sendable {
    let attempt: Int
    let retryInSeconds: Int?
    let lastError: String?
    var text: String {
        if let seconds = retryInSeconds {
            return "\(lastError ?? "入口暂未就绪。")\n\(seconds) 秒后自动重试 · 已尝试 \(attempt) 次"
        }
        return "正在校验公网入口 · 第 \(attempt) 次尝试"
    }
}

enum VerificationRetry {
    static func backoff(_ attempt: Int) -> Int {
        switch attempt { case 1: return 2; case 2: return 4; case 3: return 8; default: return 15 }
    }
    static func run(check: () async throws -> Void,
                    report: (VerificationProgress) async -> Void,
                    sleep: (TimeInterval) async throws -> Void = { try await Task.sleep(for: .seconds($0)) }) async throws {
        var attempt = 1
        while true {
            try Task.checkCancellation()
            await report(.init(attempt: attempt, retryInSeconds: nil, lastError: nil))
            do { try await check(); try Task.checkCancellation(); return }
            catch let failure as PublicCheckError where failure.canPropagate && !Task.isCancelled {
                for remaining in stride(from: backoff(attempt), through: 1, by: -1) {
                    try Task.checkCancellation()
                    await report(.init(attempt: attempt, retryInSeconds: remaining, lastError: failure.localizedDescription))
                    try await sleep(1)
                }
                attempt += 1
            }
        }
    }
}

struct SetupVerificationContext: Equatable {
    let profile: String
    let host: String
    let marker: String
}
