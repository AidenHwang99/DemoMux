import AppKit
import UniformTypeIdentifiers
import WebKit

final class DashboardDocument: NSView {
    override var isFlipped: Bool { true }
}

// Match the standalone HTML guide's palette and use the same system fonts.
@MainActor
enum GuideTheme {
    static func color(_ hex: UInt32) -> NSColor {
        NSColor(srgbRed: CGFloat((hex >> 16) & 255) / 255,
                green: CGFloat((hex >> 8) & 255) / 255,
                blue: CGFloat(hex & 255) / 255, alpha: 1)
    }
    static let background = color(0x100d11)
    static let glow = color(0x331522)
    static let surface = color(0x19151a)
    static let raised = color(0x22181e)
    static let border = color(0x44303b)
    static let text = color(0xfaf2f5)
    static let muted = color(0xc8b6bf)
    static let accent = color(0xff5b75)
    static let link = color(0xffa3b4)
    static let action = color(0x8e354c)
}

final class GuideBackground: NSView {
    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        NSGradient(colorsAndLocations: (GuideTheme.glow, 0), (GuideTheme.background, 0.77),
                   (GuideTheme.background, 1))?.draw(in: bounds, relativeCenterPosition: NSPoint(x: 0.73, y: 0.88))
    }
}

@MainActor
final class DropArea: NSBox {
    var receive: (([URL]) -> Void)?

    override func draggingEntered(_ sender: any NSDraggingInfo) -> NSDragOperation {
        readURLs(sender).contains { ["html", "htm"].contains($0.pathExtension.lowercased()) } ? .copy : []
    }

    override func performDragOperation(_ sender: any NSDraggingInfo) -> Bool {
        let files = readURLs(sender).filter { ["html", "htm"].contains($0.pathExtension.lowercased()) }
        guard !files.isEmpty else { return false }
        receive?(files)
        return true
    }

    private func readURLs(_ sender: any NSDraggingInfo) -> [URL] {
        sender.draggingPasteboard.readObjects(forClasses: [NSURL.self],
                                              options: [.urlReadingFileURLsOnly: true]) as? [URL] ?? []
    }
}

@MainActor
final class PublicationEditor: NSObject, NSTextFieldDelegate {
    let method = NSPopUpButton(frame: .zero, pullsDown: false)
    let slug = NSTextField(string: "")
    let confirm = NSButton(checkboxWithTitle: "本机项目已配置为使用这个基础路径", target: nil, action: nil)
    let example = NSTextField(labelWithString: "")
    let copy = NSButton(title: "复制基础路径", target: nil, action: nil)
    let explanation = NSTextField(wrappingLabelWithString: "")
    let host: String
    let rootPort: Int?
    let reused: Bool
    let project: Project

    init(project: Project, host: String, rootPort: Int?, reused: Bool) {
        self.project = project
        self.host = host
        self.rootPort = rootPort
        self.reused = reused
        super.init()
        method.addItems(withTitles: ["按项目名发布", "使用独立入口"])
        method.autoenablesItems = false
        method.item(at: 1)?.isEnabled = rootPort != nil
        method.selectItem(at: project.source.port != nil && rootPort != nil ? 1 : 0)
        method.target = self
        method.action = #selector(updateMethod)
        method.setAccessibilityLabel("发布方式")
        slug.stringValue = project.slug
        slug.isEditable = !project.lockedSlug
        slug.setContentHuggingPriority(.defaultLow - 1, for: .horizontal)
        slug.setAccessibilityLabel("项目标识")
        slug.delegate = self
        example.isSelectable = true
        example.textColor = GuideTheme.link
        example.lineBreakMode = .byTruncatingMiddle
        explanation.maximumNumberOfLines = 3
        explanation.textColor = GuideTheme.muted
        confirm.isHidden = project.source.port == nil
        copy.target = self
        copy.action = #selector(copyPath)
        update()
    }

    var path: String { "/p/\(slug.stringValue.trimmingCharacters(in: .whitespacesAndNewlines))/" }
    var usesRoot: Bool { method.indexOfSelectedItem == 1 && rootPort != nil }

    func controlTextDidChange(_ obj: Notification) {
        confirm.state = .off
        update()
    }

    @objc private func updateMethod() { update() }

    private func update() {
        slug.isEnabled = !usesRoot
        copy.isEnabled = !usesRoot
        confirm.isEnabled = !usesRoot
        if usesRoot, let rootPort {
            let suffix = rootPort == 443 ? "" : ":\(rootPort)"
            example.stringValue = "https://\(host)\(suffix)/"
            var details = rootPort == 443
                ? "适用于从根路径加载资源的项目。/p/ 和 /.demomux/ 仍保留。"
                : "适用于从根路径加载资源或 API 的项目。"
            if reused { details += " 之前分享的同一地址将指向这个新项目。" }
            explanation.stringValue = details
            explanation.textColor = reused ? GuideTheme.link : GuideTheme.muted
        } else {
            example.stringValue = "https://\(host)\(path)"
            explanation.stringValue = project.source.port == nil
                ? "适用于使用相对路径加载资源的 HTML。"
                : "请先将原项目配置为使用这个基础路径，再勾选下方确认。"
            explanation.textColor = GuideTheme.muted
        }
        example.toolTip = example.stringValue
    }

