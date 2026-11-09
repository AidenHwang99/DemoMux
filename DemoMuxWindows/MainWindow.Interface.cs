using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
namespace DemoMux;

internal sealed partial class MainWindow
{
    private TextBlock? noticeLabel, publicStatusLabel, verificationStepLabel, accountProgressLabel;
    private static TextBlock Label(string value, double size = 12) { var text = Text(value, size); text.Foreground = Muted; return text; }
    private static TextBlock Accent(string value, double size = 12) { var text = Text(value, size); text.Foreground = Theme.Link; return text; }
    private static TextBlock Heading(string value, double size = 17) { var text = Text(value, size); text.FontWeight = FontWeights.SemiBold; return text; }
    private static TextBlock Glyph(string glyph, double size = 18) => new() { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, Foreground = Theme.Link, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private static Grid Split(UIElement left, UIElement right, double gap = 16)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        ((FrameworkElement)left).Margin = new Thickness(0, 0, gap, 0); grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    private static UIElement Section(string number, string title, string count)
    {
        var heading = Text(title, 18); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0);
        var index = Accent(number); index.FontFamily = new FontFamily("Cascadia Mono, Consolas"); index.Margin = new Thickness(0, 6, 10, 0);
        var section = Split(Row(index, heading), Label(count, 11)); section.Margin = new Thickness(0, 9, 0, 3); return section;
    }
    private void Render()
    {
        var offset = scroller.VerticalOffset;
        var focus = Keyboard.FocusedElement as FrameworkElement;
        var focusId = focus is null ? "" : AutomationProperties.GetAutomationId(focus);
        body.Children.Clear(); footer.Children.Clear(); dropArea = null;
        var eyebrow = Text(dashboard ? "本机项目  ·  公网演示" : "DEMOMUX  /  设置向导", 11); eyebrow.Foreground = Theme.Accent; eyebrow.FontWeight = FontWeights.SemiBold;
        if (isolatedTest) eyebrow.Text = "隔离交互测试 · 不读取真实账号，不连接网络";
        var title = Text(dashboard ? "DemoMux" : "准备公网演示", dashboard ? 36 : 28); title.FontWeight = FontWeights.Bold; title.Margin = new Thickness(0, 0, 0, 6);
        if (isolatedTest) title.Text = dashboard ? "布局交互测试" : "重试交互测试";
        var verified = ready && gateway.Slots.Shares.Values.All(share => share.Verified);
        var statusText = publicRetryActive && (retryWaiting || retryAttempt > 1) ? "正在自动重试" : verifyingPublic ? "正在检测公网" : verified ? "入口已验证" : !account.Running && dashboard ? "演示已停止" : "公网待复核";
        publicStatusLabel = Accent($"{statusText}   ·   公网发布 {gateway.Slots.Shares.Count} / 3", 12);
        var status = new Border { Child = publicStatusLabel, Padding = new Thickness(13, 5, 13, 5), CornerRadius = new CornerRadius(18), BorderBrush = Brush("#663047"), BorderThickness = new Thickness(1), Background = Brush("#34202a"), VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(Split(Column(eyebrow, title), status));
        RenderNotice();
        if (!dashboard) body.Children.Add(SetupCard());
        body.Children.Add(IdentityCard());
        if (accountPanel?.IsVisible == true) RenderAccountManager();
        RenderFooter();
        if (dashboard)
        {
            var quick = new ComboBox { MinWidth = 188, Margin = new Thickness(0, 3, 0, 3), IsEnabled = operation is null && sessionProjects.Count > 0 };
            AutomationProperties.SetName(quick, "本次会话 · 快速预览");
            quick.Items.Add(new ComboBoxItem { Content = "本次会话 · 快速预览" });
            foreach (var project in catalog.Projects.Where(p => sessionProjects.Contains(p.Id))) quick.Items.Add(new ComboBoxItem { Content = project.Name, Tag = project.Id });
            quick.SelectedIndex = 0;
            quick.SelectionChanged += (_, _) => { if (quick.SelectedItem is ComboBoxItem { Tag: Guid id } && catalog.Projects.FirstOrDefault(p => p.Id == id) is { } project) Run("正在准备预览…", async t => { if (!gateway.Active.ContainsKey(project.Id)) await StartProjectAsync(project, t); Open(LocalUrl(project)); }); };
            var add = Button("添加项目", () => {}, primary:true);
            var addMenu = ProjectMenu(add,false);
            foreach (var choice in new (string Title, Action Click)[] { ("添加 HTML 文件",AddHtmlDialog), ("导入项目文件夹",AddFolder), ("连接本机端口",AddPort) }) {
                var item = new MenuItem { Header = choice.Title }; item.Click += (_,_) => choice.Click(); addMenu.Items.Add(item);
            }
            add.ToolTip = "HTML、项目文件夹或已启动的 HTTP 服务";
            var toolbar = Split(Row(add), quick, 8); toolbar.Margin = new Thickness(0, 8, 0, 3); body.Children.Add(toolbar);
            dropArea = Card(Column(Accent("拖入 HTML 文件", 14), Label("或点击选择文件 · 包含图片、CSS 或 JavaScript 时，请导入项目文件夹", 12)));
            dropArea.Background = Brush("#22181e"); dropArea.Padding = new Thickness(14, 9, 14, 9); dropArea.Cursor = Cursors.Hand; dropArea.Focusable = true; dropArea.IsEnabled = operation is null;
            AutomationProperties.SetName(dropArea, "拖入 HTML 文件，或点击选择");
            dropArea.MouseLeftButtonUp += (_, _) => { if (operation is null) AddHtmlDialog(); };
            dropArea.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space && operation is null) { AddHtmlDialog(); e.Handled = true; } };
            dropArea.GotKeyboardFocus += (_, _) => { if (dropArea is not null) dropArea.BorderBrush = Theme.Link; };
            dropArea.LostKeyboardFocus += (_, _) => { if (dropArea is not null) dropArea.BorderBrush = Theme.Border; };
            body.Children.Add(dropArea);
            body.Children.Add(Section("01", "正在分享", $"{gateway.Slots.Shares.Count} / 3"));
            if (gateway.Slots.Shares.Count == 0) body.Children.Add(Label("暂无公网项目。添加项目后，可从列表发布。"));
            foreach (var project in catalog.Projects.Where(p => gateway.Slots.Shares.ContainsKey(p.Id))) body.Children.Add(ProjectCard(project, true));
            var remaining = catalog.Projects.Where(p => !gateway.Slots.Shares.ContainsKey(p.Id)).ToArray();
            body.Children.Add(Section("02", "项目列表", $"{remaining.Length} 个项目"));
            if (catalog.Projects.Count == 0) { var empty = Card(Column(Text("你的下一个演示，从这里开始", 15), Label("添加一个 HTML、项目文件夹或本机端口，先预览，再分享。"))); empty.Background = Brushes.Transparent; body.Children.Add(empty); }
            else if (remaining.Length == 0) body.Children.Add(Label("全部项目正在分享。"));
            foreach (var project in remaining) body.Children.Add(ProjectCard(project, false));
        }
        Dispatcher.InvokeAsync(() => {
            scroller.ScrollToVerticalOffset(offset);
            if (focusId.Length > 0 && IsActive && OwnedWindows.Count == 0) FindControl(body, focusId)?.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private static FrameworkElement? FindControl(DependencyObject root, string id)
    {
        if (root is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id && element.IsEnabled) return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindControl(VisualTreeHelper.GetChild(root, i), id) is { } found) return found;
        return null;
    }
    private void RenderNotice()
    {
        var text = Label(progress, 12); noticeLabel = text; if (notice == Notice.Error) text.Foreground = Theme.Link;
        var actions = Row();
        if (authUrl is { } authorization) actions.Children.Add(Button("打开授权网页", () => Open(authorization), always: true, primary: true));
        if (operation is not null) actions.Children.Add(Button(publicRetryActive ? "停止重试" : "取消", () => operation?.Cancel(), always: true));
        else if ((notice == Notice.Error || notice == Notice.Info) && (retryAction is not null || retryLocal is not null)) actions.Children.Add(Button(retryAttempt > 0 ? "重新校验" : "重试", Retry, primary: true));
        var block = Column(Split(text, actions)); block.Margin = new Thickness(0, 0, 0, 7);
        if (operation is not null) block.Children.Add(new VerificationActivity());
        body.Children.Add(block);
    }
    private UIElement SetupCard()
    {
        var content = Column(Heading("让本机项目，成为一个可分享的链接"), Label("首次准备通常只需几分钟。账号授权在浏览器完成。"));
        var steps = new[] { ("准备预览引擎", "下载并校验 Caddy"), ("准备连接组件", "下载并校验 Tailscale"), ("连接你的账号", "使用 DemoMux 私有身份"), ("启用公网访问", "确认 Funnel 权限"), ("验证公网入口", "检查 DNS、HTTPS 和当前页面") };
        for (var i = 0; i < steps.Length; i++)
        {
            bool done = !isolatedTest && i + 1 < setupStep, current = i + 1 == setupStep;
            var number = done ? Glyph("\uE73E", 12) : Accent((i + 1).ToString("00"), 12); number.Width = 28; number.Margin = new Thickness(0, 0, 8, 0);
            var caption = Text(steps[i].Item1, 13); caption.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
            if (!current && !done) caption.Foreground = Muted;
            var detail = Label(done ? "已完成" : current && publicRetryActive ? retryWaiting ? "等待自动重试" : $"第 {retryAttempt} 次校验" : current && notice == Notice.Info && retryAttempt > 0 ? "已停止校验" : current && notice == Notice.Error ? "需要处理" : steps[i].Item2, 11);
            if (i == 4) verificationStepLabel = detail;
            if (isolatedTest && i < 4) detail.Text = "未执行 · 隔离测试";
            if (!isolatedTest && i == 2 && done && (account.Current is null || account.Backend != "Running")) detail.Text = "账号需重新连接";
            var row = Split(Row(number, caption), detail); row.Margin = new Thickness(0, 7, 0, 2); content.Children.Add(row);
        }
        return Card(content);
    }
    private UIElement IdentityCard(bool showManage = true)
    {
        var avatar = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Brush("#38212c"), Child = Glyph("\uE77B", 22), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var identity = new DockPanel(); DockPanel.SetDock(avatar, Dock.Left); identity.Children.Add(avatar);
        var label = Text("TAILSCALE  /  演示身份", 10); label.Foreground = Theme.Accent; label.FontWeight = FontWeights.SemiBold;
        var name = Text(account.Current is null ? "连接你的 Tailscale 账号" : account.DisplayName, 13); name.FontWeight = FontWeights.SemiBold;
        var info = Label(account.Current is null ? "首次连接后，在此管理账号与所属网络。" : $"{account.Current.Account}  ·  网络：{account.Current.Tailnet}", 12);
        var connection = Label(account.Running && account.Backend == "Running" ? "当前账号 · 已连接" : "上次使用 · 未连接", 12);
        foreach (var line in new[] { label, name, info, connection }) line.Margin = new Thickness(0);
        label.Margin = new Thickness(0, 0, 0, 4); identity.Children.Add(Column(label, name, info, connection));
        var manage = Button("管理账号", OpenAccountManager, always: true); manage.VerticalAlignment = VerticalAlignment.Center; manage.Margin = new Thickness(0);
        return Card(showManage ? Split(identity, manage) : identity);
    }
    private void RenderFooter()
    {
        var actions = Row(Button("使用说明", OpenGuide, always:true));
        if (dashboard) actions.Children.Add(Button("检测公网连接", () => Run("正在检测公网…", RecheckAsync,true), ready));
        var preference = new CheckBox { Content = "就绪后打开首页", IsChecked = catalog.OpenHome, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,14,0) };
        preference.Click += (_,_) => { catalog.OpenHome = preference.IsChecked == true; catalog.Save(); };
        var sessionAction = ready || account.Running || gateway.Running
            ? Button("结束演示", () => Run("正在停止…", t => { account.Dispose(); gateway.Dispose(); ready = false; return Task.CompletedTask; }))
            : Button("启动演示", () => Run("准备公网演示", StartupAsync,true), primary:true);
        sessionAction.Margin = new Thickness(0);
        var right = Row(preference,sessionAction); right.HorizontalAlignment = HorizontalAlignment.Right;
        UIElement content = !dashboard ? actions : ActualWidth >= 800 ? Split(actions,right,12) : Column(actions,right);
        var card = Card(content); card.Background = Theme.Surface; card.CornerRadius = new CornerRadius(12); card.Padding = new Thickness(12,8,12,8); card.Margin = new Thickness(0); footer.Children.Add(card);
    }
    private UIElement ProjectCard(Project project, bool sharing)
    {
        bool running = gateway.Active.ContainsKey(project.Id);
        var name = Text(project.Name, 15); name.FontWeight = FontWeights.SemiBold; name.TextTrimming = TextTrimming.CharacterEllipsis; name.TextWrapping = TextWrapping.NoWrap; name.ToolTip = project.Name;
        var description = Label(project.Description + (sharing ? "" : running ? "  ·  预览中" : "  ·  已停止"), 12); description.ToolTip = project.Kind == "port" ? project.Description : project.Source;
        var content = Column(name, description);
        var preview = Button("本机预览", () => Run("正在准备预览…", async t => { if (!gateway.Active.ContainsKey(project.Id)) await StartProjectAsync(project, t); Open(LocalUrl(project)); }));
        AutomationProperties.SetAutomationId(preview, project.Id + "-preview");
        var more = Button("更多操作", () => { }); more.Content = Glyph("\uE712", 15); more.Width = 34; more.Height = 34; more.Padding = new Thickness(0);
        AutomationProperties.SetAutomationId(more, project.Id + "-more"); more.ToolTip = "项目操作";
        var menu = ProjectMenu(more);
        void Item(string caption, Action action, bool enabled = true, bool destructive = false) {
            var item = new MenuItem { Header = caption, IsEnabled = enabled && operation is null };
            if (destructive) item.Foreground = Theme.Link;
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        if (sharing) {
            var share = gateway.Slots.Shares[project.Id];
            Item("取消发布", () => Run("正在取消发布…", t => UnpublishAsync(project,t)), !share.Pending, true);
            menu.Items.Add(new Separator { Style = (Style)Resources[typeof(Separator)] });
        }
        Item(running ? "重新启动项目" : "启动项目", () => Run("正在启动项目…", t => StartProjectAsync(project, t)));
        Item("修改名称", () => { var value = Prompt("修改显示名称", "显示名称不会改变已经分享的链接。", project.Name, text => text.Length == 0 ? "请输入项目名称。" : null); if (value is not null) { project.Name = value; catalog.Save(); Render(); } });
        if (!sharing) Item("重新定位", () => Relocate(project));
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        if (sharing)
        {
            var share = gateway.Slots.Shares[project.Id]; var url = share.Url(account.Host, project);
            var badges = Row();
            badges.Children.Add(Accent(share.Pending ? "正在验证" : share.Verified ? "已发布 · 已验证" : "已发布 · 待复核", 11));
            if (gateway.Slots.Home == project.Id) { var home = Accent("公网首页", 10); home.Margin = new Thickness(10, 2, 0, 2); badges.Children.Add(home); }
            content.Children.Add(badges);
            var link = Label(url.AbsoluteUri, 11); link.TextTrimming = TextTrimming.CharacterEllipsis; link.TextWrapping = TextWrapping.NoWrap; link.ToolTip = url.AbsoluteUri; content.Children.Add(link);
            if (gateway.Slots.Home != project.Id) Item("设为公网首页", () => Run("正在切换首页…", async t => { gateway.Slots.Home = project.Id; await gateway.ApplyAsync(t); }), !share.Pending);
            controls.Children.Add(Button("打开网页", () => Open(url), !share.Pending, true));
            controls.Children.Add(Button("复制链接", () => Copy(url.AbsoluteUri), !share.Pending, true, true));
        }
        else { controls.Children.Add(preview); controls.Children.Add(Button("发布", () => PublishDialog(project), ready && gateway.Slots.Shares.Count < 3, primary:true)); }
        if (running) { menu.Items.Add(new Separator { Style = (Style)Resources[typeof(Separator)] }); Item("停止项目", () => Run("正在停止项目…", t => StopProjectAsync(project,t)), true, true); }
        controls.Children.Add(more);
        foreach (Button action in controls.Children) { action.MinHeight = 34; action.Margin = new Thickness(6,0,0,0); }
        more.Padding = new Thickness(0);
        var card = Card(Split(content, controls, 18)); card.Padding = new Thickness(16); return card;
    }
    private ContextMenu ProjectMenu(Button anchor, bool alignRight = true)
    {
        var menu = new ContextMenu { MaxHeight = Math.Max(180, Math.Min(360,SystemParameters.WorkArea.Height-80)), PlacementTarget = anchor, Placement = PlacementMode.Custom };
        menu.Resources.MergedDictionaries.Add(Resources);
        menu.CustomPopupPlacementCallback = (popup, target, offset) => [
            new CustomPopupPlacement(new Point((alignRight ? target.Width-popup.Width : 0),target.Height+6),PopupPrimaryAxis.Vertical),
            new CustomPopupPlacement(new Point((alignRight ? target.Width-popup.Width : 0),-popup.Height-6),PopupPrimaryAxis.Vertical)
        ];
        anchor.ContextMenu = menu; anchor.Click += (_, _) => menu.IsOpen = true;
        return menu;
    }
    private void OpenAccountManager()
    {
        if (accountPanel is null) { accountPanel = Dialog("账号与公网"); accountPanel.Width = 560; accountPanel.Closed += (_, _) => accountPanel = null; }
        RenderAccountManager(); accountPanel.Show(); accountPanel.Activate();
    }
    private void RenderAccountManager()
    {
        if (accountPanel is null) return;
        bool connected = account.Running && account.Current is not null && account.Backend == "Running";
        var summary = connected ? ready ? "账号已连接，公网入口已验证。" : "账号已连接；公网入口尚未完成验证。" : account.Current is null ? "连接 Tailscale 账号后，即可继续设置公网演示。" : "账号已保留，当前连接已暂停。恢复后无需重新添加账号。";
        var content = Column(Accent("DEMOMUX  /  TAILSCALE", 10), Heading("账号与公网", 26), Label(summary), IdentityCard(false));
        if (!connected)
        {
            content.Children.Add(Card(Column(Heading(account.Current is null ? "连接你的账号" : "恢复账号连接", 15),
                Label("账号连接与公网校验是两个步骤。连接成功后，会继续检查公网入口。"),
                Row(Button(account.Current is null ? "连接账号并继续" : "恢复连接并继续", () => Run("准备公网演示", ResumeAccountAsync, true), primary: true)))));
        }
        else
        {
            content.Children.Add(Card(Column(Heading("账号操作", 15), Row(
                Button("刷新账号状态", () => Run("正在同步账号状态…", async t => { var expected = account.Current?.Id; await account.RefreshAsync(t); if (expected != account.Current?.Id) throw new AccountIdentityException(); if (account.Backend != "Running" || account.Current is null) { account.StopFunnels(); gateway.Slots.Clear(); ready = false; await gateway.ApplyAsync(t); } })),
                Button("添加其他账号", () => ChangeAccount("添加账号", t => account.LoginAsync(true, false, t)))))));
            content.Children.Add(Card(Column(Heading("会话与授权", 15), Label("这些操作会先撤下公网分享。项目列表和源文件会保留。"), Row(
                Button("暂停连接", () => ChangeAccount("暂停连接", async t => { await account.ExecuteAsync(t, "down"); }, false)),
                Button("重新授权", () => ChangeAccount("重新授权", t => account.LoginAsync(false, true, t))),
                Button("退出账号", () => ChangeAccount("退出账号", async t => { await account.ExecuteAsync(t, "logout"); }, false))))));
        }
        accountProgressLabel = null;
        if (operation is not null)
        {
            accountProgressLabel = Label(progress);
            content.Children.Add(Card(Column(accountProgressLabel, Button(publicRetryActive ? "停止重试" : "取消当前操作", () => operation?.Cancel(), always: true))));
        }
        content.Children.Add(Label("这里只管理 DemoMux 的私有账号；不更改系统 Tailscale。", 11));
        var profiles = account.Profiles.Where(p => !p.Selected).ToArray();
        if (profiles.Length > 0) { content.Children.Add(Text("其他账号", 15)); foreach (var profile in profiles) content.Children.Add(Button("切换到 " + profile.Account, () => ChangeAccount("切换账号", async t => { await account.ExecuteAsync(t, "switch", profile.Id); await account.LoginAsync(false, false, t); }))); }
        if (authUrl is { } authorization) content.Children.Add(Button("打开授权网页", () => Open(authorization), always: true, primary: true));

        var close = Button("完成", () => accountPanel?.Close(), always: true); close.HorizontalAlignment = HorizontalAlignment.Right; close.IsCancel = true; content.Children.Add(close);
        content.Margin = new Thickness(24); accountPanel.Content = new ScrollViewer { Focusable = false, Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
