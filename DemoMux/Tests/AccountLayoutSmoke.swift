import AppKit
@main
struct LayoutSmoke {
    @MainActor
    static func main() async throws {
        _ = NSApplication.shared
        let app = DemoMuxWindow()
        app.window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 920, height: 660),
                              styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        app.window.appearance = NSAppearance(named: .darkAqua)
        app.accountSnapshot = AccountSnapshot(profiles: [
            .init(id: "personal", nickname: "Personal demo", tailnet: "personal.ts.net", account: "demo@example.test", selected: true),
            .init(id: "team", nickname: "Design team", tailnet: "team.ts.net", account: "demo@example.test", selected: false)
        ], backendState: "Stopped", displayName: "Demo User")
        app.showDashboard()
        app.window.contentView!.layoutSubtreeIfNeeded()
        precondition(app.accountSummary!.frame.height > 30)
        app.accountPanel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 610, height: 650),
                                  styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        app.accountPanel!.appearance = NSAppearance(named: .darkAqua)
        app.refreshAccountUI(force: true)
        app.accountPanel!.contentView!.layoutSubtreeIfNeeded()
        func findButtons(_ view: NSView) -> [NSButton] {
            (view as? NSButton).map { [$0] } ?? view.subviews.flatMap(findButtons)
        }
        precondition(findButtons(app.window.contentView!).first { $0.title == "使用说明" }?.isEnabled == true)
        let buttons = findButtons(app.accountPanel!.contentView!)
        precondition(buttons.contains { $0.title == "切换并启动" })
        precondition(buttons.contains { $0.title == "添加账号" })
        for button in buttons { precondition(button.frame.width > 20 && button.frame.height > 15, button.title) }
        app.pendingAccountProfileID = "personal"
        app.pendingAccountAction = .logout
        app.refreshAccountUI(force: true)
        app.accountPanel!.contentView!.layoutSubtreeIfNeeded()
        precondition(findButtons(app.accountPanel!.contentView!).contains { $0.title == "停止分享并退出登录" })
        precondition(!findButtons(app.accountPanel!.contentView!).contains { $0.title == "切换并启动" })
        app.accountSnapshot.profiles[0].selected = false
        app.accountSnapshot.profiles[1].selected = true
        app.confirmAccountAction()
        precondition(app.pendingAccountAction == nil && app.task == nil)
        precondition(app.accountMessage.contains("身份已改变"))
        for width in [565.0, 610.0] {
            app.accountPanel!.setContentSize(NSSize(width: width, height: 650))
            app.refreshAccountUI(force: true)
            app.accountPanel!.contentView!.layoutSubtreeIfNeeded()
            for button in findButtons(app.accountPanel!.contentView!) {
                precondition(button.frame.width > 20 && button.frame.height > 15)
            }
        }
        app.pendingAccountAction = nil
        app.detail = "旧的介绍页验证提示"
        app.accountMessage = "账号提示保持独立"
        app.notice = "正在发布测试项目"
        app.task = Task { try? await Task.sleep(for: .seconds(30)) }
        precondition(app.accountPanelMessage == app.notice)
        app.task?.cancel()
        app.task = nil
        app.run { throw PublicCheckError.handshake }
        while app.busy { try await Task.sleep(for: .milliseconds(25)) }
        precondition(app.accountMessage == "账号提示保持独立")
        precondition(app.notice.contains("握手中断"))
        precondition(!app.accountPanelMessage.contains("旧的介绍页"))
        let running = try Child(URL(fileURLWithPath: "/bin/sleep"), ["30"])
        defer { running.stop() }
        app.caddy = running
        app.funnels[443] = running
        app.publicHost = "fixture.ts.net"
        precondition(app.publicStatusText == "入口已验证")
        app.publicVerificationIssue = "检查失败"
        precondition(app.publicStatusText == "公网待复核")
        precondition(app.accountPanelMessage.contains("公网入口需要重试"))
        app.checkingPublic = true
        precondition(app.publicStatusText == "正在检测公网")
        app.verifiedProfileID = "test-profile"
        let retryProject = app.catalog.add(.port(5173), name: "Retry state fixture")
        app.diagnostics[retryProject.id] = "Stale previous failure"
        let publication = Task { try? await app.publishProject(retryProject, publication: .path, slug: retryProject.slug) }
        for _ in 0..<80 {
            if app.pendingID == retryProject.id { break }
            try await Task.sleep(for: .milliseconds(5))
        }
        precondition(app.pendingID == retryProject.id)
        precondition(app.diagnostics[retryProject.id] == nil)
        precondition(app.notice.contains("正在发布"))
        publication.cancel()
        await publication.value
        precondition(app.pendingID == nil)
        precondition(app.slots.shares.isEmpty)
        print("Passed: account layout, minimum sizing, inline confirmation, identity guard, project error isolation, stale detail suppression and public verification status")
        print("Passed: retry clears stale error before delayed identity lookup, reserves visible pending state and releases it on cancellation")
    }
}