    @objc private func copyPath() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(path, forType: .string)
    }

    func view() -> NSView {
        let idRow = NSStackView(views: [
            NSTextField(labelWithString: "项目标识"), slug, copy,
        ])
        idRow.spacing = 8
        let controls: [NSView] = [method, example, explanation, idRow] + (confirm.isHidden ? [] : [confirm])
        let stack = NSStackView(views: controls)
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 8
        stack.edgeInsets = NSEdgeInsets(top: 4, left: 0, bottom: 4, right: 0)
        for view in [example, explanation, idRow] {
            view.widthAnchor.constraint(equalToConstant: 462).isActive = true
        }
        explanation.heightAnchor.constraint(equalToConstant: 48).isActive = true
        stack.layoutSubtreeIfNeeded()
        stack.frame = NSRect(origin: .zero, size: stack.fittingSize)
        return stack
    }
}

extension DemoMuxWindow {
    func buildMenu() {
        let menu = NSMenu()
        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        let help = NSMenuItem(title: "DemoMux 使用说明", action: #selector(openGuidePage), keyEquivalent: "?")
        help.target = self
        appMenu.addItem(help)
        let settings = NSMenuItem(title: "设置…", action: #selector(openSettings), keyEquivalent: ",")
        settings.target = self; appMenu.addItem(settings)
        appMenu.addItem(.separator())
        appMenu.addItem(NSMenuItem(title: "退出 DemoMux", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        appItem.submenu = appMenu
        menu.addItem(appItem)
        let fileItem = NSMenuItem()
        let file = NSMenu(title: "项目")
        for (name, action, key) in [("添加 HTML 文件…", #selector(addHTMLFiles), "o"),
                                    ("导入项目文件夹…", #selector(addFolder), "O"),
                                    ("连接本机端口…", #selector(addPort), "l"),
                                    ("检测公网连接", #selector(checkGuide), "r"),
                                    ("停止当前操作", #selector(cancelCurrentOperation), ".")] {
            let item = NSMenuItem(title: name, action: action, keyEquivalent: key)
            item.target = self
            if key == "O" { item.keyEquivalent = "o"; item.keyEquivalentModifierMask = [.command, .shift] }
            file.addItem(item)
        }
        fileItem.submenu = file; menu.addItem(fileItem)
        let editItem = NSMenuItem()
        let edit = NSMenu(title: "编辑")
        for (name, action, key) in [
            ("剪切", "cut:", "x"), ("复制", "copy:", "c"),
            ("粘贴", "paste:", "v"), ("全选", "selectAll:", "a"),
        ] {
            edit.addItem(NSMenuItem(title: name, action: NSSelectorFromString(action), keyEquivalent: key))
        }
        editItem.submenu = edit
        menu.addItem(editItem)
        NSApplication.shared.mainMenu = menu
    }

    func label(_ text: String, size: CGFloat = 13, weight: NSFont.Weight = .regular) -> NSTextField {
        let field = NSTextField(labelWithString: text)
        field.font = .systemFont(ofSize: size, weight: weight)
        field.textColor = GuideTheme.text
        return field
    }

    func button(_ text: String, _ action: Selector, id: UUID? = nil, primary: Bool = false) -> NSButton {
        let control = NSButton(title: text, target: self, action: action)
        styleButton(control, primary: primary)
        let symbols: [String: String] = ["使用说明": "book.closed", "添加账号": "person.badge.plus",
            "刷新账号": "arrow.clockwise", "退出当前账号": "rectangle.portrait.and.arrow.right",
            "暂停演示": "pause.circle", "停止重试": "stop.circle", "取消当前步骤": "xmark.circle",
            "管理账号": "person.crop.circle", "重新校验": "arrow.clockwise", "检测公网连接": "network"]
        if let symbol = symbols[text] {
            control.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)
            control.imagePosition = .imageLeading
        }
        if let id { control.identifier = NSUserInterfaceItemIdentifier(id.uuidString) }
        return control
    }

    private func styleButton(_ control: NSButton, primary: Bool) {
        control.bezelStyle = .rounded
        control.bezelColor = primary ? GuideTheme.action : nil
        control.font = .systemFont(ofSize: 13, weight: primary ? .semibold : .regular)
    }

    private func sectionTitle(_ number: String, _ title: String) -> NSView {
        let index = label(number)
        index.font = .monospacedSystemFont(ofSize: 12, weight: .semibold)
        index.textColor = GuideTheme.accent
        return row([index, label(title, size: 18, weight: .semibold)], spacing: 10)
    }

    func glass(_ content: NSView, radius: CGFloat, x: CGFloat, y: CGFloat) -> NSGlassEffectView {
        let glass = NSGlassEffectView()
        glass.style = .regular
        glass.cornerRadius = radius
        glass.tintColor = GuideTheme.glow.withAlphaComponent(0.62)
        let inset = NSView()
        glass.contentView = inset
        inset.addSubview(content)
        content.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            content.leadingAnchor.constraint(equalTo: glass.leadingAnchor, constant: x),
            content.trailingAnchor.constraint(equalTo: glass.trailingAnchor, constant: -x),
            content.topAnchor.constraint(equalTo: glass.topAnchor, constant: y),
            content.bottomAnchor.constraint(equalTo: glass.bottomAnchor, constant: -y),
        ])
        return glass
    }

    func column(_ views: [NSView], spacing: CGFloat = 8) -> NSStackView {
        let stack = NSStackView(views: views)
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = spacing
        return stack
    }

    func row(_ views: [NSView], spacing: CGFloat = 8) -> NSStackView {
        let stack = NSStackView(views: views)
        stack.spacing = spacing
        return stack
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction,
                 decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = navigationAction.request.url else { decisionHandler(.cancel); return }
        if navigationAction.navigationType == .linkActivated, url.scheme == "https" {
            NSWorkspace.shared.open(url)
            decisionHandler(.cancel)
        } else {
            decisionHandler(url.isFileURL ? .allow : .cancel)
        }
    }

    @objc func openAuthorization() {
        if let authorizationURL { NSWorkspace.shared.open(authorizationURL) }
    }

    // A centered readable canvas with a separate footer region. The viewport
    // ends above the glass toolbar, so its final row can never sit underneath it.
    func responsivePage(content: NSStackView, footer: NSView, maximumWidth: CGFloat) -> NSView {
        let root = GuideBackground()
        let scroll = NSScrollView()
        scroll.identifier = .init("page-scroll")
        scroll.hasVerticalScroller = true; scroll.scrollerStyle = .overlay
        scroll.scrollerKnobStyle = .light; scroll.drawsBackground = false
        let document = DashboardDocument()
        document.translatesAutoresizingMaskIntoConstraints = false
        scroll.documentView = document
        document.addSubview(content); content.translatesAutoresizingMaskIntoConstraints = false
        content.identifier = .init("page-content")
        footer.identifier = .init("page-footer")
        root.addSubview(scroll); root.addSubview(footer)
        scroll.translatesAutoresizingMaskIntoConstraints = false
        footer.translatesAutoresizingMaskIntoConstraints = false
        let preferredContentWidth = content.widthAnchor.constraint(equalTo: document.widthAnchor, constant: -48)
        preferredContentWidth.priority = .defaultHigh
        let preferredFooterWidth = footer.widthAnchor.constraint(equalTo: root.widthAnchor, constant: -48)
        preferredFooterWidth.priority = .defaultHigh
        NSLayoutConstraint.activate([
            scroll.topAnchor.constraint(equalTo: root.topAnchor),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            scroll.bottomAnchor.constraint(equalTo: footer.topAnchor, constant: -12),
            footer.centerXAnchor.constraint(equalTo: root.centerXAnchor),
            footer.widthAnchor.constraint(lessThanOrEqualToConstant: maximumWidth),
            footer.leadingAnchor.constraint(greaterThanOrEqualTo: root.leadingAnchor, constant: 24),
            footer.trailingAnchor.constraint(lessThanOrEqualTo: root.trailingAnchor, constant: -24),
            preferredFooterWidth,
            footer.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -14),
            document.topAnchor.constraint(equalTo: scroll.contentView.topAnchor),
            document.leadingAnchor.constraint(equalTo: scroll.contentView.leadingAnchor),
            document.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
            content.centerXAnchor.constraint(equalTo: document.centerXAnchor),
            content.widthAnchor.constraint(lessThanOrEqualToConstant: maximumWidth),
            content.leadingAnchor.constraint(greaterThanOrEqualTo: document.leadingAnchor, constant: 24),
            content.trailingAnchor.constraint(lessThanOrEqualTo: document.trailingAnchor, constant: -24),
            preferredContentWidth,
            content.topAnchor.constraint(equalTo: document.topAnchor, constant: 24),
            content.bottomAnchor.constraint(equalTo: document.bottomAnchor, constant: -24),
        ])
        content.setHuggingPriority(.required, for: .vertical)
        return root
    }

    func showDashboard() {
        dashboard = true
        window.appearance = NSAppearance(named: .darkAqua)
        window.backgroundColor = GuideTheme.background
        let title = label("DemoMux", size: 36, weight: .bold)
        let eyebrow = label(isolatedPreviewNotice ?? "本机项目  ·  公网演示", size: 12, weight: .semibold)
        eyebrow.textColor = GuideTheme.accent
        headerText = label("", size: 13, weight: .semibold)
        let status = glass(headerText, radius: 15, x: 13, y: 7)
        let identityCard = accountIdentityCard()
        dashboardNotice = NSTextField(wrappingLabelWithString: "")
        dashboardNotice.textColor = GuideTheme.muted
        dashboardNotice.maximumNumberOfLines = 2

        let addProject = button("添加项目", #selector(showAddProjectMenu), primary: true)
        addProject.image = NSImage(systemSymbolName: "plus", accessibilityDescription: nil)
        addProject.imagePosition = .imageLeading
        addProject.toolTip = "添加 HTML、项目文件夹或已经启动的本机 HTTP 服务"
        let openGuide = button("使用说明", #selector(openGuidePage))
        openGuide.image = NSImage(systemSymbolName: "book.closed", accessibilityDescription: nil)
        openGuide.imagePosition = .imageLeading
        let check = button("检测公网连接", #selector(checkGuide))
        check.image = NSImage(systemSymbolName: "arrow.clockwise", accessibilityDescription: nil)
        check.imagePosition = .imageLeading
        checkPublicButton = check
        let session = button("启动演示", #selector(toggleSession), primary: true)
        sessionActionButton = session
        let settings = settingsButton()
        quickSwitch = NSPopUpButton(frame: .zero, pullsDown: false)
        quickSwitch.target = self
        quickSwitch.action = #selector(openSessionProject)
        quickSwitch.setAccessibilityLabel("本次会话端口")
        quickSwitch.widthAnchor.constraint(equalToConstant: 200).isActive = true
        commandButtons = [addProject, openGuide, check, session]
        let retryAction = button("重新校验", #selector(retryLastOperation), primary: true)
        let cancelAction = button("停止重试", #selector(cancelCurrentOperation))
        retryDashboardButton = retryAction; cancelDashboardButton = cancelAction
        let motion = VerificationActivityView(frame: .zero)
        dashboardActivity = motion
        let progressBody = column([dashboardNotice, motion], spacing: 7)
        for item in progressBody.views { item.widthAnchor.constraint(equalTo: progressBody.widthAnchor).isActive = true }
        let progressCard = accountCard(progressBody, padding: 12)

        let drop = DropArea()
        drop.boxType = .custom
        drop.fillColor = GuideTheme.raised
        drop.borderColor = GuideTheme.border
        drop.borderWidth = 1
        drop.cornerRadius = 12
        drop.setAccessibilityLabel("拖入 HTML 文件，也可以点击添加 HTML")
        drop.registerForDraggedTypes([.fileURL])
        drop.receive = { [weak self] urls in self?.importFiles(urls) }
        let dropTitle = label("拖入 HTML 文件", size: 14, weight: .semibold)
        dropTitle.textColor = GuideTheme.link
        let dropCaption = NSTextField(wrappingLabelWithString: "有图片、CSS 或 JavaScript 的页面可导入项目文件夹")
        dropCaption.font = .systemFont(ofSize: 12)
        dropCaption.textColor = GuideTheme.muted
        let dropBody = column([dropTitle, dropCaption], spacing: 4)
        drop.contentView!.addSubview(dropBody)
        dropBody.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            dropBody.leadingAnchor.constraint(equalTo: drop.leadingAnchor, constant: 14),
            dropBody.trailingAnchor.constraint(equalTo: drop.trailingAnchor, constant: -14),
            dropBody.topAnchor.constraint(equalTo: drop.topAnchor, constant: 14),
            dropBody.bottomAnchor.constraint(equalTo: drop.bottomAnchor, constant: -14),
            dropCaption.widthAnchor.constraint(equalTo: dropBody.widthAnchor),
            drop.heightAnchor.constraint(greaterThanOrEqualToConstant: 62),
        ])

        projectList = column([])
        shareList = column([])
        let heading = ResponsiveHeading(column([eyebrow, title], spacing: 3), status)
        let addRow = row([addProject, NSView(), quickSwitch])
        let shareSpace = NSView(), librarySpace = NSView()
        for space in [shareSpace, librarySpace] {
            space.setContentHuggingPriority(.init(1), for: .vertical)
            space.heightAnchor.constraint(greaterThanOrEqualToConstant: 0).isActive = true
        }
        let sharing = column([sectionTitle("01", "正在分享"), shareList, shareSpace], spacing: 12)
        let library = column([sectionTitle("02", "项目列表"), projectList, librarySpace], spacing: 12)
        sharing.setCustomSpacing(0, after: shareList)
        library.setCustomSpacing(0, after: projectList)
        sharing.setHuggingPriority(.required, for: .vertical)
        library.setHuggingPriority(.required, for: .vertical)
        shareList.widthAnchor.constraint(equalTo: sharing.widthAnchor).isActive = true
        projectList.widthAnchor.constraint(equalTo: library.widthAnchor).isActive = true
        let projects = ResponsivePair(sharing, library, breakpoint: 1060)
        projects.identifier = .init("project-sections")
        let content = column([heading, progressCard, identityCard, addRow, drop, projects], spacing: 16)
        let footerBody = ResponsiveFooter(views: [row([openGuide, settings, check, retryAction, cancelAction, NSView()], spacing: 7),
                                                   row([NSView(), session], spacing: 12)])
        let toolbar = glass(footerBody, radius: 18, x: 13, y: 10)
        window.contentView = responsivePage(content: content, footer: toolbar, maximumWidth: 1200)
        for view in [heading, progressCard, identityCard, addRow, drop, projects] {
            view.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true
        }
        content.setHuggingPriority(.required, for: .vertical)
        shareList.setHuggingPriority(.required, for: .vertical)
        projectList.setHuggingPriority(.required, for: .vertical)
        refreshDashboard()
        refreshAccountUI()
    }

    func refreshDashboard() {
        guard dashboard, let headerText else { return }
        headerText.stringValue = "\(publicStatusText)   ·   公网发布 \(usedSlots) / \(PublicationSlots.limit)"
        headerText.textColor = ready ? GuideTheme.link : GuideTheme.muted
        dashboardNotice.stringValue = notice
        for control in commandButtons { control.isEnabled = !busy }
        commandButtons.first { ($0 as? NSButton)?.title == "使用说明" }?.isEnabled = true
        checkPublicButton?.isEnabled = servicesAvailable && !busy
        let sessionRunning = caddy?.running == true || accountClientRunning
        sessionActionButton?.title = sessionRunning ? "结束演示" : "启动演示"
        sessionActionButton?.image = NSImage(systemSymbolName: sessionRunning ? "stop.circle" : "play.circle", accessibilityDescription: nil)
        sessionActionButton?.imagePosition = .imageLeading
        retryDashboardButton?.isHidden = busy || !operationNeedsRetry
        retryDashboardButton?.title = verificationState != nil || publicVerificationIssue != nil ? "重新校验" : "重试操作"
        cancelDashboardButton?.isHidden = !busy
        cancelDashboardButton?.title = verificationActive ? "停止重试" : "取消操作"
        dashboardActivity?.isHidden = !busy
        dashboardActivity?.active = busy
        projectVerificationLabel = nil

        quickSwitch.removeAllItems()
        quickSwitch.addItem(withTitle: "本次会话 · 快速预览")
        quickSwitch.lastItem?.isEnabled = false
        for id in sessionPorts {
            guard let project = catalog.projects.first(where: { $0.id == id }), let port = project.source.port else { continue }
            quickSwitch.addItem(withTitle: "\(project.name) · \(port)")
            quickSwitch.lastItem?.representedObject = id.uuidString
        }
        quickSwitch.selectItem(at: 0)
        quickSwitch.isEnabled = ready && !busy && quickSwitch.numberOfItems > 1

        replaceRows(shareList, with: catalog.projects.filter {
            slots.shares[$0.id] != nil || $0.id == pendingID
        }.map { projectRow($0, sharing: true) }, empty: "暂无公网项目。添加项目后，可从列表发布。")
        replaceRows(projectList, with: catalog.projects.filter {
            slots.shares[$0.id] == nil && $0.id != pendingID
        }.map { projectRow($0, sharing: false) }, empty: "可以拖入 HTML，或添加已经运行的本机服务。")
        refreshAccountUI()
    }

    func replaceRows(_ stack: NSStackView, with rows: [NSView], empty: String) {
        for old in stack.arrangedSubviews { stack.removeArrangedSubview(old); old.removeFromSuperview() }
        if rows.isEmpty {
            let caption = NSTextField(wrappingLabelWithString: empty)
            caption.font = .systemFont(ofSize: 13)
            caption.textColor = GuideTheme.muted
            stack.addArrangedSubview(caption)
            caption.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        } else {
            for row in rows {
                stack.addArrangedSubview(row)
                row.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
            }
        }
    }

    func projectRow(_ project: Project, sharing: Bool) -> NSView {
        let valid = project.source.sourceExists
        let running = active[project.id] != nil
        let pending = pendingID == project.id
        let share = slots.shares[project.id]
        let name = label(project.name, size: 14, weight: .semibold)
        name.lineBreakMode = .byTruncatingMiddle; name.toolTip = project.name
        let state = !valid ? "源文件失效" : running ? "预览中" : "已停止"
        let source = label(project.source.description + "  ·  " + state, size: 11)
        source.textColor = valid ? GuideTheme.muted : GuideTheme.link
        source.lineBreakMode = .byTruncatingMiddle; source.toolTip = project.source.description
        var descriptions: [NSView] = [name, source]
        if let share, let host = publicHost {
            let status = label((share.verified ? "已发布" : "公网待复核") + (slots.homeProjectID == project.id ? "  ·  公网首页" : ""), size: 11, weight: .medium)
            status.textColor = GuideTheme.link
            descriptions.append(status)
            let url = share.url(host: host, project: project).absoluteString
            let address = label(url, size: 11)
            address.textColor = GuideTheme.muted; address.isSelectable = true
            address.lineBreakMode = .byTruncatingMiddle; address.toolTip = url
            descriptions.append(address)
        } else if pending {
            let progress = NSTextField(wrappingLabelWithString: verificationProgress ?? "正在发布并验证…")
            progress.font = .systemFont(ofSize: 11); progress.textColor = GuideTheme.link
            progress.maximumNumberOfLines = 3
            descriptions.append(progress); projectVerificationLabel = progress
        }
        if !pending, let error = diagnostics[project.id] {
            let warning = NSTextField(wrappingLabelWithString: error)
            warning.font = .systemFont(ofSize: 11); warning.textColor = GuideTheme.link
            warning.maximumNumberOfLines = 2; warning.toolTip = error
            descriptions.append(warning)
        }
        for view in descriptions {
            view.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
            view.setContentHuggingPriority(.defaultLow, for: .horizontal)
        }
        let info = column(descriptions, spacing: 4)
        for view in descriptions { view.widthAnchor.constraint(equalTo: info.widthAnchor).isActive = true }
        let preview = iconButton(share != nil ? "打开网页" : "本机预览", icon: share != nil ? "external-link" : "eye",
                                 action: share != nil ? #selector(openPublic) : #selector(previewProject), id: project.id,
                                 enabled: !pending && (share != nil || servicesAvailable), primary: false)
        let copy = iconButton("复制链接", icon: "copy", action: #selector(copyPublic), id: project.id,
                              enabled: share != nil && !pending, primary: share != nil)
        if share == nil {
            copy.toolTip = "复制链接 · 发布后可用"
            copy.setAccessibilityHelp("复制链接，发布后可用")
        }
        let lifecycle = iconButton(running ? "停止项目" : "启动项目", icon: running ? "circle-stop" : "circle-play",
                                   action: #selector(toggleProjectLifecycle), id: project.id,
                                   enabled: !pending && servicesAvailable && (running || valid))
        let publication = iconButton(pending ? "发布中" : share != nil ? "取消发布" : "发布",
                                     icon: share != nil ? "cloud-off" : "cloud-upload",
                                     action: #selector(toggleProjectPublication), id: project.id,
                                     enabled: !pending && (share != nil || ready && valid), primary: share == nil)
        if !ready, share == nil { publication.toolTip = "发布 · 先完成公网校验" }
        else if slots.shares.count >= PublicationSlots.limit, share == nil { publication.toolTip = "名额已满 · 点击查看当前分享的项目" }
        let more = iconButton("更多操作", icon: "ellipsis", action: #selector(showProjectMenu), id: project.id,
                              enabled: !pending)
        let controls = row([preview, copy, lifecycle, publication, more], spacing: 6)
        controls.widthAnchor.constraint(equalToConstant: 204).isActive = true
        controls.setContentHuggingPriority(.required, for: .horizontal)
        controls.setContentCompressionResistancePriority(.required, for: .horizontal)
        let body = ResponsiveProjectBody(info: info, controls: controls)
        body.setHuggingPriority(.required, for: .vertical)
        let card = accountCard(body, padding: 16)
        card.heightAnchor.constraint(greaterThanOrEqualToConstant: 94).isActive = true
        card.setContentHuggingPriority(.required, for: .vertical)
        return card
    }

    func project(_ sender: NSButton) -> Project? {
        guard let identifier = sender.identifier?.rawValue, let id = UUID(uuidString: identifier) else { return nil }
        return catalog.projects.first { $0.id == id }
    }

    func showError(_ error: Error) {
        notice = safeMessage(error.localizedDescription)
        refreshDashboard()
    }

    @objc func addHTMLFiles() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.html]
        panel.allowsMultipleSelection = true
        panel.message = "选择独立 HTML 文件。带配套资源的页面可使用「导入项目文件夹」。"
        if panel.runModal() == .OK { importFiles(panel.urls) }
    }

    func importFiles(_ files: [URL]) {
        guard !busy else { return }
        var candidate = catalog
        var added = 0
        for file in files {
            let source = ProjectSource.html(file: file.standardizedFileURL)
            guard ["html", "htm"].contains(file.pathExtension.lowercased()), source.sourceExists,
                  !candidate.projects.contains(where: { $0.source == source }) else { continue }
            _ = candidate.add(source, name: file.deletingPathExtension().lastPathComponent)
            added += 1
        }
        do {
            if added > 0 { try saveCatalog(candidate) }
            notice = added > 0 ? "已添加 \(added) 个 HTML 项目。" : "未找到可导入的新 HTML 文件。"
            refreshDashboard()
        } catch { showError(error) }
    }

    func chooseFolder() -> ProjectSource? {
        let folder = NSOpenPanel()
        folder.canChooseDirectories = true
        folder.canChooseFiles = false
        folder.message = "请选择专用项目文件夹。发布时，该文件夹中的普通文件可能被公网访问。"
        guard folder.runModal() == .OK, let root = folder.url?.standardizedFileURL else { return nil }
        let picker = NSOpenPanel()
        picker.allowedContentTypes = [.html]
        picker.directoryURL = root
        picker.message = "选择此项目文件夹内的入口 HTML。"
        guard picker.runModal() == .OK, let entry = picker.url?.standardizedFileURL else { return nil }
        guard entry.path.hasPrefix(root.path + "/") else {
            showError(AppError("入口 HTML 必须在选定的项目文件夹内。"))
            return nil
        }
        let source = ProjectSource.folder(root: root, entry: String(entry.path.dropFirst(root.path.count + 1)))
        guard source.sourceExists else {
            showError(AppError("入口 HTML 无法读取，或位于隐藏目录、符号链接中。"))
            return nil
        }
        return source
    }

    @objc func addFolder() {
        guard let source = chooseFolder(), case let .folder(root, _) = source else { return }
        if catalog.projects.contains(where: { $0.source == source }) {
            showError(AppError("该项目文件夹与入口已添加。"))
            return
        }
        var candidate = catalog
        _ = candidate.add(source, name: root.lastPathComponent)
        do {
            try saveCatalog(candidate)
            notice = "已添加 HTML 项目文件夹。"
            refreshDashboard()
        } catch { showError(error) }
    }

    @objc func addPort() {
        let dialog = NSAlert()
        dialog.messageText = "添加本机 HTTP 服务"
        dialog.informativeText = "先用项目自己的工具启动服务，再输入它在 127.0.0.1 上的端口。"
        dialog.addButton(withTitle: "检查并添加")
        dialog.addButton(withTitle: "取消")
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 260, height: 24))
        field.placeholderString = "如 5173"
        field.setAccessibilityLabel("本机端口")
        dialog.accessoryView = field
        dialog.window.initialFirstResponder = field
        guard dialog.runModal() == .alertFirstButtonReturn else { return }
        guard let port = Int(field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)),
              validProjectPort(port), port != localPort, !roots.values.contains(port) else {
            showError(AppError("请输入有效的本机项目端口。\(previewPort) 和 DemoMux 的转发端口为保留端口。"))
            return
        }
        guard !catalog.projects.contains(where: { $0.source.port == port }) else {
            showError(AppError("端口 \(port) 已在项目列表中。"))
            return
        }
        run {
            do { _ = try await Task.detached { try localHTTPStatus(port: port) }.value }
            catch { throw AppError("端口 \(port) 未响应。请先启动该项目。") }
            var candidate = self.catalog
            _ = candidate.add(.port(port), name: "本机 \(port)")
            try self.saveCatalog(candidate)
            self.notice = "已添加本机端口 \(port)。"
        }
    }

    @objc func renameProject(_ sender: NSButton) {
        guard let project = project(sender), let index = catalog.projects.firstIndex(where: { $0.id == project.id }) else { return }
        let dialog = NSAlert()
        dialog.messageText = "修改显示名称"
        dialog.informativeText = "公网链接仍使用原项目标识。"
        dialog.addButton(withTitle: "保存")
        dialog.addButton(withTitle: "取消")
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 260, height: 24))
        field.stringValue = project.name
        field.setAccessibilityLabel("显示名称")
        dialog.accessoryView = field
        dialog.window.initialFirstResponder = field
        guard dialog.runModal() == .alertFirstButtonReturn else { return }
        let name = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name.count <= 80 else { showError(AppError("请输入 1 到 80 个字符的名称。")); return }
        var candidate = catalog
        candidate.projects[index].name = name
        do { try saveCatalog(candidate); refreshDashboard() }
        catch { showError(error) }
    }

    @objc func relocateProject(_ sender: NSButton) {
        guard let project = project(sender) else { return }
        let source: ProjectSource?
        switch project.source {
        case .html:
            let picker = NSOpenPanel()
            picker.allowedContentTypes = [.html]
            picker.message = "选择此项目的新 HTML 文件位置。"
            source = picker.runModal() == .OK ? picker.url.map { .html(file: $0.standardizedFileURL) } : nil
        case .folder:
            source = chooseFolder()
        case .port(let oldPort):
            let dialog = NSAlert()
            dialog.messageText = "更换本机端口"
            dialog.informativeText = "先启动新服务。显示名称与项目标识保持，下一次预览使用新端口。"
            let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 260, height: 24))
            field.stringValue = String(oldPort); field.setAccessibilityLabel("新的本机端口")
            dialog.accessoryView = field; dialog.window.initialFirstResponder = field
            dialog.addButton(withTitle: "检查并保存"); dialog.addButton(withTitle: "取消")
            guard dialog.runModal() == .alertFirstButtonReturn else { return }
            guard let port = Int(field.stringValue), validProjectPort(port) else {
                showError(AppError("请输入可用的本机端口（1–65535），不能使用 DemoMux 的 8081。")); return
            }
            source = .port(port)
        }
        guard let source else { return }
        guard source.sourceExists else { showError(AppError("新位置无法读取。")); return }
        run { try await self.relocate(project, to: source) }
    }

