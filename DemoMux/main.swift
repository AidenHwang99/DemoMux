import AppKit
import Darwin
import WebKit

@MainActor
final class DemoMuxWindow: NSObject, NSApplicationDelegate, WKNavigationDelegate {
    var defaults = UserDefaults.standard
    var window: NSWindow!
    var runtimeDirectory: URL!
    var lockFD: Int32 = -1
    var catalog = Catalog()
    var catalogError: String?
    var active: [UUID: Content] = [:]
    var slots = PublicationSlots()
    var pendingID: UUID?
    var diagnostics: [UUID: String] = [:]
    var sessionPorts: [UUID] = []
    var caddy: Child?
    var tailscaled: Child?
    var login: Child?
    var funnels: [Int: Child] = [:]
    var primaryCandidate: String?
    var publicHost: String?
    var localPort: Int?
    var roots: [Int: Int] = [:]
    var guideMarker = UUID().uuidString
    var task: Task<Void, Never>?
    var timer: Timer?
    var keyboardMonitor: Any?
    var portProbe: Task<Void, Never>?
    var authorizationURL: URL?
    var authorizationLabel = "登录 Tailscale"
    var dashboard = false
    var phase = "准备 Caddy"
    var detail = "正在检查本机预览组件。"
    var notice = ""
    var verificationProgress: String?
    var publicVerificationIssue: String?
    var checkingPublic = false
    var entryVerified = false
    var setup = SetupProgress()
    var setupContext: SetupVerificationContext?
    var verificationState: VerificationProgress?
    var verificationActive = false
    var verificationStopped = false
    var operationNeedsRetry = false
    var projectVerificationLabel: NSTextField?
    var retryOperation: (@MainActor () async throws -> Void)?
    var setupStateLabels: [SetupStage: NSTextField] = [:]
    var setupNumberLabels: [SetupStage: NSTextField] = [:]
    var setupActivity: VerificationActivityView?
    var dashboardActivity: VerificationActivityView?
    var accountInformationLabel: NSTextField?
    var accountBadgeLabel: NSTextField?
    var retryDashboardButton: NSButton?
    var cancelDashboardButton: NSButton?
    var checkPublicButton: NSButton?
    var sessionActionButton: NSButton?
    var setupCancelButton: NSButton?
    let phaseText = NSTextField(labelWithString: "")
    let detailText = NSTextField(wrappingLabelWithString: "")
    let authorization = NSButton()
    let retry = NSButton()
    let activity = NSProgressIndicator()
    var headerText: NSTextField!
    var dashboardNotice: NSTextField!
    var projectList: NSStackView!
    var shareList: NSStackView!
    var quickSwitch: NSPopUpButton!
    var commandButtons: [NSControl] = []
    var accountSnapshot = AccountSnapshot()
    var accountClientForTesting: AccountClient?
    var publicProbeForTesting: ((URL, String, Bool, URL?) async throws -> Void)?
    var isolatedPreviewNotice: String?
    var verifiedProfileID: String?
    var accountProbe: Task<Void, Never>?
    var accountAction: AccountAction?
    var pendingAccountAction: AccountAction?
    var pendingAccountProfileID: String?
    var accountMessage = ""
    var accountPanel: NSPanel?
    var accountPanelBody: NSView?
    var accountSummary: NSTextField?
    var onboardingAccountSummary: NSTextField?
    var accountCancelRequested = false
    var accountRenderState: AccountPanelState?
    var helpWindow: NSWindow?

    var guide: URL { Bundle.main.resourceURL!.appendingPathComponent("Sample", isDirectory: true) }
    var adminSocket: URL { runtimeDirectory.appendingPathComponent("caddy.sock") }
    var tsSocket: URL { runtimeDirectory.appendingPathComponent("tailscale.sock") }
    var busy: Bool { task != nil }
    var servicesAvailable: Bool { (publicHost ?? primaryCandidate) != nil && caddy?.running == true && funnels[443]?.running == true }
    var ready: Bool { servicesAvailable && entryVerified && verifiedProfileID != nil }
    var publicStatusText: String {
        if verificationActive { return verificationState?.retryInSeconds != nil ? "正在自动重试" : "正在检测公网" }
        if !servicesAvailable { return "演示已停止" }
        if checkingPublic { return "正在检测公网" }
        return ready && publicVerificationIssue == nil && slots.shares.values.allSatisfy(\.verified) ? "入口已验证" : "公网待复核"
    }
    var guideURL: URL? { publicHost.flatMap { URL(string: "https://\($0)\(guidePath)") } }
    var usedSlots: Int { slots.shares.count + (pendingID == nil ? 0 : 1) }

