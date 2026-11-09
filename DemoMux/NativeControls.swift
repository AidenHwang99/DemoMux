import AppKit
import QuartzCore

@MainActor
enum SharedIcon {
    private static var cache: [String: NSImage] = [:]
    static var directory: URL {
        let bundled = Bundle.main.resourceURL!.appendingPathComponent("Icons/lucide")
        if fileManager.fileExists(atPath: bundled.path) { return bundled }
        return URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("Shared/Icons/lucide")
    }
    static func image(_ name: String) -> NSImage {
        if let image = cache[name] { return image }
        guard let image = NSImage(contentsOf: directory.appendingPathComponent(name + ".svg")) else {
            assertionFailure("Missing shared SVG: \(name)")
            return NSImage(size: NSSize(width: 18, height: 18))
        }
        image.size = NSSize(width: 18, height: 18)
        image.isTemplate = true
        cache[name] = image
        return image
    }
}

@MainActor
final class VerificationActivityView: NSView {
    private let track = CALayer()
    private let beam = CAGradientLayer()
    private var lastWidth: CGFloat = -1
    var active = false { didSet { if active != oldValue { updateMotion() } } }
    private var observer: NSObjectProtocol?
    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.masksToBounds = true
        track.backgroundColor = GuideTheme.border.cgColor
        beam.colors = [NSColor.clear.cgColor, GuideTheme.accent.withAlphaComponent(0.35).cgColor,
                       GuideTheme.link.cgColor, GuideTheme.accent.withAlphaComponent(0.35).cgColor, NSColor.clear.cgColor]
        beam.locations = [0, 0.3, 0.5, 0.7, 1]
        beam.startPoint = CGPoint(x: 0, y: 0.5); beam.endPoint = CGPoint(x: 1, y: 0.5)
        layer?.addSublayer(track); layer?.addSublayer(beam)
        heightAnchor.constraint(equalToConstant: 6).isActive = true
        setAccessibilityElement(false)
        observer = NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.accessibilityDisplayOptionsDidChangeNotification,
                                                                      object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.updateMotion() }
        }
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    deinit { if let observer { NSWorkspace.shared.notificationCenter.removeObserver(observer) } }
    override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); updateMotion() }
    override func layout() {
        super.layout()
        CATransaction.begin(); CATransaction.setDisableActions(true)
        track.frame = CGRect(x: 0, y: 2, width: bounds.width, height: 2)
        beam.bounds = CGRect(x: 0, y: 0, width: 160, height: 4)
        beam.position = CGPoint(x: bounds.midX, y: 3)
        CATransaction.commit()
        if lastWidth != bounds.width { lastWidth = bounds.width; updateMotion() }
    }
    private func updateMotion() {
        beam.removeAnimation(forKey: "verification-travel")
        beam.isHidden = !active
        guard active, window != nil, bounds.width > 0 else { return }
        guard !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else { return }
        let animation = CABasicAnimation(keyPath: "position.x")
        animation.fromValue = -80; animation.toValue = bounds.width + 80
        animation.duration = 2.4; animation.repeatCount = .infinity
        beam.add(animation, forKey: "verification-travel")
    }
    var animationRunning: Bool { beam.animation(forKey: "verification-travel") != nil }
}

struct ProjectMenuCommand {
    enum Action { case restart, rename, relocate, check, homepage, openHomepage }
    let id: UUID
    let action: Action
}

extension DemoMuxWindow {
    func iconButton(_ title: String, icon: String, action: Selector, id: UUID,
                    enabled: Bool = true, primary: Bool = false) -> NSButton {
        let control = button(title, action, id: id, primary: primary)
        control.image = SharedIcon.image(icon)
        control.imagePosition = .imageOnly
        control.imageScaling = .scaleProportionallyDown
        control.contentTintColor = primary ? GuideTheme.text : GuideTheme.link
        control.setAccessibilityLabel(title)
        control.setAccessibilityHelp(title)
        control.toolTip = title
        control.isEnabled = enabled && !busy
        control.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([control.widthAnchor.constraint(equalToConstant: 36),
                                     control.heightAnchor.constraint(equalToConstant: 36)])
        return control
    }