    func relocate(_ project: Project, to source: ProjectSource) async throws {
        guard slots.shares[project.id] == nil else { throw AppError("请先取消发布，再更换项目来源。") }
        guard let index = catalog.projects.firstIndex(where: { $0.id == project.id }) else { return }
        if let port = source.port {
            _ = try await Task.detached { try localHTTPStatus(port: port) }.value
        }
        var candidate = catalog
        candidate.projects[index].source = source
        var updated = active
        let previous = updated.removeValue(forKey: project.id)
        do {
            if caddy?.running == true { try await reload(active: updated, shares: slots, roots: roots) }
            try saveCatalog(candidate)
        } catch {
            if caddy?.running == true { try? await reload(active: active, shares: slots, roots: roots) }
            throw error
        }
        active = updated
        previous?.discard()
        diagnostics[project.id] = nil
        notice = "项目来源已更新。下次预览会重新检查新位置，原项目标识保持。"
    }

    @objc func previewProject(_ sender: NSButton) {
        guard let project = project(sender) else { return }
        openLocal(project)
    }

    func openLocal(_ project: Project) {
        run {
            try await self.prepare(project)
            self.remember(project)
            let url: URL
            if let port = project.source.port {
                let path = self.slots.shares[project.id]?.publication == .path ? project.path : "/"
                url = URL(string: "http://127.0.0.1:\(port)\(path)")!
            } else {
                guard let port = self.localPort else { return }
                url = URL(string: "http://127.0.0.1:\(port)\(project.localPath)")!
            }
            NSWorkspace.shared.open(url)
        }
    }

