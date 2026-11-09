import AppKit

extension DemoMuxWindow {
    func showOnboarding() {
        dashboard = false
        window.appearance = NSAppearance(named: .darkAqua)
        window.backgroundColor = GuideTheme.background
        setupStateLabels.removeAll(); setupNumberLabels.removeAll()
        let eyebrow = label(isolatedPreviewNotice ?? "DEMOMUX  /  连接准备", size: 11, weight: .semibold)
        eyebrow.textColor = GuideTheme.accent
        let title = label("让演示，随时可分享。", size: 30, weight: .bold)
        let subtitle = label("完成五步检查后，开始分享 HTML 或本机项目。账号授权在浏览器完成。", size: 12)
        subtitle.textColor = GuideTheme.muted
        let steps = column([], spacing: 18)
        for stage in SetupStage.allCases {
            let number = label(String(format: "%02d", stage.rawValue), size: 12, weight: .semibold)
            number.alignment = .center; number.textColor = GuideTheme.link
            number.widthAnchor.constraint(equalToConstant: 28).isActive = true
            let heading = label(stage.title, size: 13, weight: .semibold)
            let status = label(stage.subtitle, size: 11)
            status.maximumNumberOfLines = 2
            status.textColor = GuideTheme.muted
            let text = column([heading, status], spacing: 3)
            let stepRow = row([number, text], spacing: 10)
            stepRow.alignment = .centerY
            steps.addArrangedSubview(stepRow)
            setupStateLabels[stage] = status; setupNumberLabels[stage] = number
        }
        let stepCard = accountCard(steps, padding: 18)
        stepCard.widthAnchor.constraint(equalToConstant: 300).isActive = true
        phaseText.font = .systemFont(ofSize: 18, weight: .semibold)
        phaseText.textColor = GuideTheme.text
        phaseText.maximumNumberOfLines = 2
        detailText.font = .systemFont(ofSize: 13)
        detailText.textColor = GuideTheme.muted
        detailText.maximumNumberOfLines = 8
        let motion = VerificationActivityView(frame: .zero)
        setupActivity = motion
        authorization.target = self; authorization.action = #selector(openAuthorization)
        retry.target = self; retry.action = #selector(retryOnboarding)
        retry.bezelStyle = .rounded; retry.bezelColor = GuideTheme.action
        authorization.bezelStyle = .rounded
        let cancel = button("取消准备", #selector(cancelOnboarding))
        setupCancelButton = cancel
        let controls = row([retry, authorization, cancel], spacing: 8)
        let progress = column([phaseText, detailText, motion, controls], spacing: 14)
        for view in [phaseText, detailText, motion] { view.widthAnchor.constraint(equalTo: progress.widthAnchor).isActive = true }
        let progressCard = accountCard(progress, padding: 18)
        onboardingAccountSummary = NSTextField(wrappingLabelWithString: accountSummaryText)
        onboardingAccountSummary?.font = .systemFont(ofSize: 12)
        onboardingAccountSummary?.textColor = GuideTheme.muted
        let manage = button("管理账号与连接", #selector(openAccountManager))
        manage.image = SharedIcon.image("user-round"); manage.imagePosition = .imageLeading
        let identity = column([label("你的演示身份", size: 12, weight: .semibold), onboardingAccountSummary!, manage], spacing: 10)
        onboardingAccountSummary!.widthAnchor.constraint(equalTo: identity.widthAnchor).isActive = true
        let right = column([progressCard, accountCard(identity, padding: 18)], spacing: 14)
        for view in right.views { view.widthAnchor.constraint(equalTo: right.widthAnchor).isActive = true }
        let columns = row([stepCard, right], spacing: 18)
        columns.alignment = .top
        let guide = button("认识 DemoMux · 使用说明", #selector(openGuidePage))
        guide.image = NSImage(systemSymbolName: "book.closed", accessibilityDescription: nil)
        guide.imagePosition = .imageLeading
        let footer = label("手动启动 · 私有组件 · 退出后清理", size: 11)
        footer.textColor = GuideTheme.muted
        let stack = column([eyebrow, title, subtitle, columns, row([guide, NSView(), footer])], spacing: 14)
        let root = GuideBackground()
        root.addSubview(stack); stack.translatesAutoresizingMaskIntoConstraints = false
        window.contentView = root
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -24),
            stack.topAnchor.constraint(equalTo: root.topAnchor, constant: 24),
            stack.bottomAnchor.constraint(lessThanOrEqualTo: root.bottomAnchor, constant: -20),
            columns.widthAnchor.constraint(equalTo: stack.widthAnchor),
            stack.views.last!.widthAnchor.constraint(equalTo: stack.widthAnchor),
            right.widthAnchor.constraint(equalTo: columns.widthAnchor, constant: -318),
        ])
        updateOnboarding()
    }

