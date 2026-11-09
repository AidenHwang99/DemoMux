import AppKit
@main
struct LayoutSmoke {
    @MainActor
    static func main() async throws {
        _ = NSApplication.shared
        let app = DemoMuxWindow()
        app.accountClientForTesting = AccountClient { _ in
            try await Task.sleep(for: .seconds(2))
            throw AppError("Delayed test identity read")
        }
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
        app.entryVerified = true
        app.verifiedProfileID = "personal"
        precondition(app.publicStatusText == "入口已验证")
        app.publicVerificationIssue = "检查失败"
        precondition(app.publicStatusText == "公网待复核")
        precondition(app.accountPanelMessage.contains("公网入口需要重试"))
        app.entryVerified = false
        precondition(!app.ready && app.accountPanelMessage.contains("公网入口需要重试"))
        app.entryVerified = true
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
        app.checkingPublic = false
        app.publicVerificationIssue = nil
        app.verificationState = nil
        app.window.setContentSize(NSSize(width: 920, height: 660))
        var buttonPositions: [[CGFloat]] = []
        for state in 0...3 {
            app.pendingID = state == 3 ? retryProject.id : nil
            if state >= 1 { app.active[retryProject.id] = .proxy(5173) }
            if state == 2 { _ = try app.slots.reserve(retryProject.id, publication: .path); app.slots.shares[retryProject.id]?.verified = true }
            if state == 3 { _ = app.slots.remove(retryProject.id) }
            let card = app.projectRow(retryProject, sharing: state >= 2)
            card.frame = NSRect(x: 0, y: 0, width: 700, height: 180)
            card.layoutSubtreeIfNeeded()
            let actions = findButtons(card)
            precondition(actions.count == 5)
            precondition(actions[1].title == "复制链接")
            precondition(actions[2].title == (state == 0 ? "启动项目" : "停止项目"))
            precondition(actions[3].title == (state == 2 ? "取消发布" : state == 3 ? "发布中" : "发布"))
            precondition(actions[1].isEnabled == (state == 2))
            for button in actions { precondition(button.image != nil && abs(button.frame.width - 36) < 0.1 && abs(button.frame.height - 36) < 0.1) }
            buttonPositions.append(actions.map { $0.convert($0.bounds, to: card).minX })
        }
        let longProject = app.catalog.add(.port(3000), name: String(repeating: "Long project name ", count: 10))
        let longCard = app.projectRow(longProject, sharing: false)
        longCard.frame = NSRect(x: 0, y: 0, width: 700, height: 180)
        longCard.layoutSubtreeIfNeeded()
        let longButtons = findButtons(longCard)
        buttonPositions.append(longButtons.map { $0.convert($0.bounds, to: longCard).minX })
        precondition(abs(buttonPositions[0][0] - 480) < 0.1)
        for positions in buttonPositions.dropFirst() {
            for (a, b) in zip(positions, buttonPositions[0]) { precondition(abs(a - b) < 0.1) }
        }
        app.pendingID = nil
        _ = app.catalog.add(.port(4321), name: "Second library project")
        _ = try app.slots.reserve(retryProject.id, publication: .path)
        app.showDashboard()
        for width in [920.0, 810.0] {
            app.window.setContentSize(NSSize(width: width, height: 660))
            app.window.contentView!.layoutSubtreeIfNeeded()
            app.window.contentView!.layoutSubtreeIfNeeded()
            let help = findButtons(app.window.contentView!).first { $0.title == "使用说明" }!
            precondition(help.frame.width < 180 && help.frame.width > 40)
        }
        func identified(_ view: NSView, _ name: String) -> NSView? {
            if view.identifier?.rawValue == name { return view }
            return view.subviews.lazy.compactMap { identified($0, name) }.first
        }
        func settle() { for _ in 0..<5 { app.window.contentView!.layoutSubtreeIfNeeded() } }
        func checkPage(_ maximum: CGFloat) {
            let root = app.window.contentView!
            let content = identified(root, "page-content")!
            let scroll = identified(root, "page-scroll")!
            let footer = identified(root, "page-footer")!
            precondition(content.frame.width <= maximum + 0.1)
            let viewport = scroll.convert(scroll.bounds, to: root)
            let bottomBar = footer.convert(footer.bounds, to: root)
            precondition(viewport.minY >= bottomBar.maxY + 11.9, "Footer overlaps scroll viewport")
            precondition(abs(bottomBar.midX - root.bounds.midX) < 1, "Footer not centered")
            precondition(bottomBar.minX >= 23.9 && bottomBar.maxX <= root.bounds.width - 23.9)
        }
        for size in [NSSize(width: 810, height: 553), NSSize(width: 920, height: 660),
                     NSSize(width: 1440, height: 900), NSSize(width: 1920, height: 1050),
                     NSSize(width: 1000, height: 575), NSSize(width: 810, height: 553)] {
            app.window.setContentSize(size); settle(); checkPage(1200)
            let sections = identified(app.window.contentView!, "project-sections") as! ResponsivePair
            precondition(sections.sideBySide == (size.width - 48 >= 1060))
            for card in app.shareList.arrangedSubviews { precondition(card.frame.height <= 190, "Project card stretched to match adjacent list") }
            let help = findButtons(app.window.contentView!).first { $0.title == "使用说明" }!
            precondition(help.frame.width > 40 && help.frame.width < 180)
        }
        // Narrow cards in the two-column view retain all actions on a second row.
        let compactCard = app.projectRow(longProject, sharing: false)
        compactCard.frame = NSRect(x: 0, y: 0, width: 540, height: 180)
        for _ in 0..<4 { compactCard.layoutSubtreeIfNeeded() }
        let compactButtons = findButtons(compactCard)
        for button in compactButtons {
            let rect = button.convert(button.bounds, to: compactCard)
            precondition(rect.minX >= 15 && rect.maxX <= 525)
            precondition(abs(rect.width - 36) < 0.1)
        }
        precondition(abs(compactButtons[0].convert(compactButtons[0].bounds, to: compactCard).minX - 320) < 0.1)
        app.setup = SetupProgress()
        app.setup.begin(.publicEntry)
        app.showOnboarding()
        app.window.setContentSize(NSSize(width: 810, height: 575))
        app.window.contentView!.layoutSubtreeIfNeeded()
        precondition(!app.setup.mayVerify)
        precondition(app.setupStateLabels[.account]!.stringValue != "已完成")
        app.detail = String(repeating: "详细错误与下一步指引，应完整换行显示。", count: 18)
        app.updateOnboarding()
        let preservedRetry = ObjectIdentifier(app.retry)
        let preservedMotion = ObjectIdentifier(app.setupActivity!)
        for size in [NSSize(width: 810, height: 553), NSSize(width: 920, height: 660),
                     NSSize(width: 1000, height: 575), NSSize(width: 1440, height: 900),
                     NSSize(width: 1920, height: 1050), NSSize(width: 810, height: 553)] {
            app.window.setContentSize(size); settle(); checkPage(1120)
            let columns = identified(app.window.contentView!, "setup-columns") as! ResponsivePair
            precondition(columns.sideBySide == (min(1120, size.width - 48) >= 932))
            precondition(ObjectIdentifier(app.retry) == preservedRetry && ObjectIdentifier(app.setupActivity!) == preservedMotion)
            precondition(app.detailText.frame.height > 70, "Long diagnostic text clipped")
            for stage in SetupStage.allCases { precondition(app.setupStateLabels[stage]!.frame.width > 70) }
        }
        print("Passed: responsive 810/920/1000/1440/1920 layouts, bounded centered canvas, non-overlapping footer, adaptive sections/cards/steps and preserved retry controls")
        let stableActivity = ObjectIdentifier(app.setupActivity!)
        for seconds in [8, 7, 6] {
            app.verificationState = .init(attempt: 3, retryInSeconds: seconds, lastError: "Fixture timeout")
            app.detail = app.verificationState!.text
            app.updateVerificationPresentation()
            precondition(ObjectIdentifier(app.setupActivity!) == stableActivity)
        }
        for name in ["eye", "external-link", "copy", "circle-play", "circle-stop", "cloud-upload", "cloud-off", "ellipsis"] {
            precondition(!SharedIcon.image(name).representations.isEmpty)
        }
        let publicProcess = try Child(URL(fileURLWithPath: "/bin/sleep"), ["30"])
        app.funnels[443] = publicProcess
        app.task = Task { try? await Task.sleep(for: .seconds(30)) }
        app.verificationActive = true
        app.stopCurrentVerification()
        precondition(app.funnels[443] == nil && !publicProcess.running)
        precondition(app.caddy?.running == true)
        precondition(app.setup[.publicEntry] == .paused)
        app.task?.cancel(); app.task = nil; app.verificationActive = false
        let preferenceDomain = "app.demomux.layout.preferences." + UUID().uuidString
        app.defaults = UserDefaults(suiteName: preferenceDomain)!
        defer { app.defaults.removePersistentDomain(forName: preferenceDomain) }
        let settingRoot = FileManager.default.temporaryDirectory.resolvingSymlinksInPath().appendingPathComponent("DemoMux-Settings-" + UUID().uuidString)
        try privateDirectory(settingRoot)
        defer { try? FileManager.default.removeItem(at: settingRoot) }
        app.maintenanceContext = .init(app: settingRoot.appendingPathComponent("DemoMux.app"), home: settingRoot.appendingPathComponent("Home"), preferenceDomain: preferenceDomain)
        app.settingsPanel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 720, height: 690), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        app.renderSettings()
        for width in [640.0, 720.0, 1200.0] {
            app.settingsPanel!.setContentSize(NSSize(width: width, height: 540))
            for _ in 0..<5 { app.settingsPanel!.contentView!.layoutSubtreeIfNeeded() }
            let root = app.settingsPanel!.contentView!
            let footer = identified(root, "page-footer")!
            let remove = findButtons(footer).first { $0.title == "卸载 DemoMux…" }!
            precondition(remove.frame.width > 80 && remove.isEnabled)
            let rect = footer.convert(footer.bounds, to: root)
            precondition(rect.width <= 860.1 && abs(rect.midX - root.bounds.midX) < 1)
        }
        let preference = NSButton(checkboxWithTitle: "fixture", target: nil, action: nil)
        preference.state = .off; app.changeOpenPreference(preference)
        precondition(app.defaults.object(forKey: "openGuideWhenReady") as? Bool == false)
        app.window.setContentSize(NSSize(width: 1000, height: 720)); app.rememberMainWindowSize()
        precondition(app.defaults.dictionary(forKey: "mainWindowSize") != nil)
        app.changeWindowPreference(preference)
        precondition(app.defaults.dictionary(forKey: "mainWindowSize") == nil && !app.remembersWindowSize)
        precondition(!app.settingsDiagnostics.contains("demo@example.test") && !app.settingsDiagnostics.contains("fixture.ts.net"))
        app.reviewUninstall()
        precondition(app.uninstallReview && app.uninstallDataCheckbox?.state == .on)
        app.uninstallDataCheckbox?.state = .off; app.changeUninstallData()
        precondition(app.uninstallDetailLabel!.stringValue.contains("保留项目列表"))
        app.renderSettings(); precondition(!app.uninstallReview && !app.uninstallRunning)
        // An app can move/change while cancellation is draining. A failed
        // post-cancellation identity check must leave the application usable.
        let appInfo = app.maintenanceContext.app.appendingPathComponent("Contents/Info.plist")
        try privateDirectory(appInfo.deletingLastPathComponent())
        try PropertyListSerialization.data(fromPropertyList: ["CFBundleIdentifier": MaintenanceContext.bundleID, "CFBundleExecutable": "DemoMux"], format: .xml, options: 0).write(to: appInfo)
        app.timer = Timer.scheduledTimer(withTimeInterval: 100, repeats: true) { _ in }
        app.reviewUninstall()
        app.task = Task {
            try? await Task.sleep(for: .seconds(30))
            try? FileManager.default.removeItem(at: appInfo)
        }
        app.confirmUninstall()
        while app.maintenanceTask != nil { try await Task.sleep(for: .milliseconds(20)) }
        precondition(!app.uninstallRunning && !app.uninstallCommitted)
        precondition(app.timer?.isValid == true, "Uninstall failure disabled status monitoring")
        precondition(app.uninstallStatusLabel!.stringValue.contains("无法确认"))
        app.timer?.invalidate(); app.task = nil
        print("Passed: uninstall identity race after cancellation reports failure, preserves monitoring and leaves app recoverable")
        app.uninstallCommitted = true; app.rememberMainWindowSize()
        precondition(app.defaults.dictionary(forKey: "mainWindowSize") == nil)
        print("Passed: settings 640/720/1200 layouts, always-visible uninstall, preference persistence, diagnostic privacy, single review/keep-data controls and no writes after uninstall")
        print("Passed: stop setup verification withdraws bootstrap Funnel, preserves private preview and marks the current step paused")
        print("Passed: fixed five-slot SVG actions across stopped/running/published/verifying states, disabled copy slot, minimum setup and stable countdown activity view")
        print("Passed: account layout, minimum sizing, inline confirmation, identity guard, project error isolation, stale detail suppression and public verification status")
        print("Passed: retry clears stale error before delayed identity lookup, reserves visible pending state and releases it on cancellation")
    }
}
