import Foundation

enum ProjectSource: Codable, Equatable {
    case html(file: URL)
    case folder(root: URL, entry: String)
    case port(Int)

    var description: String {
        switch self {
        case .html(let file): return "单文件 HTML · \(file.lastPathComponent)"
        case .folder(let root, let entry): return "HTML 项目文件夹 · \(root.lastPathComponent)/\(entry)"
        case .port(let port): return "本机端口 · \(port)"
        }
    }

    var port: Int? {
        if case let .port(port) = self { return port }
        return nil
    }

    var sourceExists: Bool {
        switch self {
        case .html(let file): return Self.regularFile(file)
        case .folder(let root, let entry):
            guard Self.plainDirectory(root) else { return false }
            var path = root
            let parts = entry.split(separator: "/", omittingEmptySubsequences: false)
            for (index, part) in parts.enumerated() {
                guard !part.isEmpty, !part.hasPrefix(".") else { return false }
                path.appendPathComponent(String(part))
                if index == parts.count - 1 { return Self.regularFile(path) }
                guard Self.plainDirectory(path) else { return false }
            }
            return false
        case .port: return true
        }
    }

    private static func regularFile(_ url: URL) -> Bool {
        guard fileManager.fileExists(atPath: url.path),
              let values = try? url.resourceValues(forKeys: [.isRegularFileKey, .isSymbolicLinkKey]) else { return false }
        return values.isRegularFile == true && values.isSymbolicLink == false
    }

    private static func plainDirectory(_ url: URL) -> Bool {
        guard fileManager.fileExists(atPath: url.path),
              let values = try? url.resourceValues(forKeys: [.isDirectoryKey, .isSymbolicLinkKey]) else { return false }
        return values.isDirectory == true && values.isSymbolicLink == false
    }

    func prepare(in directory: URL) throws -> Content {
        if case .port(let port) = self {
            do { _ = try localHTTPStatus(port: port) }
            catch { throw AppError("本机端口 \(port) 未响应。请先启动该项目，再重试。") }
            return .proxy(port)
        }
        guard sourceExists else { throw AppError("源文件失效。请重新定位后再启动。") }
        let snapshot = directory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try privateDirectory(snapshot)
        do {
            switch self {
            case .html(let file):
                try fileManager.copyItem(at: file, to: snapshot.appendingPathComponent("index.html"))
                return .files(snapshot, "index.html")
            case .folder(let root, let entry):
                let rootPath = root.resolvingSymlinksInPath().path
                let snapshotPath = snapshot.resolvingSymlinksInPath().path
                let supportPath = support.resolvingSymlinksInPath().path
                guard rootPath != "/", !snapshotPath.hasPrefix(rootPath + "/"),
                      supportPath != rootPath, !supportPath.hasPrefix(rootPath + "/"),
                      !rootPath.hasPrefix(supportPath + "/") else {
                    throw AppError("请选择专用项目文件夹，范围不能包含 DemoMux 的数据或预览目录。")
                }
                try copyStaticFolder(root, into: snapshot)
                guard Self.regularFile(snapshot.appendingPathComponent(entry)) else {
                    throw AppError("源文件失效。所选入口 HTML 无法读取。")
                }
                return .files(snapshot, entry)
            case .port:
                preconditionFailure("Port sources are handled above")
            }
        } catch {
            try? fileManager.removeItem(at: snapshot)
            throw error
        }
    }
}

private func copyStaticFolder(_ source: URL, into target: URL) throws {
    let keys: Set<URLResourceKey> = [.isDirectoryKey, .isRegularFileKey, .isSymbolicLinkKey]
    let children = try fileManager.contentsOfDirectory(at: source, includingPropertiesForKeys: Array(keys),
                                                       options: [.skipsHiddenFiles])
    for child in children {
        let values = try child.resourceValues(forKeys: keys)
        guard values.isSymbolicLink == false, !child.lastPathComponent.hasPrefix(".") else { continue }
        let output = target.appendingPathComponent(child.lastPathComponent)
        if values.isDirectory == true {
            try fileManager.createDirectory(at: output, withIntermediateDirectories: false)
            try copyStaticFolder(child, into: output)
        } else if values.isRegularFile == true {
            try fileManager.copyItem(at: child, to: output)
        }
    }
}

struct Project: Codable, Identifiable {
    let id: UUID
    var name: String
    var slug: String
    var source: ProjectSource
    var lockedSlug = false

    var path: String { "/p/\(slug)/" }
    var localPath: String { "/project/\(id.uuidString.lowercased())/" }
}

struct Catalog: Codable {
    var projects: [Project] = []
    // Keep the last assignment even after a slot has been released or the app has quit.
    var previousRootOwners: [Int: UUID] = [:]
    var accountRootOwners: [String: [Int: UUID]] = [:]

