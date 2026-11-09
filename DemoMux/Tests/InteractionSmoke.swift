import AppKit

@MainActor
final class InteractionHost: NSObject, NSApplicationDelegate {
    let controller = DemoMuxWindow()
    var simulatedRecovery = false
    func applicationDidFinishLaunching(_ note: Notification) {
        do {
            try privateDirectory(support)
            controller.runtimeDirectory = support.appendingPathComponent("Runtime")
            try privateDirectory(controller.runtimeDirectory)
            let fixtureRoot = support.resolvingSymlinksInPath()
            let fixtureHome = fixtureRoot.appendingPathComponent("UninstallFixtureHome")
            let fixtureApp = Bundle.main.bundleURL.resolvingSymlinksInPath()
            let domain = "app.demomux.qa." + UUID().uuidString
            controller.defaults = UserDefaults(suiteName: domain)!
            controller.maintenanceContext = .init(app: fixtureApp, home: fixtureHome, preferenceDomain: domain,
                                                  expectedBundleID: Bundle.main.bundleIdentifier!, expectedExecutable: "DemoMuxQA")
            for path in ["Library/Application Support/DemoMux/Components/Caddy-2.11.9/caddy",
                         "Library/Application Support/DemoMux/Components/Tailscale-1.104.5/tailscaled",
                         "Library/Application Support/DemoMux/Projects.json", "Documents/Original.html",
                         "System/Tailscale.app/preserve"] {
                let file = fixtureHome.appendingPathComponent(path)
                try privateDirectory(file.deletingLastPathComponent())
                try Data("isolated fixture".utf8).write(to: file)
            }
            controller.window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 920, height: 660),
                                         styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
            controller.window.title = "DemoMux · 隔离交互测试"
            controller.window.minSize = NSSize(width: 810, height: 575)
            controller.window.isReleasedWhenClosed = false
            controller.window.delegate = controller
            controller.isolatedPreviewNotice = "隔离交互测试 · 合成数据 · 不登录、不连接公网"
            let fixture = try Child(URL(fileURLWithPath: "/bin/sleep"), ["3600"])
            controller.caddy = fixture; controller.tailscaled = fixture
            controller.funnels[443] = try Child(URL(fileURLWithPath: "/bin/sleep"), ["3600"])
            controller.primaryCandidate = "demo.fixture.ts.net"
            controller.accountSnapshot = .init(profiles: [.init(id: "fixture", nickname: "Design Demo", tailnet: "fixture.ts.net", account: "demo@example.test", selected: true)],
                                               backendState: "Running", displayName: "Design Demo", dnsName: "demo.fixture.ts.net", principalConfirmed: true)
            controller.accountClientForTesting = AccountClient { arguments in
                if arguments.first == "switch" { return #"[{"id":"fixture","nickname":"Design Demo","tailnet":"fixture.ts.net","account":"demo@example.test","selected":true}]"# }
                return #"{"BackendState":"Running","Self":{"DNSName":"demo.fixture.ts.net.","UserID":42},"User":{"42":{"DisplayName":"Design Demo","LoginName":"demo@example.test"}}}"#
            }
            controller.publicProbeForTesting = { [weak self] _, _, _, _ in
                try await Task.sleep(for: .milliseconds(350))
                if self?.simulatedRecovery != true { throw PublicCheckError.timeout }
            }
            let menu = NSMenu()
            let appItem = NSMenuItem(); let commands = NSMenu()
            for (title, action, key) in [("小窗口 810 × 553", #selector(smallWindow), "1"),
                                          ("默认窗口 920 × 660", #selector(defaultWindow), "2"),
                                          ("宽窗口 1200 × 820", #selector(wideWindow), "3"),
                                          ("模拟网络恢复", #selector(recover), "r"),
                                          ("重新开始隔离重试", #selector(startRetry), "t"),
                                          ("退出隔离测试", #selector(NSApplication.terminate(_:)), "q")] {
                let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
                item.target = title == "退出隔离测试" ? NSApplication.shared : self
                commands.addItem(item)
            }
            appItem.submenu = commands; menu.addItem(appItem); NSApplication.shared.mainMenu = menu
            if Bundle.main.bundleURL.lastPathComponent.contains("Retry") {
                controller.setup.begin(.publicEntry)
                controller.phase = "隔离测试 · 公网重试"
                controller.detail = "仅模拟网络超时；前四步未执行。停止后可重新校验，⌘R 模拟网络恢复。"
                controller.setupContext = .init(profile: "fixture", host: "demo.fixture.ts.net", marker: controller.guideMarker)
                controller.showOnboarding()
                controller.retry.target = self; controller.retry.action = #selector(startRetry)
                startRetry()
            } else {
                controller.publicHost = "demo.fixture.ts.net"
                controller.verifiedProfileID = "fixture"; controller.entryVerified = true
                controller.localPort = 54321
                let live = controller.catalog.add(.port(5173), name: "产品设计 · 组件目录")
                _ = try controller.slots.reserve(live.id, publication: .path)
                controller.slots.shares[live.id]?.verified = true
                controller.active[live.id] = .proxy(5173)
                _ = controller.catalog.add(.port(3000), name: "一个很长的项目名称用于检查窄窗口下的省略、图标和固定操作位置 · 产品数据看板")
                let running = controller.catalog.add(.port(4321), name: "本机服务 · 产品介绍")
                controller.active[running.id] = .proxy(4321)
                controller.notice = "仅合成布局样例。状态与链接均为夹具，未连接公网。"
                controller.showDashboard()
            }
            controller.window.center(); controller.window.makeKeyAndOrderFront(nil)
            NSApplication.shared.activate(ignoringOtherApps: true)
        } catch { NSAlert(error: error).runModal(); NSApplication.shared.terminate(nil) }
    }
    @objc func smallWindow() { controller.window.setContentSize(NSSize(width: 810, height: 553)) }
    @objc func defaultWindow() { controller.window.setContentSize(NSSize(width: 920, height: 660)) }
    @objc func wideWindow() { controller.window.setContentSize(NSSize(width: 1200, height: 820)) }
    @objc func startRetry() {
        controller.setup.begin(.publicEntry)
        if controller.funnels[443]?.running != true {
            controller.funnels[443] = try? Child(URL(fileURLWithPath: "/bin/sleep"), ["3600"])
        }
        controller.run {
            try await self.controller.waitForPublic(URL(string: "https://demo.fixture.ts.net/")!, marker: self.controller.guideMarker, onboarding: true)
            self.controller.setup.complete(.publicEntry)
            self.controller.detail = "合成校验已通过。没有执行下载、登录或公网发布。"
        }
    }
    @objc func recover() { simulatedRecovery = true }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func applicationWillTerminate(_ note: Notification) {
        controller.task?.cancel(); controller.stopServices()
        if controller.uninstallCommitted {
            let context = controller.maintenanceContext
            let result: [String: Bool] = [
                "fixture_app_removed": !FileManager.default.fileExists(atPath: context.app.path),
                "fixture_data_removed": !FileManager.default.fileExists(atPath: context.data.path),
                "original_html_preserved": FileManager.default.fileExists(atPath: context.home.appendingPathComponent("Documents/Original.html").path),
                "system_app_preserved": FileManager.default.fileExists(atPath: context.home.appendingPathComponent("System/Tailscale.app/preserve").path),
                "installed_app_preserved": FileManager.default.fileExists(atPath: "/Applications/DemoMux.app"),
                "owned_processes_stopped": controller.caddy == nil && controller.tailscaled == nil && controller.funnels.isEmpty,
            ]
            if let data = try? JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys]) {
                try? data.write(to: Bundle.main.bundleURL.deletingLastPathComponent().appendingPathComponent("uninstall-result.json"))
            }
        }
        try? FileManager.default.removeItem(at: support)
    }
}

@main
struct InteractionSmoke {
    static func main() {
        MainActor.assumeIsolated {
            let app = NSApplication.shared
            app.setActivationPolicy(.regular)
            let host = InteractionHost(); app.delegate = host
            withExtendedLifetime(host) { app.run() }
        }
    }
}