    @objc func openSessionProject() {
        guard let text = quickSwitch.selectedItem?.representedObject as? String, let id = UUID(uuidString: text),
              let project = catalog.projects.first(where: { $0.id == id }) else { return }
        if let host = publicHost, let share = slots.shares[id], share.verified {
            NSWorkspace.shared.open(share.url(host: host, project: project))
        } else {
            openLocal(project)
        }
    }

    @objc func startOrRestart(_ sender: NSButton) {
        guard let project = project(sender) else { return }
        run {
            do {
                try await self.prepare(project, force: true)
                self.notice = "「\(project.name)」本机预览已就绪。"
            } catch {
                if self.active[project.id] != nil, !project.source.sourceExists {
                    try await self.withdraw(project.id, stop: true)
                }
                self.diagnostics[project.id] = verificationErrorMessage(error)
                throw error
            }
        }
    }

    @objc func stopProject(_ sender: NSButton) {
        guard let project = project(sender) else { return }
        run {
            try await self.withdraw(project.id, stop: true)
            self.notice = "已停止「\(project.name)」的预览和发布。"
        }
    }

    @objc func requestPublication(_ sender: NSButton) {
        guard let project = project(sender), let host = publicHost else { return }
        guard slots.shares.count < PublicationSlots.limit else {
            let full = NSAlert()
            full.messageText = "三个公网名额已用完"
            let names = catalog.projects.filter { slots.shares[$0.id] != nil }.map(\.name)
            full.informativeText = "正在分享 \(names.joined(separator: "、"))。请先在「正在分享」取消一项，再发布「\(project.name)」。"
            full.addButton(withTitle: "返回正在分享")
            full.runModal()
            projectList.enclosingScrollView?.documentView?.scroll(.zero)
            return
        }
        let root = slots.freeRoot
        let editor = PublicationEditor(project: project, host: host, rootPort: root,
                                       reused: root.map { catalog.reusesRoot($0, project: project.id, profile: rootAccountID, host: host) } ?? false)
        let dialog = NSAlert()
        dialog.messageText = "发布「\(project.name)」"
        dialog.informativeText = "选择发布方式并检查链接。任何拿到公网链接的人都可以访问。"
        dialog.window.appearance = NSAppearance(named: .darkAqua)
        dialog.accessoryView = editor.view()
        dialog.addButton(withTitle: "发布并验证")
        dialog.addButton(withTitle: "取消")
        guard dialog.runModal() == .alertFirstButtonReturn else { return }
        if editor.usesRoot, let root {
            run { try await self.publishProject(project, publication: .root(root), slug: project.slug) }
        } else {
            let slug = editor.slug.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            guard validSlug(slug),
                  !catalog.projects.contains(where: { $0.id != project.id && $0.slug == slug }) else {
                showError(AppError("项目标识无效或已被使用。请换一个小写字母、数字和连字符组合。"))
                return
            }
            guard project.source.port == nil || editor.confirm.state == .on else {
                showError(AppError("请先为本机项目配置 \(editor.path)，再勾选确认。"))
                return
            }
            run { try await self.publishProject(project, publication: .path, slug: slug) }
        }
    }