    @objc func showAddProjectMenu(_ sender: NSButton) {
        let menu = NSMenu(title: "添加项目")
        for (name, action, symbol) in [("添加 HTML 文件", #selector(addHTMLFiles), "doc.badge.plus"),
                                      ("导入项目文件夹", #selector(addFolder), "folder.badge.plus"),
                                      ("连接本机端口", #selector(addPort), "network")] {
            let item = NSMenuItem(title: name, action: action, keyEquivalent: "")
            item.target = self; item.isEnabled = !busy
            item.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)
            menu.addItem(item)
        }
        showMenu(menu, anchor: sender)
    }
    func showMenu(_ menu: NSMenu, anchor: NSButton) {
        menu.appearance = NSAppearance(named: .darkAqua)
        menu.autoenablesItems = false
        menu.popUp(positioning: nil, at: NSPoint(x: max(0, anchor.bounds.width - menu.size.width), y: -6), in: anchor)
    }
    @objc func showProjectMenu(_ sender: NSButton) {
        guard let project = project(sender) else { return }
        let running = active[project.id] != nil
        let share = slots.shares[project.id]
        let menu = NSMenu(title: "项目操作")
        func add(_ title: String, _ action: ProjectMenuCommand.Action, enabled: Bool = true) {
            let item = NSMenuItem(title: title, action: #selector(performProjectMenu), keyEquivalent: "")
            item.target = self; item.representedObject = ProjectMenuCommand(id: project.id, action: action)
            item.isEnabled = enabled && !busy; menu.addItem(item)
        }
        if running { add("重新启动项目", .restart, enabled: project.source.sourceExists && servicesAvailable) }
        add("修改显示名称", .rename)
        if share == nil { add(project.source.port == nil ? "重新定位源文件" : "更换本机端口", .relocate) }
        if share != nil {
            menu.addItem(.separator())
            add("重新校验此项目", .check, enabled: servicesAvailable)
            if slots.homeProjectID == project.id { add("打开公网首页", .openHomepage) }
            else {
                add("设为公网首页", .homepage, enabled: !slots.shares.contains { $0.key != project.id && $0.value.publication == .root(443) })
            }
        }
        showMenu(menu, anchor: sender)
    }
    @objc func performProjectMenu(_ sender: NSMenuItem) {
        guard let command = sender.representedObject as? ProjectMenuCommand else { return }
        let proxy = NSButton(); proxy.identifier = NSUserInterfaceItemIdentifier(command.id.uuidString)
        switch command.action {
        case .restart: startOrRestart(proxy)
        case .rename: renameProject(proxy)
        case .relocate: relocateProject(proxy)
        case .check: checkProjectLink(proxy)
        case .homepage: selectPublicHome(proxy)
        case .openHomepage: openPublicHome()
        }
    }
    @objc func toggleProjectLifecycle(_ sender: NSButton) {
        guard let project = project(sender) else { return }
        if active[project.id] != nil { stopProject(sender) } else { startOrRestart(sender) }
    }
    @objc func toggleProjectPublication(_ sender: NSButton) {
        guard let project = project(sender) else { return }
        if slots.shares[project.id] != nil { cancelPublic(sender) } else { requestPublication(sender) }
    }
}

@MainActor
final class ResponsiveFooter: NSStackView {
    private var narrow = false
    private var fullWidth: [NSLayoutConstraint] = []
    private var gapView: NSView?
    override init(frame: NSRect) { super.init(frame: frame) }
    required init?(coder: NSCoder) { super.init(coder: coder) }
    override func layout() {
        if gapView == nil, arrangedSubviews.count == 2 {
            let gap = NSView()
            gap.setContentHuggingPriority(.defaultLow, for: .horizontal)
            gapView = gap
            for group in arrangedSubviews { (group as? NSStackView)?.setHuggingPriority(.required, for: .horizontal) }
            insertArrangedSubview(gap, at: 1)
            detachesHiddenViews = true
        }
        let next = bounds.width < 780
        if next != narrow || fullWidth.isEmpty {
            narrow = next
            NSLayoutConstraint.deactivate(fullWidth)
            orientation = narrow ? .vertical : .horizontal
            alignment = narrow ? .leading : .centerY
            distribution = .fill
            spacing = narrow ? 8 : 18
            gapView?.isHidden = narrow
            fullWidth = arrangedSubviews.filter { $0 !== gapView }.map { $0.widthAnchor.constraint(equalTo: widthAnchor) }
            if narrow { NSLayoutConstraint.activate(fullWidth) }
        }
        super.layout()
    }
}

extension DemoMuxWindow: NSMenuItemValidation {
    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        switch menuItem.action {
        case #selector(openGuidePage): return true
        case #selector(cancelCurrentOperation): return busy
        case #selector(checkGuide): return dashboard && servicesAvailable && !busy
        case #selector(addHTMLFiles), #selector(addFolder), #selector(addPort): return dashboard && !busy
        default: return true
        }
    }
}
