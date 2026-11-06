import AppKit
import Darwin

@MainActor
final class DemoMuxWindow: NSObject, NSApplicationDelegate {
    private let defaults = UserDefaults.standard
    private let localURL = URL(string: "http://127.0.0.1:\(previewPort)/")!
    private var window: NSWindow!
    private var runtimeDirectory: URL!
    private var lockFD: Int32 = -1
    private var caddy: Child?
    private var tailscaled: Child?
    private var login: Child?
    private var funnel: Child?
    private var task: Task<Void, Never>?
    private var monitor: Timer?
    private var authorizationURL: URL?
    private var publicURL: URL?
    private var installedCaddy = false
    private var installedTailscale = false
    private var route = Route.sample
    private var sessionPorts: [Int] = []
    private var operationMessage: String?
    private var sharingPending = false

    private let localState = NSTextField(labelWithString: "")
    private let publicState = NSTextField(labelWithString: "")
    private let routeState = NSTextField(labelWithString: "")
    private let message = NSTextField(wrappingLabelWithString: "")
    private let portField = NSTextField(string: "")
    private let sessionMenu = NSPopUpButton(frame: .zero, pullsDown: false)
    private var installButton: NSButton!
    private var startButton: NSButton!
    private var stopButton: NSButton!
    private var localButton: NSButton!
    private var applyButton: NSButton!
    private var sampleButton: NSButton!
    private var publishButton: NSButton!
    private var unpublishButton: NSButton!
    private var authorizationButton: NSButton!
    private var copyButton: NSButton!

    private var busy: Bool { task != nil }
    private var adminSocket: URL { runtimeDirectory.appendingPathComponent("caddy.sock") }
    private var tsSocket: URL { runtimeDirectory.appendingPathComponent("tailscale.sock") }
    private var sample: URL { Bundle.main.resourceURL!.appendingPathComponent("Sample") }

    func applicationDidFinishLaunching(_ notification: Notification) {
        do {
            guard ProcessInfo.processInfo.operatingSystemVersion.majorVersion == 26 else {
                throw AppError("此试用版需要 Apple 芯片和 macOS 26。")
            }
            try privateDirectory(support)
            lockFD = open(support.appendingPathComponent("instance.lock").path,
                          O_CREAT | O_RDWR | O_NOFOLLOW, S_IRUSR | S_IWUSR)
            guard lockFD >= 0, flock(lockFD, LOCK_EX | LOCK_NB) == 0 else {
                throw AppError("DemoMux 已在运行，或无法打开应用数据目录。")
            }
            _ = fcntl(lockFD, F_SETFD, FD_CLOEXEC)
            runtimeDirectory = URL(fileURLWithPath: "/tmp/demomux-\(UUID().uuidString)", isDirectory: true)
            try privateDirectory(runtimeDirectory)
        } catch {
            let alert = NSAlert(error: error)
            alert.runModal()
            NSApplication.shared.terminate(nil)
            return
        }

        let saved = defaults.integer(forKey: "projectPort")
        if (1...65535).contains(saved), saved != previewPort {
            route = .project(saved)
            portField.stringValue = String(saved)
        }

        buildWindow()
        refresh()
        beginOperation("正在检查所需组件…") {
            let ready = await Task.detached(priority: .utility) {
                (Dependency.caddy.installed, Dependency.tailscale.installed)
            }.value
            self.installedCaddy = ready.0
            self.installedTailscale = ready.1
        }
        monitor = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.checkProcesses() }
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationWillTerminate(_ notification: Notification) {
        monitor?.invalidate()
        task?.cancel()
        stopSharing()
        caddy?.stop()
        if let runtimeDirectory { try? FileManager.default.removeItem(at: runtimeDirectory) }
        if lockFD >= 0 { close(lockFD) }
    }