    func updateOnboarding() {
        guard !dashboard else { return }
        phaseText.stringValue = phase
        detailText.stringValue = detail
        authorization.title = authorizationLabel
        authorization.isHidden = authorizationURL == nil
        retry.title = setupContext != nil ? "重新校验" : "继续准备"
        retry.isEnabled = !busy
        retry.isHidden = busy
        setupCancelButton?.title = verificationActive ? "停止重试" : busy ? "取消准备" : "结束准备"
        setupCancelButton?.isEnabled = !accountCancelRequested
        setupActivity?.active = busy
        updateSetupSteps()
        refreshAccountUI()
    }

    func updateSetupSteps() {
        for stage in SetupStage.allCases {
            let status = setup[stage]
            let caption: String
            switch status {
            case .pending: caption = isolatedPreviewNotice != nil ? "未执行 · 隔离测试" : stage.subtitle
            case .active:
                if stage == .publicEntry, let state = verificationState {
                    caption = state.retryInSeconds != nil ? "等待自动重试" : "第 \(state.attempt) 次校验"
                } else { caption = "正在检查" }
            case .complete: caption = "已完成"
            case .paused: caption = "已停止 · 尚未完成验证"
            case .failed: caption = "需要处理 · 可按提示重试"
            }
            setupStateLabels[stage]?.stringValue = caption
            setupStateLabels[stage]?.textColor = status == .failed ? GuideTheme.link : status == .active ? GuideTheme.text : GuideTheme.muted
            setupNumberLabels[stage]?.stringValue = status == .complete ? "✓" : String(format: "%02d", stage.rawValue)
            setupNumberLabels[stage]?.textColor = status == .pending ? GuideTheme.muted : GuideTheme.link
        }
    }

    // Only labels change during countdowns. Keep native controls, keyboard focus
    // and the compositor-driven activity layer alive between ticks.
    func updateVerificationPresentation() {
        headerText?.stringValue = "\(publicStatusText)   ·   公网发布 \(usedSlots) / 3"
        dashboardNotice?.stringValue = notice
        projectVerificationLabel?.stringValue = verificationState?.text ?? "正在发布并验证…"
        detailText.stringValue = detail
        accountInformationLabel?.stringValue = verificationState?.text ?? accountPanelMessage
        accountBadgeLabel?.stringValue = publicStatusText
        accountSummary?.stringValue = accountSummaryText
        onboardingAccountSummary?.stringValue = accountSummaryText
        setupCancelButton?.title = "停止重试"
        cancelDashboardButton?.title = "停止重试"
        updateSetupSteps()
    }

    @objc func retryOnboarding() {
        if let context = setupContext, caddy?.running == true, accountClientRunning {
            run { try await self.completeSetupVerification(context) }
        } else { startSession() }
    }
    @objc func retryLastOperation() {
        if !dashboard { retryOnboarding() }
        else if let operation = retryOperation { run(operation) }
    }
    @objc func stopCurrentVerification() {
        guard busy else { return }
        verificationStopped = true
        if checkingPublic { entryVerified = false; publicVerificationIssue = "公网校验已停止，尚未完成验证。" }
        if !dashboard {
            setup.pause()
            // Stop exposing the bootstrap page while its public check is stopped.
            // Keep verified components and account state for a focused retry.
            funnels.removeValue(forKey: 443)?.stop()
        }
        task?.cancel()
    }
    @objc func cancelOnboarding() {
        if verificationActive { stopCurrentVerification() }
        else { cancelAccountWork() }
    }
    @objc func cancelCurrentOperation() {
        if verificationActive { stopCurrentVerification() } else { task?.cancel() }
    }
    @objc func toggleSession() {
        if caddy?.running == true || accountClientRunning { stopAll() }
        else { startAgain() }
    }
}
