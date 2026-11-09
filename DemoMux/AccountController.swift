import AppKit

extension DemoMuxWindow {
    var accountClient: AccountClient { accountClientForTesting ?? AccountClient(socket: tsSocket) }
    var accountClientRunning: Bool { tailscaled?.running == true }
    var rootAccountID: String { verifiedProfileID ?? accountSnapshot.current?.id ?? "" }

    func ensureAccountClient() async throws {
        if !accountClientRunning {
            let state = support.appendingPathComponent("Tailscale", isDirectory: true)
            try privateDirectory(state)
            tailscaled = try Child(Dependency.tailscale.executable("tailscaled"), [
                "--tun=userspace-networking", "--statedir=\(state.path)",
                "--socket=\(tsSocket.path)", "--port=0",
            ])
        }
        let socket = tsSocket
        for _ in 0..<40 {
            try Task.checkCancellation()
            if FileManager.default.fileExists(atPath: socket.path),
               let _ = try? await accountClient.snapshot() { return }
            try await Task.sleep(for: .milliseconds(150))
        }
        throw AppError("Tailscale 本机组件未就绪，请重试。")
    }

    func readAccounts() async throws {
        let snapshot = try await accountClient.snapshot()
        try Task.checkCancellation()
        accountSnapshot = snapshot
        try AccountCache.save(snapshot)
        refreshAccountUI()
    }

    func requireAccount(_ id: String, host: String? = nil) async throws {
        let snapshot = try await accountClient.snapshot()
        try Task.checkCancellation()
        try snapshot.requireSynchronized(id, host: host)
        accountSnapshot = snapshot
    }

    func synchronizeAccount() async throws {
        accountSnapshot = try await accountClient.synchronizedSnapshot(expected: accountSnapshot.current?.id)
        try AccountCache.save(accountSnapshot)
        refreshAccountUI()
    }

    func authenticateAccount(adding: Bool) async throws {
        let saved = defaults.string(forKey: "nodeSuffix")
        let identity = saved ?? String(UUID().uuidString.prefix(8)).lowercased()
        defaults.set(identity, forKey: "nodeSuffix")
        let authentication = try Child(Dependency.tailscale.executable("tailscale"), [
            "--socket=\(tsSocket.path)", adding ? "login" : "up",
            "--accept-dns=false", "--accept-routes=false",
            "--hostname=demomux-\(identity)", "--timeout=5m",
        ])
        login = authentication
        accountMessage = "正在等待浏览器授权。请在网页确认需要使用的账号。"
        refreshAccountUI()
        defer {
            authentication.stop()
            if login === authentication { login = nil }
            authorizationURL = nil
            refreshAccountUI()
        }
        while authentication.running {
            try await Task.sleep(for: .milliseconds(180))
            showAuthorization(authentication.output, label: "打开登录页面",
                              instruction: "请在浏览器用需要的账号登录。完成后会自动继续。")
        }
        try Task.checkCancellation()
        guard authentication.process.terminationStatus == 0 else {
            throw AppError("登录未完成。请检查网络、账号和设备审批状态，再重试。")
        }
        try await readAccounts()
        try await synchronizeAccount()
    }

    func finishAccountSession() async throws {
        guard let profile = accountSnapshot.current?.id else {
            throw AppError("没有可使用的账号，请添加账号登录。")
        }
        try await requireAccount(profile)
        setup.complete(.account)
        let epoch = guideMarker
        setPhase("4 / 5 · 启用 Funnel", "正在为当前账号准备公网介绍页。需要时请打开授权页面。")
        accountMessage = "正在准备 Funnel。项目列表保留，项目不会自动发布。"
        refreshAccountUI()
        try await startGateway()
        let host = try await funnelHost(port: 443, listener: previewPort)
        primaryCandidate = host
        try await requireAccount(profile, host: host)
        setup.complete(.funnel)
        let context = SetupVerificationContext(profile: profile, host: host, marker: epoch)
        setupContext = context
        try await completeSetupVerification(context)
    }

    func completeSetupVerification(_ context: SetupVerificationContext) async throws {
        try await requireAccount(context.profile, host: context.host)
        guard setup.mayVerify, caddy?.running == true, guideMarker == context.marker else { throw AccountNotReadyError() }
        if funnels[443]?.running != true {
            guard try await funnelHost(port: 443, listener: previewPort) == context.host else { throw AccountIdentityError() }
        }
        let url = URL(string: "https://\(context.host)\(guidePath)")!
        entryVerified = false
        setPhase("5 / 5 · 验证公网介绍页", "正在通过公共 DNS 和 HTTPS 验证当前账号的介绍页。")
        accountMessage = "账号已连接，正在验证公网介绍页。"
        refreshAccountUI()
        try await waitForPublic(url, marker: context.marker, onboarding: true)
        try await requireAccount(context.profile, host: context.host)
        guard guideMarker == context.marker else { throw CancellationError() }
        setup.complete(.publicEntry)
        verifiedProfileID = context.profile
        publicHost = context.host
        entryVerified = true
        setupContext = nil
        publicVerificationIssue = nil
        detail = ""
        accountMessage = "公网已就绪。请选择要发布的项目，新账号下的链接请以界面显示为准。"
        notice = accountMessage
        showDashboard()
        refreshAccountUI()
        if defaults.object(forKey: "openGuideWhenReady") as? Bool ?? true { NSWorkspace.shared.open(url) }
    }