    func publicAddress(_ sender: NSButton) -> URL? {
        guard let project = project(sender), let publicHost, let share = slots.shares[project.id] else { return nil }
        return share.url(host: publicHost, project: project)
    }

    @objc func copyPublic(_ sender: NSButton) {
        guard let address = publicAddress(sender) else { return }
        copyPublicValue(address.absoluteString)
    }
    func copyPublicValue(_ value: String) {
        NSPasteboard.general.clearContents()
        if NSPasteboard.general.setString(value, forType: .string) {
            notice = "链接已复制，可以粘贴分享。"
            operationNeedsRetry = false
        } else {
            notice = "剪贴板正在使用，未能复制链接。请点击「重试操作」。"
            operationNeedsRetry = true
            retryOperation = { self.copyPublicValue(value) }
        }
        refreshDashboard()
    }

    @objc func openPublic(_ sender: NSButton) {
        if let address = publicAddress(sender) { NSWorkspace.shared.open(address) }
    }

    @objc func cancelPublic(_ sender: NSButton) {
        guard let project = project(sender) else { return }
        run {
            try await self.withdraw(project.id, stop: false)
            self.notice = "已取消发布「\(project.name)」。"
        }
    }

    @objc func checkProjectLink(_ sender: NSButton) {
        guard let project = project(sender), let address = publicAddress(sender),
              let share = slots.shares[project.id] else { return }
        notice = "正在检查「\(project.name)」的公网入口…"
        diagnostics[project.id] = nil
        run {
            do {
                try await self.waitForPublic(address, marker: share.marker)
                self.diagnostics[project.id] = nil
                self.slots.shares[project.id]?.verified = true
                self.notice = "「\(project.name)」公网入口检查通过。"
            } catch {
                self.diagnostics[project.id] = verificationErrorMessage(error)
                self.slots.shares[project.id]?.verified = false
                throw error
            }
        }
    }

