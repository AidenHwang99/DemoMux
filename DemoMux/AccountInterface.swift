import AppKit

struct AccountPanelState: Equatable {
    let snapshot: AccountSnapshot
    let ready: Bool
    let busy: Bool
    let clientRunning: Bool
    let message: String
    let detail: String
    let authorization: URL?
    let pending: AccountAction?
    let cancelling: Bool
    let publishedNames: [String]
}

extension DemoMuxWindow {
    var accountPanelMessage: String {
        if verificationActive, let verificationState { return verificationState.text }
        if busy && dashboard && accountAction == nil {
            return notice + (verificationProgress.map { "\n" + $0 } ?? "")
        }
        if let publicVerificationIssue, servicesAvailable { return "账号仍已连接；公网入口需要重试。\n" + publicVerificationIssue }
        let message = accountMessage.isEmpty ? "选择已保存账号，或添加一个新账号。" : accountMessage
        return busy && !detail.isEmpty && (accountAction != nil || !dashboard) ? message + "\n" + detail : message
    }
    func accountTitle(_ profile: AccountProfile) -> String {
        if profile.selected, !accountSnapshot.displayName.isEmpty { return accountSnapshot.displayName }
        return profile.title.isEmpty ? "Tailscale 账号" : profile.title
    }

    func accountMetadata(_ profile: AccountProfile) -> String {
        if !profile.account.isEmpty, profile.tailnet.caseInsensitiveCompare(profile.account) == .orderedSame {
            return "账号 / 网络：" + profile.account
        }
        let network = profile.tailnet.isEmpty ? "网络信息待连接后读取" : "网络：\(profile.tailnet)"
        return accountTitle(profile).caseInsensitiveCompare(profile.account) == .orderedSame
            ? network : "\(profile.account) · \(network)"
    }

    var accountSummaryText: String {
        guard let profile = accountSnapshot.current else {
            return "尚未选择账号\n添加或选择 DemoMux 中保存的 Tailscale 账号"
        }
        let name = accountTitle(profile)
        let connection: String
        if !accountClientRunning { connection = "上次使用 · 未连接" }
        else if accountSnapshot.online { connection = accountSnapshot.principalConfirmed ? "当前账号 · \(publicStatusText)" : "账号已连接 · 身份待同步" }
        else { connection = "需要登录或设备审批" }
        return "\(name)\n\(accountMetadata(profile))\n\(connection)"
    }

