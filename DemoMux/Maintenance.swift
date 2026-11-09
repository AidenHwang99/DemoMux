import Foundation

struct MaintenanceContext {
    static let bundleID = "com.demomux.preview"
    let app: URL
    let home: URL
    let preferenceDomain: String
    var expectedBundleID: String = MaintenanceContext.bundleID
    var expectedExecutable: String = "DemoMux"
    static var current: MaintenanceContext {
        .init(app: Bundle.main.bundleURL, home: fileManager.homeDirectoryForCurrentUser,
              preferenceDomain: bundleID)
    }
    var data: URL { home.appendingPathComponent("Library/Application Support/DemoMux", isDirectory: true) }
    var components: URL { data.appendingPathComponent("Components", isDirectory: true) }

    func plan(removingData: Bool) throws -> UninstallPlan {
        let app = app.standardizedFileURL
        guard app.isFileURL, app.pathExtension == "app", app.path != "/",
              app.resolvingSymlinksInPath().path == app.path,
              let metadata = try? Data(contentsOf: app.appendingPathComponent("Contents/Info.plist")),
              let info = (try? PropertyListSerialization.propertyList(from: metadata, options: [], format: nil)) as? [String: Any],
              info["CFBundleIdentifier"] as? String == expectedBundleID,
              info["CFBundleExecutable"] as? String == expectedExecutable else {
            throw AppError("无法确认当前 DemoMux 应用的位置或身份。请在 Finder 中检查应用后重试。")
        }
        let home = home.standardizedFileURL
        guard home.isFileURL, home.path != "/", home.resolvingSymlinksInPath().path == home.path else {
            throw AppError("应用数据目录不符合预期，未执行卸载。")
        }
        var entries: [UninstallPlan.Entry] = []
        if removingData {
            entries.append(.init(name: "私有组件、项目列表与本机登录数据", url: data))
            for relative in ["Library/Caches/\(Self.bundleID)",
                             "Library/HTTPStorages/\(Self.bundleID)",
                             "Library/WebKit/\(Self.bundleID)",
                             "Library/Saved Application State/\(Self.bundleID).savedState"] {
                entries.append(.init(name: "DemoMux 应用缓存", url: home.appendingPathComponent(relative)))
            }
        } else {
            entries += [.init(name: "已下载的 Caddy 与 Tailscale", url: components),
                        .init(name: "Caddy 运行数据", url: data.appendingPathComponent("CaddyData"))]
        }
        entries.append(.init(name: "DemoMux 应用", url: app))
        // Reject linked roots/ancestors. Never follow a link into another app or
        // a system-wide install; links inside a whole trashed directory are inert.
        for entry in entries {
            let path = entry.url.standardizedFileURL
            guard path.path != home.path, path.path != "/",
                  path.resolvingSymlinksInPath().path == path.path else {
                throw AppError("\(entry.name) 的路径包含符号链接，未执行卸载。")
            }
        }
        return .init(entries: entries, removesData: removingData)
    }
}

struct UninstallPlan {
    struct Entry { let name: String; let url: URL }
    let entries: [Entry]
    let removesData: Bool
}

struct UninstallReceipt {
    struct Item { let original: URL; let trash: URL }
    let items: [Item]
}

enum Uninstaller {
    typealias Trash = (URL) throws -> URL
    static func systemTrash(_ source: URL) throws -> URL {
        var result: NSURL?
        try fileManager.trashItem(at: source, resultingItemURL: &result)
        guard let result else { throw AppError("macOS 未返回废纸篓位置，请在 Finder 中检查。") }
        return result as URL
    }

    // Move whole owned locations, never walk user project sources. If any move
    // fails, restore preceding moves without overwriting newly-created files.
    static func execute(_ context: MaintenanceContext, removingData: Bool,
                        trash: Trash = systemTrash, clearPreferences: () throws -> Void) throws -> UninstallReceipt {
        let plan = try context.plan(removingData: removingData)
        var moved: [UninstallReceipt.Item] = []
        var current = "卸载"
        do {
            for entry in plan.entries where fileManager.fileExists(atPath: entry.url.path) {
                current = entry.name
                let destination = try trash(entry.url)
                moved.append(.init(original: entry.url, trash: destination))
            }
            if removingData { current = "偏好设置"; try clearPreferences() }
            return .init(items: moved)
        } catch {
            var unrestored: [String] = []
            for item in moved.reversed() {
                do {
                    guard !fileManager.fileExists(atPath: item.original.path) else { throw AppError("原位置已被占用") }
                    try fileManager.moveItem(at: item.trash, to: item.original)
                } catch { unrestored.append(item.original.lastPathComponent) }
            }
            let recovery = unrestored.isEmpty ? "已恢复本次移走的文件。" : "部分文件仍在废纸篓：\(unrestored.joined(separator: "、"))。可用 Finder 还原。"
            throw AppError("无法移除\(current)：\(error.localizedDescription)\n\(recovery)演示已停止；处理目录权限后可重试。")
        }
    }
}

func managedStorageSize(_ root: URL) -> Int64 {
    guard let values = try? root.resourceValues(forKeys: [.isSymbolicLinkKey]), values.isSymbolicLink != true,
          let walk = fileManager.enumerator(at: root, includingPropertiesForKeys: [.isSymbolicLinkKey, .isRegularFileKey, .fileSizeKey],
                                           options: [], errorHandler: { _, _ in false }) else { return 0 }
    var bytes: Int64 = 0
    for case let url as URL in walk {
        guard let values = try? url.resourceValues(forKeys: [.isSymbolicLinkKey, .isRegularFileKey, .fileSizeKey]) else { continue }
        if values.isSymbolicLink == true { walk.skipDescendants(); continue }
        if values.isRegularFile == true { bytes += Int64(values.fileSize ?? 0) }
    }
    return bytes
}