    func applicationDidFinishLaunching(_ notification: Notification) {
        do {
            guard ProcessInfo.processInfo.operatingSystemVersion.majorVersion == 26 else {
                throw AppError("此试用版需要 Apple 芯片和 macOS 26。")
            }
            try privateDirectory(support)
            accountSnapshot = AccountCache.load()
            lockFD = open(support.appendingPathComponent("instance.lock").path,
                          O_CREAT | O_RDWR | O_NOFOLLOW, S_IRUSR | S_IWUSR)
            guard lockFD >= 0, flock(lockFD, LOCK_EX | LOCK_NB) == 0 else {
                throw AppError("DemoMux 已在运行，或无法打开应用数据目录。")
            }
            _ = fcntl(lockFD, F_SETFD, FD_CLOEXEC)
            runtimeDirectory = URL(fileURLWithPath: "/tmp/demomux-\(UUID().uuidString)", isDirectory: true)
            try privateDirectory(runtimeDirectory)
            do { catalog = try Catalog.load() }
            catch { catalogError = "无法读取项目列表。请检查 Projects.json 后重新打开应用。\(error.localizedDescription)" }
            if catalogError == nil, !FileManager.default.fileExists(atPath: Catalog.file.path) {
                let previous = defaults.integer(forKey: "projectPort")
                if validProjectPort(previous) {
                    _ = catalog.add(.port(previous), name: "本机 \(previous)")
                    try catalog.save()
                }
            }
        } catch {
            NSAlert(error: error).runModal()
            NSApplication.shared.terminate(nil)
            return
        }

        NSApplication.shared.appearance = NSAppearance(named: .darkAqua)
        buildMenu()
        keyboardMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard event.keyCode == 53 else { return event }
            let consumed = MainActor.assumeIsolated {
                guard self?.busy == true else { return false }
                self?.cancelCurrentOperation()
                return true
            }
            return consumed ? nil : event
        }
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 920, height: 660),
                          styleMask: [.titled, .closable, .miniaturizable, .resizable],
                          backing: .buffered, defer: false)
        window.title = "DemoMux"
        window.minSize = NSSize(width: 810, height: 575)
        window.isReleasedWhenClosed = false
        window.delegate = self
        showOnboarding()
        window.center()
        window.makeKeyAndOrderFront(nil)
        NSApplication.shared.activate(ignoringOtherApps: true)
        startSession()
        timer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.monitorServices() }
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationWillTerminate(_ notification: Notification) {
        timer?.invalidate()
        if let keyboardMonitor { NSEvent.removeMonitor(keyboardMonitor) }
        task?.cancel()
        stopServices()
        if let runtimeDirectory { try? FileManager.default.removeItem(at: runtimeDirectory) }
        if lockFD >= 0 { close(lockFD) }
    }

    func setPhase(_ name: String, _ text: String) {
        phase = name
        detail = text
        if let number = name.first?.wholeNumberValue, let stage = SetupStage(rawValue: number) { setup.begin(stage) }
        updateOnboarding()
    }

    func run(_ operation: @escaping @MainActor () async throws -> Void) {
        guard !busy else { return }
        retryOperation = operation
        verificationState = nil
        verificationStopped = false
        operationNeedsRetry = false
        accountCancelRequested = false
        task = Task {
            defer {
                self.task = nil
                if self.dashboard { self.refreshDashboard() } else { self.updateOnboarding() }
                self.refreshAccountUI()
            }
            do {
                try await operation()
            } catch is CancellationError {
                self.operationNeedsRetry = true
                let message = self.verificationStopped || self.verificationState != nil
                    ? "已停止公网校验，尚未完成验证。可点击「重新校验」继续。" : "操作已取消，可以重试。"
                if !self.dashboard { self.setup.pause(); self.detail = message; self.accountMessage = message }
                self.notice = message
            } catch {
                self.operationNeedsRetry = true
                self.setup.fail()
                if error is AccountIdentityError || error is AccountNotReadyError {
                    self.stopServices(keepingAccountClient: true)
                    self.setup.invalidate(from: .account)
                    self.setup.begin(.account); self.setup.fail()
                    self.phase = "3 / 5 · 连接你的账号"
                    if self.dashboard { self.showOnboarding() }
                }
                let reason = safeMessage(error.localizedDescription)
                if self.accountAction != nil || !self.dashboard || error is AccountIdentityError {
                    self.accountMessage = reason
                }
                if Task.isCancelled {
                    self.detail = "操作已取消。点击「重试」可继续。"
                } else if self.dashboard {
                    self.notice = reason
                } else {
                    self.detail = reason
                }
            }
        }
        if dashboard { refreshDashboard() } else { updateOnboarding() }
        refreshAccountUI()
    }

    func startSession() {
        setup = SetupProgress()
        setupContext = nil
        run {
            if let error = self.catalogError { throw AppError(error) }
            self.setPhase("1 / 5 · 准备 Caddy", "检查本机预览组件。需要时将下载并校验。")
            try await Dependency.caddy.install()
            try await self.startGateway()
            self.setup.complete(.caddy)
            self.setPhase("2 / 5 · 准备 Tailscale", "检查公网组件。需要时将下载并校验。")
            try await Dependency.tailscale.install()
            try await self.ensureAccountClient()
            self.setup.complete(.tailscale)
            try Task.checkCancellation()
            self.setPhase("3 / 5 · 登录 Tailscale", "正在连接。需要登录时请打开授权网页。")
            try await self.connectTailscale()
            try self.accountSnapshot.requireSynchronized(self.accountSnapshot.current?.id)
            self.setup.complete(.account)
            try await self.finishAccountSession()
        }
    }

    func connectTailscale() async throws {
        try await ensureAccountClient()
        try await readAccounts()
        if !accountSnapshot.online { try await authenticateAccount(adding: false) }
        try await synchronizeAccount()
    }

    func funnelHost(port: Int, listener: Int) async throws -> String {
        if port == 443, let primaryCandidate, funnels[443]?.running == true { return primaryCandidate }
        let child = try Child(Dependency.tailscale.executable("tailscale"), [
            "--socket=\(tsSocket.path)", "funnel", "--https=\(port)", "http://127.0.0.1:\(listener)",
        ])
        funnels[port] = child
        do {
            let deadline = Date().addingTimeInterval(300)
            while Date() < deadline {
                try await Task.sleep(for: .milliseconds(200))
                let output = child.output
                if output.contains("Available on the internet:"),
                   let host = firstURL(in: output, where: { $0.host?.hasSuffix(".ts.net") == true })?.host,
                   child.running {
                    authorizationURL = nil
                    updateOnboarding()
                    return host
                }
                if !dashboard {
                    showAuthorization(output, label: "启用 Funnel", instruction: "请在网页启用 Funnel。如果没有权限，请联系 tailnet 管理员。")
                } else if firstURL(in: output, where: { $0.host == "login.tailscale.com" }) != nil {
                    throw AppError("该 HTTPS 入口需要额外的 Funnel 权限。请检查 tailnet 策略后重新发布。")
                }
                guard child.running else {
                    throw AppError("Funnel 未启动。请确认已启用 HTTPS 和 Funnel 权限，再重试。\(safeMessage(output))")
                }
            }
            throw AppError("等待 Funnel 授权超时。请完成网页授权后重试。")
        } catch {
            child.stop()
            funnels[port] = nil
            authorizationURL = nil
            throw error
        }
    }

    func showAuthorization(_ output: String, label: String, instruction: String) {
        guard let url = firstURL(in: output, where: {
            $0.host == "login.tailscale.com" || $0.host == "console.tailscale.com"
        }), authorizationURL != url else { return }
        authorizationURL = url
        authorizationLabel = label
        detail = instruction
        updateOnboarding()
        refreshAccountUI()
    }

    func waitForPublic(_ url: URL, marker: String, onboarding: Bool = false, expectedRedirect: URL? = nil) async throws {
        let expectedProfile = verifiedProfileID ?? setupContext?.profile ?? accountSnapshot.current?.id
        guard let expectedProfile, !expectedProfile.isEmpty else { throw AccountNotReadyError() }
        verificationActive = true
        verificationState = nil
        if dashboard { refreshDashboard() } else { updateOnboarding() }
        refreshAccountUI(force: true)
        defer { verificationActive = false }
        try await VerificationRetry.run(check: {
            try Task.checkCancellation()
            guard self.caddy?.running == true, self.funnels[url.port ?? 443]?.running == true else {
                throw AppError("公网进程已退出。请重新启动演示。")
            }
            try await self.requireAccount(expectedProfile, host: url.host)
            if let probe = self.publicProbeForTesting { try await probe(url, marker, !onboarding, expectedRedirect) }
            else { try await validatePublic(url, marker: marker, allowRedirect: !onboarding, expectedRedirect: expectedRedirect) }
            try await self.requireAccount(expectedProfile, host: url.host)
        }, report: { state in
            self.verificationState = state
            self.verificationProgress = state.text
            if onboarding && !self.dashboard { self.detail = state.text }
            self.notice = state.text
            self.updateVerificationPresentation()
        })
    }

    func startGateway() async throws {
        if caddy?.running == true { return }
        let reserved = Set(catalog.projects.compactMap { $0.source.port })
        let listener = try await Task.detached {
            _ = try loopbackPort(previewPort)
            return try availableLoopbackPort(excluding: reserved)
        }.value
        localPort = listener
        try Task.checkCancellation()
        let config = try configuration(active: [:], shares: PublicationSlots(), roots: [:])
        let initial = runtimeDirectory.appendingPathComponent("gateway.json")
        try config.data().write(to: initial, options: .atomic)
        var environment = ProcessInfo.processInfo.environment
        environment["XDG_DATA_HOME"] = support.appendingPathComponent("CaddyData").path
        environment["XDG_CONFIG_HOME"] = support.appendingPathComponent("CaddyConfig").path
        let child = try Child(Dependency.caddy.executable("caddy"), ["run", "--config", initial.path],
                              environment: environment)
        caddy = child
        let socket = adminSocket
        do {
            try await Task.detached {
                for _ in 0..<30 {
                    guard child.running else { throw AppError("Caddy 启动失败。\(safeMessage(componentFailure(child.output)))") }
                    if FileManager.default.fileExists(atPath: socket.path),
                       (try? curl(["--unix-socket", socket.path, "--fail", "--output", "/dev/null",
                                   "http://localhost/config/"])) != nil,
                       (try? localHTTPStatus(port: previewPort, path: guidePath)) == 200 {
                        return
                    }
                    try await Task.sleep(for: .milliseconds(120))
                }
                throw AppError("本机预览启动超时。请检查端口 \(previewPort) 后重试。")
            }.value
        } catch {
            child.stop()
            caddy = nil
            throw error
        }
    }

    func configuration(active: [UUID: Content], shares: PublicationSlots, roots: [Int: Int]) throws -> GatewayConfiguration {
        guard let localPort else { throw AppError("请先启动演示。") }
        return GatewayConfiguration(admin: adminSocket, guide: guide, marker: guideMarker,
                                    localPort: localPort, roots: roots, projects: catalog.projects,
                                    active: active, publications: shares, publicHost: publicHost ?? primaryCandidate)
    }

    func reload(active: [UUID: Content], shares: PublicationSlots, roots: [Int: Int]) async throws {
        guard caddy?.running == true else { throw AppError("本机预览已停止，请重新启动演示。") }
        let config = try configuration(active: active, shares: shares, roots: roots)
        let candidate = runtimeDirectory.appendingPathComponent("candidate.json")
        try await Task.detached {
            try config.data().write(to: candidate, options: .atomic)
            _ = try command(Dependency.caddy.executable("caddy"),
                            ["reload", "--config", candidate.path, "--address", "unix/\(config.admin.path)"])
        }.value
    }

    func stopServices(keepingAccountClient: Bool = false) {
        accountProbe?.cancel()
        portProbe?.cancel()
        funnels.values.forEach { $0.stop() }
        funnels.removeAll()
        login?.stop()
        login = nil
        if !keepingAccountClient {
            tailscaled?.stop()
            tailscaled = nil
            accountSnapshot = accountSnapshot.disconnected
        }
        verifiedProfileID = nil
        publicVerificationIssue = nil
        entryVerified = false
        setupContext = nil
        checkingPublic = false
        verificationProgress = nil
        caddy?.stop()
        caddy = nil
        active.values.forEach { $0.discard() }
        active.removeAll()
        slots = PublicationSlots()
        pendingID = nil
        roots.removeAll()
        publicHost = nil
        primaryCandidate = nil
        authorizationURL = nil
        localPort = nil
        guideMarker = UUID().uuidString
    }

    func monitorServices() {
        probeAccountIdentity()
        guard dashboard, !busy else { return }
        if publicHost != nil && !servicesAvailable {
            stopServices()
            notice = "公网服务已断开。请重新启动演示。"
            refreshDashboard()
            return
        }
        let invalid = catalog.projects.filter { active[$0.id] != nil && !$0.source.sourceExists }
        let lost = slots.shares.compactMap { (id, share) -> UUID? in
            if case .root(let port) = share.publication, port != 443, funnels[port]?.running != true { return id }
            return nil
        }
        guard !invalid.isEmpty || !lost.isEmpty else {
            checkActivePorts()
            return
        }
        run {
            for project in invalid {
                try await self.withdraw(project.id, stop: true)
                self.diagnostics[project.id] = "源文件失效，请重新定位。"
            }
            for id in lost where self.slots.shares[id] != nil {
                try await self.withdraw(id, stop: false)
                self.diagnostics[id] = "独立入口已断开，请重新发布。"
            }
        }
    }

    private func checkActivePorts() {
        guard ready, portProbe == nil else { return }
        let ports = active.compactMapValues { content -> Int? in
            if case let .proxy(port) = content { return port }
            return nil
        }
        guard !ports.isEmpty else { return }
        let session = guideMarker
        portProbe = Task {
            defer { self.portProbe = nil }
            let refused = await Task.detached {
                ports.filter { loopbackConnectionRefused($0.value) }
            }.value
            guard !Task.isCancelled, !refused.isEmpty, self.ready, !self.busy,
                  self.guideMarker == session else { return }
            self.run {
                var names: [String] = []
                for project in self.catalog.projects {
                    guard let port = refused[project.id],
                          case .proxy(port)? = self.active[project.id] else { continue }
                    // Check again in case the project restarted while the result was in flight.
                    let stillClosed = await Task.detached { loopbackConnectionRefused(port) }.value
                    guard stillClosed else { continue }
                    try await self.withdraw(project.id, stop: true)
                    self.diagnostics[project.id] = "本机端口 \(port) 已关闭。请先启动原项目，再重新预览或发布。"
                    names.append(project.name)
                }
                if !names.isEmpty {
                    self.notice = "已停止转发「\(names.joined(separator: "、"))」：本机服务已关闭。"
                }
            }
        }
    }

    func saveCatalog(_ candidate: Catalog) throws {
        try candidate.save()
        catalog = candidate
    }

    func remember(_ project: Project) {
        guard project.source.port != nil else { return }
        sessionPorts.removeAll { $0 == project.id }
        sessionPorts.insert(project.id, at: 0)
    }

    func prepare(_ project: Project, force: Bool = false) async throws {
        guard force || active[project.id] == nil else { return }
        if let port = project.source.port, port == localPort || roots.values.contains(port) {
            throw AppError("该端口目前用于 DemoMux 的转发，请重新启动本机项目并使用其他端口。")
        }
        let directory = runtimeDirectory!
        let candidate = try await Task.detached { try project.source.prepare(in: directory) }.value
        var updated = active
        let old = updated.updateValue(candidate, forKey: project.id)
        do { try await reload(active: updated, shares: slots, roots: roots) }
        catch {
            candidate.discard()
            throw error
        }
        active = updated
        old?.discard()
        diagnostics[project.id] = nil
        remember(project)
    }

    func publishProject(_ project: Project, publication: Publication, slug: String) async throws {
        guard let host = publicHost, ready else { throw AppError("请先启动并验证公网演示。") }
        guard let profile = verifiedProfileID else { throw AccountIdentityError() }
        guard let index = catalog.projects.firstIndex(where: { $0.id == project.id }) else { return }
        var prospective = catalog
        prospective.projects[index].slug = slug
        let selected = prospective.projects[index]
        var candidate = slots
        let share = try candidate.reserve(project.id, publication: publication)
        pendingID = project.id
        diagnostics[project.id] = nil
        verificationProgress = nil
        notice = "正在发布「\(selected.name)」并验证公网链接…"
        refreshDashboard()
        defer { pendingID = nil; verificationProgress = nil }

        do {
            try await requireAccount(profile)
            try await prepare(project)
            if case .path = publication, let port = selected.source.port {
                let response = try await Task.detached { try localHTTPStatus(port: port, path: selected.path) }.value
                guard (200...399).contains(response) else {
                    throw AppError("项目在 \(selected.path) 返回 HTTP \(response)。请检查基础路径，或改用独立入口。")
                }
            }
            var listeners = roots
            if case .root(let port) = publication {
                prospective.rememberRoot(port, project: project.id, profile: profile, host: host)
                if port != 443 {
                    let reserved = Set(catalog.projects.compactMap { $0.source.port })
                    listeners[port] = try await Task.detached { try availableLoopbackPort(excluding: reserved) }.value
                }
            }
            try saveCatalog(prospective)
            do {
                try await reload(active: active, shares: candidate, roots: listeners)
                if case .root(let port) = publication, let listener = listeners[port], port != 443 {
                    guard try await funnelHost(port: port, listener: listener) == host else {
                        throw AppError("独立入口返回了不同的 Funnel 域名。")
                    }
                }
                let url = share.url(host: host, project: selected)
                try await waitForPublic(url, marker: share.marker)
                try await requireAccount(profile)
                if case .path = publication {
                    prospective.projects[index].lockedSlug = true
                    try saveCatalog(prospective)
                }
            } catch {
                if case .root(let port) = publication, port != 443 {
                    funnels.removeValue(forKey: port)?.stop()
                }
                do { try await reload(active: active, shares: slots, roots: roots) }
                catch {
                    stopServices()
                    throw AppError("无法恢复转发配置，已停止本次演示。请重新启动。")
                }
                throw error
            }
            candidate.shares[project.id]?.verified = true
            if let home = candidate.homeProjectID {
                prospective.rememberRoot(443, project: home, profile: profile, host: host)
                try saveCatalog(prospective)
            }
            slots = candidate
            roots = listeners
            diagnostics[project.id] = nil
            remember(selected)
            notice = "「\(selected.name)」已发布。"
        } catch {
            diagnostics[project.id] = verificationErrorMessage(error)
            throw error
        }
    }

    func withdraw(_ id: UUID, stop: Bool) async throws {
        var updated = active
        let old = stop ? updated.removeValue(forKey: id) : nil
        var candidate = slots
        let removed = candidate.remove(id)
        var listeners = roots
        if case .root(let port) = removed?.publication, port != 443 {
            funnels.removeValue(forKey: port)?.stop()
            listeners.removeValue(forKey: port)
        }
        do { try await reload(active: updated, shares: candidate, roots: listeners) }
        catch {
            stopServices()
            throw AppError("无法撤下转发配置，已停止本次演示。请重新启动。")
        }
        active = updated
        slots = candidate
        roots = listeners
        old?.discard()
        if let home = candidate.homeProjectID, let host = publicHost, let profile = verifiedProfileID {
            var updatedCatalog = catalog
            updatedCatalog.rememberRoot(443, project: home, profile: profile, host: host)
            try saveCatalog(updatedCatalog)
        }
    }
}

func verificationErrorMessage(_ error: Error) -> String {
    if error is CancellationError { return "已停止校验，尚未完成验证。可重新校验或再次发布。" }
    return safeMessage(error.localizedDescription)
}

func safeMessage(_ text: String) -> String {
    let redacted = text.replacingOccurrences(of: #"https://(?:login|console)\.tailscale\.com/\S+"#,
                                              with: "[Tailscale 授权链接]", options: .regularExpression)
    return String(redacted.prefix(450)).trimmingCharacters(in: .whitespacesAndNewlines)
}

MainActor.assumeIsolated {
    let app = NSApplication.shared
    let controller = DemoMuxWindow()
    app.setActivationPolicy(.regular)
    app.delegate = controller
    app.run()
}
