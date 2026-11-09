import AppKit

extension DemoMuxWindow {
    func settingsButton() -> NSButton {
        let control = button("设置", #selector(openSettings))
        control.image = NSImage(systemSymbolName: "gearshape", accessibilityDescription: nil)
        control.imagePosition = .imageLeading
        control.toolTip = "演示偏好、组件与存储、卸载 DemoMux（⌘,）"
        return control
    }

    var remembersWindowSize: Bool { defaults.object(forKey: "rememberWindowSize") as? Bool ?? true }

    func restoreMainWindowSize() {
        guard remembersWindowSize, let stored = defaults.dictionary(forKey: "mainWindowSize"),
              let width = stored["width"] as? Double, let height = stored["height"] as? Double,
              width.isFinite, height.isFinite, width >= 810, height >= 553 else { return }
        let available = window.contentRect(forFrameRect: NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)).size
        window.setContentSize(NSSize(width: min(width, max(810, available.width)),
                                     height: min(height, max(553, available.height))))
    }

    func rememberMainWindowSize() {
        guard !uninstallCommitted, remembersWindowSize, let size = window?.contentView?.bounds.size else { return }
        defaults.set(["width": Double(size.width), "height": Double(size.height)], forKey: "mainWindowSize")
    }
    @objc func changeWindowPreference(_ sender: NSButton) {
        defaults.set(sender.state == .on, forKey: "rememberWindowSize")
        if sender.state == .off { defaults.removeObject(forKey: "mainWindowSize") }
        else { rememberMainWindowSize() }
    }