    init() {}
    private enum CodingKeys: String, CodingKey { case projects, previousRootOwners, accountRootOwners }
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        projects = try values.decode([Project].self, forKey: .projects)
        previousRootOwners = try values.decodeIfPresent([Int: UUID].self, forKey: .previousRootOwners) ?? [:]
        accountRootOwners = try values.decodeIfPresent([String: [Int: UUID]].self, forKey: .accountRootOwners) ?? [:]
    }

    func reusesRoot(_ port: Int, project: UUID, profile: String, host: String) -> Bool {
        if let previous = accountRootOwners[profile + "|" + host.lowercased()]?[port] { return previous != project }
        if accountRootOwners.contains(where: {
            $0.key.hasSuffix("|" + host.lowercased()) && $0.value[port].map { $0 != project } == true
        }) { return true }
        // Legacy records did not identify the account or host. Warn once rather
        // than silently assigning an old shared address.
        return previousRootOwners[port] != nil
    }

    mutating func rememberRoot(_ port: Int, project: UUID, profile: String, host: String) {
        accountRootOwners[profile + "|" + host.lowercased(), default: [:]][port] = project
        previousRootOwners[port] = nil
    }

    static var file: URL { support.appendingPathComponent("Projects.json") }

    static func load() throws -> Self {
        guard fileManager.fileExists(atPath: file.path) else { return Catalog() }
        let catalog = try JSONDecoder().decode(Catalog.self, from: Data(contentsOf: file))
        let slugs = catalog.projects.map(\.slug)
        guard Set(slugs).count == slugs.count, slugs.allSatisfy(validSlug),
              Set(catalog.projects.map(\.id)).count == catalog.projects.count,
              catalog.projects.allSatisfy({ $0.source.port.map(validProjectPort) ?? true }) else {
            throw AppError("项目列表包含无效或重复的条目。")
        }
        return catalog
    }

    func save() throws {
        try JSONEncoder().encode(self).write(to: Self.file, options: .atomic)
    }

    mutating func add(_ source: ProjectSource, name: String) -> Project {
        let id = UUID()
        let suggested = name.lowercased().split(whereSeparator: {
            !$0.isASCII || !($0.isLetter || $0.isNumber)
        }).joined(separator: "-")
        var slug = String(suggested.prefix(40)).trimmingCharacters(in: CharacterSet(charactersIn: "-"))
        if !validSlug(slug) || projects.contains(where: { $0.slug == slug }) {
            slug = "project-\(id.uuidString.lowercased())"
        }
        let entry = Project(id: id, name: name, slug: slug, source: source)
        projects.append(entry)
        return entry
    }
}

func validProjectPort(_ port: Int) -> Bool { (1...65535).contains(port) && port != previewPort }
func validSlug(_ slug: String) -> Bool {
    slug.range(of: #"^[a-z0-9](?:[a-z0-9-]{0,46}[a-z0-9])?$"#, options: .regularExpression) != nil
}

enum Content {
    case files(URL, String)
    case proxy(Int)

    func discard() {
        if case let .files(directory, _) = self { try? fileManager.removeItem(at: directory) }
    }
}

enum Publication: Equatable {
    case path
    case root(Int)
}

struct Share {
    let publication: Publication
    let marker = UUID().uuidString
    var verified = false

    func url(host: String, project: Project) -> URL {
        switch publication {
        case .path: return URL(string: "https://\(host)\(project.path)")!
        case .root(let port): return URL(string: "https://\(host)\(port == 443 ? "" : ":\(port)")/")!
        }
    }
}

struct PublicationSlots {
    static let limit = 3
    static let rootOrder = [8443, 10000, 443]
    var shares: [UUID: Share] = [:]
    var order: [UUID] = []
    var preferredHome: UUID?

    var homeProjectID: UUID? {
        if let root = shares.first(where: { $0.value.publication == .root(443) }) { return root.key }
        if let preferredHome, shares[preferredHome] != nil { return preferredHome }
        return order.first(where: { shares[$0] != nil }) ?? shares.keys.sorted { $0.uuidString < $1.uuidString }.first
    }

    mutating func remove(_ id: UUID) -> Share? {
        order.removeAll { $0 == id }
        if preferredHome == id { preferredHome = nil }
        return shares.removeValue(forKey: id)
    }

    mutating func selectHome(_ id: UUID) throws {
        guard shares[id] != nil else { throw AppError("请先发布此项目，再设为公网首页。") }
        guard !shares.contains(where: { $0.key != id && $0.value.publication == .root(443) }) else {
            throw AppError("公网根入口已由另一个项目独占。请先取消该项目发布，再更换首页。")
        }
        preferredHome = id
    }

    var freeRoot: Int? {
        Self.rootOrder.first { port in !shares.values.contains { $0.publication == .root(port) } }
    }

    mutating func reserve(_ project: UUID, publication: Publication) throws -> Share {
        guard shares[project] == nil, shares.count < Self.limit else {
            throw AppError("已用完三个公网发布名额。")
        }
        if case .root(let port) = publication {
            guard Self.rootOrder.contains(port),
                  !shares.values.contains(where: { $0.publication == publication }) else {
                throw AppError("该公网入口已经被占用。")
            }
        }
        let share = Share(publication: publication)
        shares[project] = share
        order.removeAll { $0 == project }
        order.append(project)
        return share
    }
}