    func accountCard(_ content: NSView, padding: CGFloat = 14) -> NSBox {
        let card = NSBox()
        card.boxType = .custom
        card.fillColor = GuideTheme.surface
        card.borderColor = GuideTheme.border
        card.borderWidth = 1
        card.cornerRadius = 14
        card.contentView!.addSubview(content)
        content.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            content.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: padding),
            content.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -padding),
            content.topAnchor.constraint(equalTo: card.topAnchor, constant: padding),
            content.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -padding),
        ])
        return card
    }

    func accountIdentityCard() -> NSView {
        let icon = NSImageView(image: SharedIcon.image("user-round"))
        icon.setAccessibilityLabel("Tailscale 账号")
        icon.contentTintColor = GuideTheme.link
        icon.symbolConfiguration = .init(pointSize: 30, weight: .regular)
        icon.widthAnchor.constraint(equalToConstant: 38).isActive = true
        icon.heightAnchor.constraint(equalToConstant: 38).isActive = true
        icon.imageScaling = .scaleProportionallyUpOrDown
        icon.wantsLayer = true
        icon.layer?.backgroundColor = GuideTheme.raised.cgColor
        icon.layer?.cornerRadius = 19
        icon.heightAnchor.constraint(equalToConstant: 38).isActive = true
        let caption = label("TAILSCALE  /  演示身份", size: 10, weight: .semibold)
        caption.textColor = GuideTheme.accent
        accountSummary = NSTextField(wrappingLabelWithString: accountSummaryText)
        accountSummary?.font = .systemFont(ofSize: 12)
        accountSummary?.textColor = GuideTheme.muted
        accountSummary?.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        let text = column([caption, accountSummary!], spacing: 5)
        let manage = button("管理账号", #selector(openAccountManager))
        return accountCard(row([icon, text, NSView(), manage], spacing: 12))
    }

    @objc func openAccountManager() {
        if accountPanel == nil {
            let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 610, height: 650),
                                styleMask: [.titled, .closable, .resizable],
                                backing: .buffered, defer: false)
            panel.title = "DemoMux · 账号与公网"
            panel.appearance = NSAppearance(named: .darkAqua)
            panel.backgroundColor = GuideTheme.background
            panel.minSize = NSSize(width: 565, height: 540)
            panel.isReleasedWhenClosed = false
            panel.delegate = self
            panel.center()
            accountPanel = panel
        }
        refreshAccountUI(force: true)
        accountPanel?.makeKeyAndOrderFront(nil)
    }

    func refreshAccountUI(force: Bool = false) {
        accountSummary?.stringValue = accountSummaryText
        onboardingAccountSummary?.stringValue = accountSummaryText
        guard let panel = accountPanel, force || panel.isVisible else { return }
        let next = AccountPanelState(snapshot: accountSnapshot, ready: ready, busy: busy,
                                     clientRunning: accountClientRunning, message: accountPanelMessage, detail: publicStatusText,
                                     authorization: authorizationURL, pending: pendingAccountAction,
                                     cancelling: accountCancelRequested,
                                     publishedNames: catalog.projects.filter { slots.shares[$0.id] != nil }.map(\.name))
        guard force || accountRenderState != next else { return }
        accountRenderState = next
        let root = GuideBackground()
        let eyebrow = label("DEMOMUX  /  TAILSCALE", size: 11, weight: .semibold)
        eyebrow.textColor = GuideTheme.accent
        let title = label("账号与公网", size: 28, weight: .bold)
        let summary = NSTextField(wrappingLabelWithString: accountSummaryText)
        summary.font = .systemFont(ofSize: 13)
        summary.textColor = GuideTheme.text
        let status = label(servicesAvailable ? publicStatusText : busy ? "正在准备" : "分享已停止", size: 11, weight: .semibold)
        status.textColor = GuideTheme.link
        accountBadgeLabel = status
        let badge = glass(status, radius: 12, x: 10, y: 6)
        let explanation = NSTextField(wrappingLabelWithString:
            "账号之间共享项目列表。切换后只开启介绍页，项目由你手动发布。这里只管理 DemoMux 的私有实例。")
        explanation.font = .systemFont(ofSize: 12)
        explanation.textColor = GuideTheme.muted
        let content = column([row([column([eyebrow, title], spacing: 4), NSView(), badge]),
                              accountCard(summary), explanation], spacing: 14)
        let information = NSTextField(wrappingLabelWithString: accountPanelMessage)
        accountInformationLabel = information
        information.font = .systemFont(ofSize: 12)
        information.textColor = GuideTheme.link
        content.addArrangedSubview(information)

        if let action = pendingAccountAction {
            let heading = label(action == .logout ? "退出当前账号" : "切换演示身份", size: 16, weight: .semibold)
            let names = catalog.projects.filter { slots.shares[$0.id] != nil }.map(\.name)
            var text = action == .logout
                ? "这会撤下当前分享并使此设备的当前账号授权失效。下次使用需要重新登录。"
                : "继续后将撤下旧账号的分享，项目列表保留。新账号只开启介绍页。"
            if action == .logout, let target = accountSnapshot.profiles.first(where: { $0.id == pendingAccountProfileID }) {
                text = "将退出：\(target.account) · \(target.tailnet)。\n" + text
            }
            if case let .switchTo(id) = action,
               let target = accountSnapshot.profiles.first(where: { $0.id == id }) {
                text = "切换到 \(target.account) · \(target.tailnet)。\n" + text
            }
            if !names.isEmpty { text += "\n正在分享：\(names.joined(separator: "、"))。" }
            let warning = NSTextField(wrappingLabelWithString: text)
            warning.font = .systemFont(ofSize: 12)
            warning.textColor = GuideTheme.muted
            let accept = button(action == .logout ? "停止分享并退出登录" : "继续", #selector(confirmAccountAction), primary: true)
            let back = button("返回", #selector(cancelAccountConfirmation))
            let body = column([heading, warning, row([back, NSView(), accept])], spacing: 12)
            warning.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true
            body.views.last!.widthAnchor.constraint(equalTo: body.widthAnchor).isActive = true
            content.addArrangedSubview(accountCard(body))
        } else {
            if accountSnapshot.profiles.isEmpty {
                let empty = label("还没有保存的账号", size: 14, weight: .semibold)
                content.addArrangedSubview(accountCard(empty))
            }
            for profile in accountSnapshot.profiles {
                let heading = label(accountTitle(profile),
                                    size: 14, weight: .semibold)
                heading.lineBreakMode = .byTruncatingMiddle
                heading.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
                let metadata = label(accountMetadata(profile), size: 11)
                metadata.lineBreakMode = .byTruncatingMiddle
                metadata.textColor = GuideTheme.muted
                metadata.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
                let note = profile.selected ? (!accountClientRunning ? "上次使用 · 未连接" :
                    accountSnapshot.online ? "当前账号 · 已连接" : "需要重新登录或设备审批") :
                    "已保存 · 切换后检查授权"
                let state = label(note, size: 11)
                state.textColor = profile.selected ? GuideTheme.link : GuideTheme.muted
                let control = button(profile.selected && ready ? "检查公网" :
                    profile.selected && !accountSnapshot.online && accountClientRunning ? "重新登录" :
                    profile.selected ? accountClientRunning && accountSnapshot.online ? "同步身份并继续" : "恢复连接并继续" : "切换并启动", #selector(selectAccount), primary: profile.selected)
                control.identifier = NSUserInterfaceItemIdentifier(profile.id)
                control.isEnabled = !busy
                content.addArrangedSubview(accountCard(row([column([heading, metadata, state], spacing: 4),
                                                           NSView(), control], spacing: 10)))
            }
        }

        if authorizationURL != nil {
            let authorize = button(authorizationLabel, #selector(openAuthorization), primary: true)
            content.addArrangedSubview(authorize)
        }
        let add = button("添加账号", #selector(addAccount), primary: true)
        let refresh = button("刷新账号", #selector(refreshAccounts))
        let logout = button("退出当前账号", #selector(logoutAccount))
        let pause = button("暂停演示", #selector(pauseAccounts))
        let cancel = button(verificationActive ? "停止重试" : "取消当前步骤", #selector(cancelAccountWork))
        for control in [add, refresh, logout, pause] { control.isEnabled = !busy && pendingAccountAction == nil }
        logout.isEnabled = logout.isEnabled && accountSnapshot.current != nil
        pause.isEnabled = pause.isEnabled && accountClientRunning
        cancel.isEnabled = busy && accountAction != .logout && !accountCancelRequested
        let footer = column([row([add, refresh, NSView(), logout]), row([pause, NSView(), cancel])], spacing: 8)
        for item in footer.views { item.widthAnchor.constraint(equalTo: footer.widthAnchor).isActive = true }
        let controls = glass(footer, radius: 16, x: 12, y: 10)
        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.scrollerStyle = .overlay
        scroll.scrollerKnobStyle = .light
        scroll.drawsBackground = false
        let document = AccountDocument()
        document.translatesAutoresizingMaskIntoConstraints = false
        scroll.documentView = document
        document.addSubview(content)
        content.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(scroll)
        root.addSubview(controls)
        scroll.translatesAutoresizingMaskIntoConstraints = false
        controls.translatesAutoresizingMaskIntoConstraints = false
        panel.contentView = root
        NSLayoutConstraint.activate([
            scroll.topAnchor.constraint(equalTo: root.topAnchor), scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor), scroll.bottomAnchor.constraint(equalTo: root.bottomAnchor),
            controls.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 20),
            controls.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -20),
            controls.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -16),
            document.topAnchor.constraint(equalTo: scroll.contentView.topAnchor),
            document.leadingAnchor.constraint(equalTo: scroll.contentView.leadingAnchor),
            document.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
            content.topAnchor.constraint(equalTo: document.topAnchor, constant: 24),
            content.leadingAnchor.constraint(equalTo: document.leadingAnchor, constant: 22),
            content.trailingAnchor.constraint(equalTo: document.trailingAnchor, constant: -22),
            content.bottomAnchor.constraint(equalTo: document.bottomAnchor, constant: -118),
        ])
        for view in content.views { view.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true }
        content.setHuggingPriority(.required, for: .vertical)
        accountPanelBody = root
    }

    @objc func selectAccount(_ sender: NSButton) {
        guard let id = sender.identifier?.rawValue else { return }
        if ready && accountSnapshot.current?.id == id { checkGuide(); return }
        requestAccountAction(.switchTo(id))
    }
    @objc func addAccount() { requestAccountAction(.add) }
    @objc func logoutAccount() { requestAccountAction(.logout) }
    @objc func confirmAccountAction() {
        guard let action = pendingAccountAction else { return }
        if action == .logout, pendingAccountProfileID != accountSnapshot.current?.id {
            pendingAccountAction = nil
            pendingAccountProfileID = nil
            accountMessage = "当前身份已改变。请确认账号后重新选择退出，尚未退出任何账号。"
            refreshAccountUI()
            return
        }
        performAccountAction(action)
    }
    @objc func cancelAccountConfirmation() {
        pendingAccountAction = nil
        pendingAccountProfileID = nil
        refreshAccountUI()
    }
    @objc func pauseAccounts() { stopAll(); refreshAccountUI() }
    @objc func refreshAccounts() {
        guard !busy else { return }
        run {
            let wasRunning = self.accountClientRunning
            defer { if !wasRunning { self.stopServices() } }
            try await Dependency.tailscale.install()
            try await self.ensureAccountClient()
            try await self.readAccounts()
            self.accountMessage = "账号列表已刷新。选择账号可启动并验证 Funnel。"
        }
    }
    @objc func cancelAccountWork() {
        if verificationActive { stopCurrentVerification(); return }
        guard accountAction != .logout else { return }
        accountCancelRequested = true
        task?.cancel()
        login?.stop()
        stopServices(keepingAccountClient: accountAction != nil)
        detail = "已取消当前步骤。可在账号面板选择账号后重试。"
        updateOnboarding()
        if dashboard { refreshDashboard() }
    }
}

private final class AccountDocument: NSView {
    override var isFlipped: Bool { true }
}

extension DemoMuxWindow: NSWindowDelegate {
    func windowWillClose(_ notification: Notification) {
        if let closed = notification.object as? NSWindow, closed === window {
            NSApplication.shared.terminate(nil)
            return
        }
        if let closed = notification.object as? NSWindow, closed === accountPanel,
           busy, accountAction != nil, accountAction != .logout { cancelAccountWork() }
    }
}