    private func buildWindow() {
        let menu = NSMenu()
        let appItem = NSMenuItem()
        menu.addItem(appItem)
        let appMenu = NSMenu()
        appMenu.addItem(NSMenuItem(title: "退出 DemoMux", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        appItem.submenu = appMenu
        let editItem = NSMenuItem()
        menu.addItem(editItem)
        let edit = NSMenu(title: "编辑")
        for (name, action, key) in [
            ("剪切", "cut:", "x"), ("复制", "copy:", "c"),
            ("粘贴", "paste:", "v"), ("全选", "selectAll:", "a"),
        ] {
            edit.addItem(NSMenuItem(title: name, action: NSSelectorFromString(action), keyEquivalent: key))
        }
        editItem.submenu = edit
        NSApplication.shared.mainMenu = menu

        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 675, height: 505),
                          styleMask: [.titled, .closable, .miniaturizable], backing: .buffered, defer: false)
        window.title = "DemoMux"
        window.isReleasedWhenClosed = false
        let title = NSTextField(labelWithString: "DemoMux")
        title.font = .systemFont(ofSize: 33, weight: .bold)
        let subtitle = NSTextField(labelWithString: "本机项目预览与公网演示")
        subtitle.textColor = .secondaryLabelColor

        installButton = button("安装所需组件", #selector(installLocal))
        startButton = button("启动本机预览", #selector(startLocal))
        stopButton = button("停止", #selector(stopLocal))
        localButton = button("打开本机预览", #selector(openLocal))
        applyButton = button("切换到此端口", #selector(applyPort))
        sampleButton = button("恢复内置示例", #selector(restoreSample))
        publishButton = button("启用公网分享", #selector(publish))
        unpublishButton = button("停止公网分享", #selector(unpublish))
        authorizationButton = button("打开授权网页", #selector(openAuthorization))
        copyButton = button("复制链接", #selector(copyLink))

        portField.placeholderString = "如 5174"
        portField.setAccessibilityLabel("项目端口")
        portField.widthAnchor.constraint(equalToConstant: 82).isActive = true
        portField.target = self
        portField.action = #selector(applyPort)
        sessionMenu.setAccessibilityLabel("本次会话端口")
        sessionMenu.target = self
        sessionMenu.action = #selector(selectSessionPort)
        rebuildSessionMenu()
        routeState.textColor = .secondaryLabelColor
        message.textColor = .secondaryLabelColor
        message.maximumNumberOfLines = 4
        message.font = .systemFont(ofSize: 12)
        publicState.lineBreakMode = .byTruncatingMiddle
        publicState.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        let content = NSStackView(views: [
            title, subtitle, separator(),
            horizontal([heading("本机预览"), localState]),
            horizontal([installButton, startButton, stopButton, localButton]),
            horizontal([NSTextField(labelWithString: "项目端口"), portField, applyButton, sampleButton]),
            horizontal([NSTextField(labelWithString: "本次会话"), sessionMenu]),
            routeState, separator(),
            horizontal([heading("公网分享"), publicState]),
            horizontal([publishButton, unpublishButton, authorizationButton, copyButton]),
            message,
        ])
        content.orientation = .vertical
        content.alignment = .leading
        content.spacing = 11
        content.translatesAutoresizingMaskIntoConstraints = false
        let root = NSView()
        root.addSubview(content)
        window.contentView = root
        NSLayoutConstraint.activate([
            content.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 24),
            content.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -24),
            content.topAnchor.constraint(equalTo: root.topAnchor, constant: 22),
            content.bottomAnchor.constraint(lessThanOrEqualTo: root.bottomAnchor, constant: -18),
            message.widthAnchor.constraint(equalTo: content.widthAnchor),
        ])
        for case let line as NSBox in content.views {
            line.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true
        }
        window.initialFirstResponder = portField
        window.center()
        window.makeKeyAndOrderFront(nil)
        NSApplication.shared.activate(ignoringOtherApps: true)
    }

    private func button(_ title: String, _ action: Selector) -> NSButton {
        NSButton(title: title, target: self, action: action)
    }

    private func heading(_ title: String) -> NSTextField {
        let field = NSTextField(labelWithString: title)
        field.font = .systemFont(ofSize: 16, weight: .semibold)
        return field
    }

    private func horizontal(_ views: [NSView]) -> NSStackView {
        let row = NSStackView(views: views)
        row.spacing = 8
        return row
    }

    private func separator() -> NSBox {
        let line = NSBox()
        line.boxType = .separator
        return line
    }

    private func refresh() {
        let localRunning = caddy?.running == true
        localState.stringValue = localRunning ? "运行中" : installedCaddy ? "组件就绪 · 已停止" : "需要 Caddy"
        if let publicURL {
            publicState.stringValue = publicURL.absoluteString
        } else if authorizationURL != nil {
            publicState.stringValue = "等待网页授权"
        } else if sharingPending {
            publicState.stringValue = "正在连接"
        } else {
            publicState.stringValue = installedTailscale ? "组件就绪 · 已停止" : "按需安装 Tailscale"
        }
        routeState.stringValue = "当前展示 \(route.label) · 127.0.0.1:\(previewPort)"
        message.stringValue = operationMessage ?? "手动启动。退出 DemoMux 时会停止本机预览和公网分享。"
        installButton.isEnabled = !busy && !installedCaddy
        startButton.isEnabled = !busy && installedCaddy && !localRunning
        stopButton.isEnabled = !busy && localRunning
        localButton.isEnabled = localRunning
        applyButton.isEnabled = !busy
        sampleButton.isEnabled = !busy && route != .sample
        portField.isEnabled = !busy
        sessionMenu.isEnabled = !busy && !sessionPorts.isEmpty
        publishButton.isEnabled = !busy && localRunning && publicURL == nil
        unpublishButton.isEnabled = sharingPending || publicURL != nil
        authorizationButton.isEnabled = authorizationURL != nil
        copyButton.isEnabled = publicURL != nil
    }

    private func beginOperation(_ progress: String, sharing: Bool = false,
                         _ operation: @escaping @MainActor () async throws -> Void) {
        guard !busy else { return }
        operationMessage = progress
        sharingPending = sharing
        task = Task { [weak self] in
            guard let self else { return }
            do {
                try await operation()
                self.operationMessage = nil
            } catch is CancellationError {
                self.operationMessage = "操作已取消。"
            } catch {
                // Login links are displayed only through the authorization button.
                self.operationMessage = self.safeMessage(error.localizedDescription)
            }
            self.task = nil
            self.sharingPending = false
            self.refresh()
        }
        refresh()
    }

    private func safeMessage(_ text: String) -> String {
        let redacted = text.replacingOccurrences(of: #"https://(?:login|console)\.tailscale\.com/\S+"#,
                                                  with: "[Tailscale 授权链接]", options: .regularExpression)
        return String(redacted.prefix(500)).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private func checkProcesses() {
        guard !busy else { return }
        if let caddy, !caddy.running {
            self.caddy = nil
            stopSharing()
            operationMessage = "Caddy 已退出。\(safeMessage(caddy.output))"
        } else if let funnel, !funnel.running || tailscaled?.running != true {
            stopSharing()
            operationMessage = "公网分享已断开。"
        }
        refresh()
    }

    @objc private func installLocal() {
        beginOperation("正在下载和校验 Caddy…") {
            try await Dependency.caddy.install()
            self.installedCaddy = true
        }
    }

    @objc private func startLocal() {
        beginOperation("正在启动本机预览…") {
            let selected = self.route
            try await Task.detached {
                try checkPreviewPort()
                if case let .project(port) = selected { try checkProject(port) }
            }.value
            try Task.checkCancellation()
            let config = self.runtimeDirectory.appendingPathComponent("preview.json")
            try caddyConfig(for: selected, socket: self.adminSocket, sample: self.sample)
                .write(to: config, options: .atomic)
            var environment = ProcessInfo.processInfo.environment
            environment["XDG_DATA_HOME"] = support.appendingPathComponent("CaddyData").path
            environment["XDG_CONFIG_HOME"] = support.appendingPathComponent("CaddyConfig").path
            let process = try Child(Dependency.caddy.executable("caddy"),
                                    ["run", "--config", config.path], environment: environment)
            self.caddy = process
            do {
                for _ in 0..<25 {
                    try await Task.sleep(for: .milliseconds(120))
                    guard process.running else { throw AppError("Caddy 启动失败。\(process.output)") }
                    if (try? curl(["--unix-socket", self.adminSocket.path, "--fail",
                                   "--output", "/dev/null", "http://localhost/config/"])) != nil {
                        return
                    }
                }
                throw AppError("本机预览启动超时。")
            } catch {
                process.stop()
                self.caddy = nil
                throw error
            }
        }
    }

    @objc private func stopLocal() {
        stopSharing()
        caddy?.stop()
        caddy = nil
        operationMessage = nil
        refresh()
    }

    @objc private func openLocal() { NSWorkspace.shared.open(localURL) }

    private func rebuildSessionMenu() {
        sessionMenu.removeAllItems()
        sessionMenu.addItem(withTitle: sessionPorts.isEmpty ? "暂无切换记录" : "快速切换端口")
        sessionMenu.lastItem?.isEnabled = false
        for port in sessionPorts {
            sessionMenu.addItem(withTitle: String(port))
            sessionMenu.lastItem?.tag = port
        }
        sessionMenu.selectItem(at: 0)
    }

    @objc private func applyPort() {
        let input = portField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let port = Int(input), (1...65535).contains(port), port != previewPort else {
            operationMessage = "请输入有效的项目端口。\(previewPort) 是 DemoMux 的预览入口。"
            refresh()
            return
        }
        changeRoute(.project(port))
    }

    @objc private func selectSessionPort() {
        guard let port = sessionMenu.selectedItem?.tag, port > 0 else { return }
        sessionMenu.selectItem(at: 0)
        changeRoute(.project(port))
    }

    @objc private func restoreSample() { changeRoute(.sample) }

    private func changeRoute(_ next: Route) {
        guard route != next else { return }
        beginOperation("正在切换展示来源…") {
            let socket = self.adminSocket
            let sample = self.sample
            let candidate = self.runtimeDirectory.appendingPathComponent("candidate.json")
            let running = self.caddy?.running == true
            try await Task.detached {
                if case let .project(port) = next { try checkProject(port) }
                if running {
                    try caddyConfig(for: next, socket: socket, sample: sample).write(to: candidate, options: .atomic)
                    _ = try command(Dependency.caddy.executable("caddy"),
                                    ["reload", "--config", candidate.path, "--address", "unix/\(socket.path)"])
                }
            }.value
            self.route = next
            switch next {
            case .sample:
                self.defaults.removeObject(forKey: "projectPort")
                self.portField.stringValue = ""
            case .project(let port):
                self.defaults.set(port, forKey: "projectPort")
                self.sessionPorts.removeAll { $0 == port }
                self.sessionPorts.insert(port, at: 0)
                self.portField.stringValue = String(port)
                self.rebuildSessionMenu()
            }
        }
    }

    @objc private func publish() {
        guard caddy?.running == true else { return }
        beginOperation("正在准备 Tailscale 公网分享…", sharing: true) {
            do {
                try await self.startSharing()
            } catch {
                self.stopSharing()
                throw error
            }
        }
    }

    private func startSharing() async throws {
        if !installedTailscale {
            operationMessage = "正在下载和校验 Tailscale…"
            refresh()
            try await Dependency.tailscale.install()
            installedTailscale = true
        }
        try Task.checkCancellation()
        let state = support.appendingPathComponent("Tailscale", isDirectory: true)
        try privateDirectory(state)
        let daemon = try Child(Dependency.tailscale.executable("tailscaled"), [
            "--tun=userspace-networking", "--statedir=\(state.path)", "--socket=\(tsSocket.path)", "--port=0",
        ])
        tailscaled = daemon
        var ready = false
        for _ in 0..<30 {
            try await Task.sleep(for: .milliseconds(150))
            guard daemon.running else { throw AppError("Tailscale 启动失败。\(daemon.output)") }
            if FileManager.default.fileExists(atPath: tsSocket.path),
               (try? curl(["--unix-socket", tsSocket.path, "--fail", "--output", "/dev/null",
                           "http://local-tailscaled.sock/localapi/v0/status"])) != nil {
                ready = true
                break
            }
        }
        guard ready else { throw AppError("Tailscale 服务响应超时。") }

        let identity: String
        if let saved = defaults.string(forKey: "nodeSuffix") {
            identity = saved
        } else {
            identity = String(UUID().uuidString.prefix(8)).lowercased()
            defaults.set(identity, forKey: "nodeSuffix")
        }
        let cli = Dependency.tailscale.executable("tailscale")
        let socketFlag = "--socket=\(tsSocket.path)"
        let authentication = try Child(cli, [socketFlag, "up", "--accept-dns=false", "--accept-routes=false",
                                             "--hostname=demomux-\(identity)", "--timeout=5m"])
        login = authentication
        try await waitForCommand(authentication, progress: "请在浏览器中登录 Tailscale。")
        login = nil
        authorizationURL = nil
        operationMessage = "正在开启 Funnel…"
        refresh()

        let share = try Child(cli, [socketFlag, "funnel", "--https=443", "http://127.0.0.1:\(previewPort)"])
        funnel = share
        let deadline = Date().addingTimeInterval(300)
        while Date() < deadline {
            try await Task.sleep(for: .milliseconds(200))
            let output = share.output
            if output.contains("Available on the internet:"),
               let url = firstURL(in: output, where: { $0.host?.hasSuffix(".ts.net") == true }),
               share.running {
                publicURL = url
                authorizationURL = nil
                return
            }
            guard share.running else {
                throw AppError("Funnel 未启动。\(output)")
            }
            updateAuthorization(from: output, progress: "请在浏览器中启用 Funnel。")
        }
        throw AppError("Funnel 等待超时。")
    }

    private func waitForCommand(_ process: Child, progress: String) async throws {
        while process.running {
            try await Task.sleep(for: .milliseconds(200))
            updateAuthorization(from: process.output, progress: progress)
        }
        try await Task.sleep(for: .milliseconds(50))
        guard process.process.terminationStatus == 0 else {
            throw AppError("Tailscale 登录未完成。\(process.output)")
        }
    }

    private func updateAuthorization(from output: String, progress: String) {
        guard let url = firstURL(in: output, where: {
            $0.host == "login.tailscale.com" || $0.host == "console.tailscale.com"
        }), authorizationURL != url else { return }
        authorizationURL = url
        operationMessage = progress
        refresh()
    }

    private func stopSharing() {
        funnel?.stop()
        login?.stop()
        tailscaled?.stop()
        funnel = nil
        login = nil
        tailscaled = nil
        publicURL = nil
        authorizationURL = nil
    }

    @objc private func unpublish() {
        if sharingPending { task?.cancel() }
        stopSharing()
        operationMessage = nil
        refresh()
    }

    @objc private func openAuthorization() {
        if let authorizationURL { NSWorkspace.shared.open(authorizationURL) }
    }

    @objc private func copyLink() {
        guard let publicURL else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(publicURL.absoluteString, forType: .string)
        operationMessage = "已复制公网链接。"
        refresh()
    }
}

MainActor.assumeIsolated {
    let app = NSApplication.shared
    let controller = DemoMuxWindow()
    app.setActivationPolicy(.regular)
    app.delegate = controller
    app.run()
}