    @objc func openGuidePage() {
        if let helpWindow {
            helpWindow.makeKeyAndOrderFront(nil)
            return
        }
        let help = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1000, height: 720),
                            styleMask: [.titled, .closable, .miniaturizable, .resizable],
                            backing: .buffered, defer: false)
        help.title = "DemoMux 使用说明"
        help.minSize = NSSize(width: 650, height: 470)
        help.appearance = NSAppearance(named: .darkAqua)
        help.backgroundColor = GuideTheme.background
        help.isReleasedWhenClosed = false
        let web = WKWebView()
        web.navigationDelegate = self
        web.loadFileURL(guide.appendingPathComponent("index.html"), allowingReadAccessTo: guide)
        help.contentView = web
        help.center()
        helpWindow = help
        help.makeKeyAndOrderFront(nil)
    }

    @objc func checkGuide() {
        guard let guideURL, servicesAvailable else { return }
        notice = "正在检查公网首页和已发布项目…"
        run {
            self.checkingPublic = true
            self.entryVerified = false
            self.publicVerificationIssue = nil
            self.refreshDashboard()
            self.refreshAccountUI()
            defer { self.checkingPublic = false }
            do {
            if let id = self.slots.homeProjectID, let share = self.slots.shares[id],
               let home = self.catalog.projects.first(where: { $0.id == id }), let host = self.publicHost {
                try await self.waitForPublic(guideURL, marker: share.marker,
                                             expectedRedirect: share.url(host: host, project: home))
            } else {
                try await self.waitForPublic(guideURL, marker: self.guideMarker, onboarding: true)
            }
            } catch {
                self.publicVerificationIssue = safeMessage(error.localizedDescription)
                throw error
            }
            for project in self.catalog.projects {
                guard let share = self.slots.shares[project.id], let host = self.publicHost else { continue }
                do {
                    try await self.waitForPublic(share.url(host: host, project: project), marker: share.marker)
                    self.diagnostics[project.id] = nil
                    self.slots.shares[project.id]?.verified = true
                } catch is CancellationError {
                    self.slots.shares[project.id]?.verified = false
                    throw CancellationError()
                } catch {
                    self.diagnostics[project.id] = verificationErrorMessage(error)
                    self.slots.shares[project.id]?.verified = false
                    if error is AccountIdentityError || error is AccountNotReadyError { throw error }
                }
            }
            let failed = self.slots.shares.keys.contains { self.diagnostics[$0] != nil }
            self.entryVerified = !failed
            self.publicVerificationIssue = failed ? "部分项目尚未通过公网复核。请查看项目提示，处理后重新校验。" : nil
            self.operationNeedsRetry = failed
            self.notice = failed ? "检查已完成，请查看项目状态。" : "公网入口检查通过。"
        }
    }

    @objc func openPublicHome() {
        if let guideURL { NSWorkspace.shared.open(guideURL) }
    }

    @objc func selectPublicHome(_ sender: NSButton) {
        guard let project = project(sender), let host = publicHost, let profile = verifiedProfileID,
              let guideURL, let share = slots.shares[project.id] else { return }
        notice = "正在验证「\(project.name)」的公网首页入口…"
        run {
            try await self.requireAccount(profile)
            var candidate = self.slots
            try candidate.selectHome(project.id)
            var catalog = self.catalog
            catalog.rememberRoot(443, project: project.id, profile: profile, host: host)
            do {
                try await self.reload(active: self.active, shares: candidate, roots: self.roots)
                try await self.waitForPublic(guideURL, marker: share.marker,
                                             expectedRedirect: share.url(host: host, project: project))
                try await self.requireAccount(profile)
                try self.saveCatalog(catalog)
            } catch {
                do { try await self.reload(active: self.active, shares: self.slots, roots: self.roots) }
                catch { self.stopServices() }
                throw error
            }
            self.slots = candidate
            self.notice = "公网首页已转向「\(project.name)」。其他项目链接保持不变。"
        }
    }

    @objc func stopAll() {
        stopServices()
        accountMessage = "演示已暂停，账号授权保留。重新启动时可继续使用或切换账号。"
        notice = accountMessage
        refreshDashboard()
    }

    @objc func startAgain() {
        showOnboarding()
        startSession()
    }

    @objc func changeOpenPreference(_ sender: NSButton) {
        defaults.set(sender.state == .on, forKey: "openGuideWhenReady")
    }
}
