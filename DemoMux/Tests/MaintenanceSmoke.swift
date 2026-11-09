import Foundation

@main
struct MaintenanceSmoke {
    static func main() throws {
        let base = fileManager.temporaryDirectory.resolvingSymlinksInPath().appendingPathComponent("DemoMux-Uninstall-" + UUID().uuidString)
        try privateDirectory(base)
        defer { try? fileManager.removeItem(at: base) }
        func write(_ file: URL, _ text: String = "fixture") throws {
            try privateDirectory(file.deletingLastPathComponent())
            try Data(text.utf8).write(to: file)
        }
        func fixture(_ name: String) throws -> MaintenanceContext {
            let area = base.appendingPathComponent(name)
            let context = MaintenanceContext(app: area.appendingPathComponent("DemoMux.app"), home: area.appendingPathComponent("Home"), preferenceDomain: "app.demomux.qa." + name)
            try privateDirectory(context.home)
            try write(context.app.appendingPathComponent("Contents/Info.plist"), "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict><key>CFBundleIdentifier</key><string>com.demomux.preview</string><key>CFBundleExecutable</key><string>DemoMux</string></dict></plist>")
            for path in ["Components/Caddy-2.11.9/caddy", "Components/Caddy-0.0.1/caddy", "Components/Tailscale-1.104.5/tailscaled", "CaddyData/data", "Accounts.json", "Projects.json", "Tailscale/state"] {
                try write(context.data.appendingPathComponent(path))
            }
            try write(context.home.appendingPathComponent("Library/Caches/com.demomux.preview/item"))
            try write(context.home.appendingPathComponent("Documents/Original.html"))
            try write(context.home.appendingPathComponent("System/Tailscale.app/unrelated"))
            return context
        }
        let trash = base.appendingPathComponent("Trash")
        try privateDirectory(trash)
        func move(_ source: URL) throws -> URL {
            let target = trash.appendingPathComponent(UUID().uuidString + "-" + source.lastPathComponent)
            try fileManager.moveItem(at: source, to: target)
            return target
        }
        func exists(_ url: URL) -> Bool { fileManager.fileExists(atPath: url.path) }
        let full = try fixture("full")
        var preferencesCleared = false
        let receipt = try Uninstaller.execute(full, removingData: true, trash: move) { preferencesCleared = true }
        precondition(preferencesCleared && !exists(full.app) && !exists(full.data) && receipt.items.count == 3)
        precondition(exists(full.home.appendingPathComponent("Documents/Original.html")))
        precondition(exists(full.home.appendingPathComponent("System/Tailscale.app/unrelated")))
        precondition(receipt.items.allSatisfy { exists($0.trash) })
        let keep = try fixture("keep")
        _ = try Uninstaller.execute(keep, removingData: false, trash: move) { preconditionFailure("Keep-data must not clear preferences") }
        precondition(!exists(keep.app) && !exists(keep.components) && !exists(keep.data.appendingPathComponent("CaddyData")))
        precondition(exists(keep.data.appendingPathComponent("Projects.json")) && exists(keep.data.appendingPathComponent("Tailscale/state")))
        let failure = try fixture("failure")
        do {
            _ = try Uninstaller.execute(failure, removingData: true, trash: { url in
                if url.path == failure.app.path { throw AppError("Permission fixture") }
                return try move(url)
            }) { preconditionFailure("No preferences clearing after move failure") }
            preconditionFailure("Expected permission failure")
        } catch { precondition(error.localizedDescription.contains("已恢复")) }
        precondition(exists(failure.app) && exists(failure.data.appendingPathComponent("Accounts.json")))
        let preferenceFailure = try fixture("prefs-failure")
        do {
            _ = try Uninstaller.execute(preferenceFailure, removingData: true, trash: move) { throw AppError("Preferences fixture") }
            preconditionFailure("Expected preference failure")
        } catch { precondition(error.localizedDescription.contains("已恢复")) }
        precondition(exists(preferenceFailure.app) && exists(preferenceFailure.data))
        let occupied = try fixture("occupied")
        do {
            _ = try Uninstaller.execute(occupied, removingData: true, trash: { url in
                if url.path == occupied.app.path {
                    try write(occupied.data.appendingPathComponent("new-user-file"), "preserve")
                    throw AppError("Permission fixture")
                }
                return try move(url)
            }) {}
            preconditionFailure("Expected conflict")
        } catch { precondition(error.localizedDescription.contains("部分文件仍在废纸篓")) }
        let preserved = try String(contentsOf: occupied.data.appendingPathComponent("new-user-file"), encoding: .utf8)
        precondition(preserved == "preserve")
        let wrong = try fixture("wrong-app")
        try write(wrong.app.appendingPathComponent("Contents/Info.plist"), "<?xml version=\"1.0\"?><plist><dict><key>CFBundleIdentifier</key><string>another.app</string></dict></plist>")
        do { _ = try wrong.plan(removingData: true); preconditionFailure("Wrong app accepted") } catch {}
        let linked = try fixture("linked")
        let outside = base.appendingPathComponent("External")
        try privateDirectory(outside); try write(outside.appendingPathComponent("preserve"))
        try fileManager.removeItem(at: linked.data)
        try fileManager.createSymbolicLink(at: linked.data, withDestinationURL: outside)
        do { _ = try linked.plan(removingData: true); preconditionFailure("Linked root accepted") } catch {}
        precondition(exists(outside.appendingPathComponent("preserve")))
        let nested = try fixture("nested-link")
        let bytes = managedStorageSize(nested.data)
        try fileManager.createSymbolicLink(at: nested.components.appendingPathComponent("external"), withDestinationURL: outside)
        precondition(managedStorageSize(nested.data) == bytes)
        _ = try Uninstaller.execute(nested, removingData: false, trash: move) {}
        precondition(exists(outside.appendingPathComponent("preserve")))
        print("Passed: full/keep-data uninstall, all private component versions, original/system preservation, recoverable moves, permission/preferences rollback, non-overwrite recovery, app identity and symlink scope, storage measurement")
    }
}