    func requestAccountAction(_ action: AccountAction) {
        guard !busy else { return }
        if action == .logout || slots.shares.count > 0 {
            pendingAccountAction = action
            pendingAccountProfileID = accountSnapshot.current?.id
            refreshAccountUI()
        } else {
            performAccountAction(action)
        }
    }

    func performAccountAction(_ action: AccountAction) {
        guard !busy else { return }
        pendingAccountAction = nil
        pendingAccountProfileID = nil
        accountAction = action
        accountCancelRequested = false
        let previous = accountSnapshot.current?.id
        run {
            defer {
                self.accountAction = nil
                self.accountCancelRequested = false
                self.refreshAccountUI()
            }
            self.stopServices(keepingAccountClient: true)
            self.showOnboarding()
            self.setPhase("账号与公网", action == .logout ? "正在退出当前账号…" : "正在准备所选账号…")
            do {
                self.setPhase("1 / 5 · 准备 Caddy", "检查并复用已经校验的预览引擎。")
                try await Dependency.caddy.install()
                try await self.startGateway()
                self.setup.complete(.caddy)
                self.setPhase("2 / 5 · 准备 Tailscale", "检查并复用 DemoMux 的私有连接组件。")
                try await Dependency.tailscale.install()
                try await self.ensureAccountClient()
                self.setup.complete(.tailscale)
                self.setPhase("3 / 5 · 连接你的账号", "正在同步所选账号与设备身份。")
                try await self.ensureAccountClient()
                try await self.readAccounts()
                switch action {
                case .logout:
                    // Logout revokes the current node key. Never silently target a
                    // different profile if the state changed since confirmation.
                    guard self.accountSnapshot.current?.id == previous, previous != nil else {
                        throw AccountIdentityError()
                    }
                    try await self.accountClient.logout()
                    try await self.readAccounts()
                    self.stopServices()
                    self.accountMessage = "已退出当前账号。再次使用需要登录；其他已保存账号不受影响。"
                    self.notice = self.accountMessage
                    self.showDashboard()
                    return
                case .switchTo(let id):
                    self.accountSnapshot = try await self.accountClient.select(id)
                    try AccountCache.save(self.accountSnapshot)
                    if !self.accountSnapshot.online { try await self.authenticateAccount(adding: false) }
                case .add:
                    try await self.authenticateAccount(adding: true)
                case .reconnect:
                    if !self.accountSnapshot.online { try await self.authenticateAccount(adding: false) }
                }
                try await self.finishAccountSession()
            } catch is CancellationError {
                if self.verificationStopped, self.setupContext != nil {
                    self.setup.pause()
                    self.detail = "已停止公网校验，尚未完成验证。点击「重新校验」可继续，无需重新下载和登录。"
                    self.accountMessage = self.detail
                    self.updateOnboarding()
                    return
                }
                // Restore only the selection; cancelled operations never restore
                // public routes or automatically publish the previous projects.
                if action != .logout, let previous {
                    let client = self.accountClient
                    if let restored = try? await Task.detached(operation: { try await client.select(previous) }).value {
                        self.accountSnapshot = restored
                        try? AccountCache.save(restored)
                    }
                }
                self.stopServices()
                self.accountMessage = "已取消。分享保持停止；可以选择账号后重新启动 Funnel。"
                self.notice = self.accountMessage
                self.showDashboard()
            } catch {
                if self.setupContext != nil, !(error is AccountIdentityError), !(error is AccountNotReadyError) {
                    self.setup.fail()
                    throw error
                }
                self.stopServices()
                self.accountMessage = safeMessage(error.localizedDescription)
                self.notice = self.accountMessage
                self.showDashboard()
                throw error
            }
        }
    }

    func probeAccountIdentity() {
        guard accountClientRunning, !busy, accountProbe == nil else { return }
        let client = accountClient
        let epoch = guideMarker
        let expected = verifiedProfileID
        accountProbe = Task {
            defer { self.accountProbe = nil }
            do {
                let current = try await client.snapshot()
                guard !Task.isCancelled, !self.busy, self.guideMarker == epoch else { return }
                if let expected {
                    do { try current.requireOnline(expected) }
                    catch {
                        self.accountSnapshot = current
                        self.stopServices()
                        self.accountMessage = "登录状态已改变，已撤下本次分享。请确认账号后重新启动 Funnel。"
                        self.notice = self.accountMessage
                        if self.dashboard { self.refreshDashboard() }
                    }
                }
                if self.accountClientRunning, self.accountSnapshot != current {
                    self.accountSnapshot = current
                    try? AccountCache.save(current)
                }
                self.refreshAccountUI()
            } catch {
                // A single failed status read is not evidence of logout.
            }
        }
    }
}