    @objc func openSettings() {
        if settingsPanel == nil {
            let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 720, height: 690),
                                styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
            panel.title = "DemoMux · 设置"; panel.appearance = NSAppearance(named: .darkAqua)
            panel.minSize = NSSize(width: 640, height: 540)
            panel.isReleasedWhenClosed = false; panel.delegate = self
            panel.center(); settingsPanel = panel
        }
        if !settingsPanel!.isVisible { renderSettings() }
        settingsPanel!.makeKeyAndOrderFront(nil)
    }

    func settingsParagraph(_ text: String, size: CGFloat = 12) -> NSTextField {
        let view = NSTextField(wrappingLabelWithString: text)
        view.font = .systemFont(ofSize: size); view.textColor = GuideTheme.muted
        return view
    }

    func settingsCard(_ title: String, _ views: [NSView]) -> NSView {
        let body = column([label(title, size: 15, weight: .semibold)] + views, spacing: 12)
        for view in views where !(view is NSButton) { view.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true }
        return accountCard(body, padding: 18)
    }

    @objc func renderSettings() {
        guard let panel = settingsPanel, !uninstallRunning else { return }
        uninstallReview = false
        let eyebrow = label(isolatedPreviewNotice ?? "DEMOMUX  /  SETTINGS", size: 11, weight: .semibold)
        eyebrow.textColor = GuideTheme.accent
        let title = label("按你的方式演示。", size: 28, weight: .bold)
        let open = NSButton(checkboxWithTitle: "公网就绪后，在默认浏览器打开首页", target: self, action: #selector(changeOpenPreference))
        open.state = (defaults.object(forKey: "openGuideWhenReady") as? Bool ?? true) ? .on : .off
        let remember = NSButton(checkboxWithTitle: "记住主窗口的大小", target: self, action: #selector(changeWindowPreference))
        remember.state = remembersWindowSize ? .on : .off
        let general = settingsCard("演示偏好", [open, remember,
            settingsParagraph("手动打开应用才会连接；不添加开机启动项。关闭主窗口会退出应用，并停止它启动的组件。")])
        var components: [NSView] = []
        for item in Dependency.allCases {
            let path = maintenanceContext.components.appendingPathComponent("\(item.name)-\(item.version)")
            let downloaded = FileManager.default.fileExists(atPath: path.path)
            components.append(settingsParagraph("\(item.name)  \(item.version) · \(downloaded ? "已下载" : "尚未下载")"))
        }
        let bytes = managedStorageSize(maintenanceContext.data)
        components.append(settingsParagraph("DemoMux 数据占用  \(ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file))\n仅统计应用私有目录；启动时会校验组件完整性。"))
        let reveal = button("打开数据文件夹", #selector(revealSettingsData))
        reveal.image = NSImage(systemSymbolName: "folder", accessibilityDescription: nil); reveal.imagePosition = .imageLeading
        components.append(reveal)
        let storage = settingsCard("组件与存储", components)
        let diagnostic = button("复制诊断摘要", #selector(copySettingsDiagnostics))
        diagnostic.image = SharedIcon.image("copy"); diagnostic.imagePosition = .imageLeading
        settingsMessageLabel = settingsParagraph(settingsMessage)
        let supportCard = settingsCard("支持与诊断", [settingsParagraph("复制版本、组件和连接状态，便于排查问题。摘要不包含账号、项目名称、文件路径或登录链接。"),
                                                        diagnostic, settingsMessageLabel!])
        let remove = button("卸载 DemoMux…", #selector(reviewUninstall))
        remove.contentTintColor = GuideTheme.link
        remove.image = NSImage(systemSymbolName: "trash", accessibilityDescription: nil); remove.imagePosition = .imageLeading
        let body = column([eyebrow, title, general, storage, supportCard], spacing: 18)
        for view in [general, storage, supportCard] { view.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true }
        let guide = button("使用说明", #selector(openGuidePage))
        let version = label("DemoMux \(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "QA")", size: 11)
        version.textColor = GuideTheme.muted
        let footer = glass(row([guide, NSView(), version, NSView(), remove], spacing: 10), radius: 18, x: 13, y: 10)
        panel.contentView = responsivePage(content: body, footer: footer, maximumWidth: 860)
    }

    @objc func revealSettingsData() {
        NSWorkspace.shared.selectFile(nil, inFileViewerRootedAtPath: maintenanceContext.data.path)
    }
    var settingsDiagnostics: String {
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "QA"
        let system = ProcessInfo.processInfo.operatingSystemVersion
        return "DemoMux \(version)\nmacOS \(system.majorVersion).\(system.minorVersion).\(system.patchVersion) · arm64\nCaddy \(Dependency.caddy.version)\nTailscale \(Dependency.tailscale.version)\n状态：\(publicStatusText)\n公网发布：\(usedSlots) / 3\n打开首页：\((defaults.object(forKey: "openGuideWhenReady") as? Bool ?? true) ? "开启" : "关闭")"
    }
    @objc func copySettingsDiagnostics() {
        NSPasteboard.general.clearContents()
        settingsMessage = NSPasteboard.general.setString(settingsDiagnostics, forType: .string) ? "诊断摘要已复制。" : "未能写入剪贴板，请重试。"
        settingsMessageLabel?.stringValue = settingsMessage
    }

    @objc func reviewUninstall() {
        guard let panel = settingsPanel, !uninstallRunning else { return }
        uninstallReview = true
        let eyebrow = label(isolatedPreviewNotice ?? "DEMOMUX  /  应用维护", size: 11, weight: .semibold)
        eyebrow.textColor = GuideTheme.accent
        let title = label("卸载 DemoMux", size: 28, weight: .bold)
        let explanation = settingsParagraph("确认后会结束当前操作，停止本次分享和私有组件，将下列内容移到废纸篓，然后退出应用。")
        let scope = settingsCard("卸载范围", [settingsParagraph("• 当前 DemoMux.app\n• DemoMux 下载的所有版本 Caddy、Tailscale\n• Caddy 的运行数据"),
            settingsParagraph("原始 HTML、项目文件夹和你已启动的本机项目保留。独立安装的系统版 Caddy、Tailscale 不受影响。")])
        uninstallDataCheckbox = NSButton(checkboxWithTitle: "同时清除项目列表、本机登录、偏好设置和应用缓存", target: self, action: #selector(changeUninstallData))
        uninstallDataCheckbox!.state = .on
        uninstallDetailLabel = settingsParagraph("")
        let options = settingsCard("应用数据", [uninstallDataCheckbox!, uninstallDetailLabel!])
        uninstallStatusLabel = settingsParagraph("")
        uninstallStatusLabel!.textColor = GuideTheme.link
        let body = column([eyebrow, title, explanation, scope, options, uninstallStatusLabel!], spacing: 18)
        for view in [explanation, scope, options, uninstallStatusLabel!] { view.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true }
        let cancel = button("返回设置", #selector(renderSettings))
        uninstallBackButton = cancel
        uninstallConfirmButton = button("停止并卸载", #selector(confirmUninstall), primary: true)
        uninstallConfirmButton!.image = NSImage(systemSymbolName: "trash", accessibilityDescription: nil)
        uninstallConfirmButton!.imagePosition = .imageLeading
        let toolbar = glass(row([cancel, NSView(), uninstallConfirmButton!]), radius: 18, x: 13, y: 10)
        panel.contentView = responsivePage(content: body, footer: toolbar, maximumWidth: 860)
        changeUninstallData()
    }

    @objc func changeUninstallData() {
        let remove = uninstallDataCheckbox?.state == .on
        uninstallDetailLabel?.stringValue = remove
            ? "将清除 DemoMux 的本机账号与项目记录。Tailscale 在线账号本身不会被删除；云端设备记录可在 Tailscale 管理页自行移除。"
            : "保留项目列表、Tailscale 本机登录与偏好设置。重新安装时需重新下载组件，随后可继续使用这些记录。"
    }

    @objc func confirmUninstall() {
        guard uninstallReview, !uninstallRunning else { return }
        let remove = uninstallDataCheckbox?.state == .on
        do { _ = try maintenanceContext.plan(removingData: remove) }
        catch { uninstallStatusLabel?.stringValue = error.localizedDescription; return }
        uninstallRunning = true
        uninstallConfirmButton?.isEnabled = false; uninstallDataCheckbox?.isEnabled = false
        uninstallBackButton?.isEnabled = false
        settingsPanel?.standardWindowButton(.closeButton)?.isEnabled = false
        uninstallStatusLabel?.stringValue = "正在结束当前任务并停止分享与组件…"
        let pending = task
        pending?.cancel(); accountProbe?.cancel(); portProbe?.cancel()
        timer?.invalidate()
        maintenanceTask = Task {
            await pending?.value
            self.stopServices()
            let domain = self.maintenanceContext.preferenceDomain
            let previous = self.defaults.persistentDomain(forName: domain)
            do {
                _ = try Uninstaller.execute(self.maintenanceContext, removingData: remove) {
                    self.defaults.removePersistentDomain(forName: domain)
                    guard self.defaults.synchronize() else {
                        if let previous { self.defaults.setPersistentDomain(previous, forName: domain) }
                        throw AppError("偏好设置未能写入，请重试。")
                    }
                }
                self.uninstallCommitted = true
                NSApplication.shared.terminate(nil)
            } catch {
                self.uninstallRunning = false
                self.uninstallStatusLabel?.stringValue = error.localizedDescription
                self.uninstallConfirmButton?.isEnabled = true; self.uninstallDataCheckbox?.isEnabled = true
                self.uninstallBackButton?.isEnabled = true
                self.settingsPanel?.standardWindowButton(.closeButton)?.isEnabled = true
                self.notice = "卸载未完成，演示已停止。请在设置中查看原因。"
                if self.dashboard { self.refreshDashboard() } else { self.updateOnboarding() }
            }
            self.maintenanceTask = nil
        }
    }

    func windowDidEndLiveResize(_ notification: Notification) {
        if let resized = notification.object as? NSWindow, resized === window { rememberMainWindowSize() }
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool { !uninstallRunning }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        uninstallRunning && !uninstallCommitted ? .terminateCancel : .terminateNow
    }
}
