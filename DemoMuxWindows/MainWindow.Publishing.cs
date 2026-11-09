using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DemoMux;

internal sealed partial class MainWindow
{
    private sealed record PublishDraft(string Slug, bool Root);
    private sealed record PublishFailure(Guid ProjectId, bool Root, int? StatusCode);
    private readonly Dictionary<Guid, PublishDraft> publishDrafts = [];
    private PublishFailure? publishFailure;
    internal Action<string> CopyPublicationPath { get; set; } = Clipboard.SetText;

    private void PublishDialog(Project project, bool? useRoot = null)
    {
        if (gateway.Slots.Shares.Count >= 3) { progress = "三个公网名额已用完，请先取消一项。"; Render(); return; }
        publishDrafts.TryGetValue(project.Id, out var draft);
        bool portProject = project.Kind == "port", root = useRoot ?? draft?.Root ?? portProject;
        var dialog = Dialog("发布项目");
        dialog.Width = 640; dialog.MinWidth = 480; dialog.MinHeight = Math.Min(400, dialog.MaxHeight);
        dialog.Height = Math.Min(720, dialog.MaxHeight); dialog.SizeToContent = SizeToContent.Manual; dialog.ResizeMode = ResizeMode.CanResize;
        using var checks = new PublicationPreflightSession();
        bool closed = false, submitting = false; long revision = 0;
        var slug = new TextBox { Text = project.LockedSlug ? project.Slug : draft?.Slug ?? project.Slug, IsReadOnly = project.LockedSlug };
        System.Windows.Automation.AutomationProperties.SetName(slug, "项目标识");
        var intro = Label(""); var validation = Accent(""); var checkStatus = Label(""); var copyFeedback = Label("");
        var link = new TextBox { IsReadOnly = true, FontSize = 12 };
        System.Windows.Automation.AutomationProperties.SetName(link, "预计公网链接");
        var method = new ComboBox { ItemsSource = new[] { "按项目名发布", "使用独立入口" }, SelectedIndex = root ? 1 : 0, Visibility = portProject ? Visibility.Collapsed : Visibility.Visible };
        System.Windows.Automation.AutomationProperties.SetName(method, "发布方式");
        var ok = Button("发布并验证", () => { }, always: true, primary: true); ok.IsDefault = true;
        var cancel = Button("取消", () => dialog.DialogResult = false, always: true); cancel.IsCancel = true;
        var advanced = Button("高级：按项目路径发布", () => { }, always: true);
        var recommended = Card(Column(Heading("独立入口（推荐）", 17), Label("适合直接访问本机服务，通常不需要修改项目。"), advanced));
        var pathRequirement = Label("");
        var back = Button("返回推荐方式", () => { }, always: true);
        var advancedIntro = Card(Column(Heading("按项目路径发布（高级）", 17), pathRequirement, back));
        var copy = Button("复制项目配置路径", () =>
        {
            try { CopyPublicationPath($"/p/{slug.Text}/"); copyFeedback.Text = "项目配置路径已复制，不会自动修改项目配置。"; }
            catch (System.Runtime.InteropServices.ExternalException) { copyFeedback.Text = "剪贴板正在使用，请重试。"; }
        }, always: true);
        var copyGroup = Column(copy, Label("仅复制路径，不会自动修改项目配置。", 11), copyFeedback);
        var slugGroup = Column(Label("项目标识"), slug, Label(project.LockedSlug ? "首次发布后标识已固定；修改显示名称不会改变链接。" : "使用小写字母、数字和短横线，最多 48 个字符。", 11));
        var retry = Button("重新检测", () => { }, always: true);
        var switchRoot = Button("改用独立入口", () => { }, always: true);
        var preflight = Card(Column(Heading("发布前检查", 15), checkStatus, Row(retry, switchRoot)));
        var body = Column(Accent("DEMOMUX", 10), Heading("发布项目", 25), Heading(project.Name, 17), method, recommended, advancedIntro,
            Card(Column(Label("预计公网链接", 11), link, intro)), slugGroup, copyGroup, validation, preflight,
            Label("任何拿到链接的人都可以访问。最多同时分享 3 个项目。", 11));
        body.Margin = new Thickness(24);
        var footer = new Border { Child = Row(cancel, ok), Background = Theme.Surface, Padding = new Thickness(16, 10, 16, 10) };
        var layout = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = body, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        dialog.Content = layout;
        foreach (var (control, id) in new (FrameworkElement, string)[] { (advanced, "publish-advanced"), (back, "publish-back"),
            (switchRoot, "publish-recommended"), (slug, "publish-slug"), (ok, "publish-confirm"), (cancel, "publish-cancel"),
            (retry, "publish-recheck"), (checkStatus, "publish-preflight"), (copy, "publish-copy-path"), (copyFeedback, "publish-copy-feedback"), (method, "publish-method") })
            System.Windows.Automation.AutomationProperties.SetAutomationId(control, id);

        PublicationTarget Target() => new(project.Id, project.Port, root, slug.Text);
        bool Valid() => root || Catalog.ValidSlug(slug.Text) && !catalog.Projects.Any(p => p.Id != project.Id && p.Slug == slug.Text);
        void Availability()
        {
            bool slotAvailable = gateway.Slots.Shares.Count < 3 && (!root || gateway.Slots.NextRoot is not null);
            ok.IsEnabled = !submitting && Valid() && slotAvailable && (!portProject || checks.Current?.FreshFor(Target(), DateTimeOffset.UtcNow) == true);
            retry.IsEnabled = !submitting && Valid(); copy.IsEnabled = !submitting && Catalog.ValidSlug(slug.Text);
            switchRoot.Visibility = !root && checks.Current is { Status: not PreflightStatus.Accessible } ? Visibility.Visible : Visibility.Collapsed;
            if (!slotAvailable) validation.Text = "没有可用公网名额，请先取消一项发布。";
        }
        void Update()
        {
            recommended.Visibility = portProject && root ? Visibility.Visible : Visibility.Collapsed;
            advancedIntro.Visibility = portProject && !root ? Visibility.Visible : Visibility.Collapsed;
            preflight.Visibility = portProject ? Visibility.Visible : Visibility.Collapsed;
            slugGroup.Visibility = root ? Visibility.Collapsed : Visibility.Visible;
            copyGroup.Visibility = portProject && !root ? Visibility.Visible : Visibility.Collapsed;
            pathRequirement.Text = $"项目必须支持 /p/{slug.Text}/ 这样的子路径。DemoMux 不会自动配置你的项目。";
            var port = gateway.Slots.NextRoot; var key = $"{account.Current?.Id}|{account.Host}|{port}";
            var reused = catalog.RootOwners.TryGetValue(key, out var previous) && previous != project.Id;
            link.Text = root ? port is null ? "暂无可用独立入口" : $"https://{account.Host}{(port == 443 ? "" : ":" + port)}/" : $"https://{account.Host}/p/{slug.Text}/";
            intro.Text = root ? "适用于从根路径加载资源或 API 的项目。" + (reused ? " 之前分享的同一地址将指向这个新项目。" : "")
                : portProject ? "此方式保留项目路径并转发到本机服务，需要先检测该路径是否可访问。" : "适用于使用相对路径加载资源的 HTML。";
            validation.Text = Valid() ? "" : "项目标识无效或已被使用。";
            Availability();
        }
        void Switch(bool value)
        {
            if (submitting) return;
            root = value; revision++; checks.Invalidate(); copyFeedback.Text = ""; Update();
            if (portProject && Valid()) _ = RefreshAsync(false);
        }
        async Task RefreshAsync(bool debounce)
        {
            long version = ++revision;
            if (!portProject || !Valid()) { checks.Invalidate(); checkStatus.Text = "请先填写有效的项目标识。"; Availability(); return; }
            var target = Target();
            checkStatus.Text = "正在检查本机路径，尚未创建公网分享…";
            var pending = checks.CheckAsync(target, debounce); Availability();
            try
            {
                var result = await pending;
                if (closed || version != revision || target != Target() || result is null) return;
                checkStatus.Text = result.Message;
                checkStatus.Foreground = result.Status == PreflightStatus.Accessible ? Muted : Theme.Link;
                Availability();
            }
            catch (Exception) when (!closed)
            {
                if (version == revision) { checks.Invalidate(); checkStatus.Text = "无法完成本机检测，请重新检测。"; Availability(); }
            }
            catch (Exception) when (closed) { }
        }
        advanced.Click += (_, _) => Switch(false); back.Click += (_, _) => Switch(true);
        switchRoot.Click += (_, _) => Switch(true); retry.Click += (_, _) => _ = RefreshAsync(false);
        method.SelectionChanged += (_, _) => Switch(method.SelectedIndex == 1);
        slug.TextChanged += (_, _) => { if (closed) return; checks.Invalidate(); revision++; copyFeedback.Text = ""; Update(); if (portProject) _ = RefreshAsync(true); };
        ok.Click += async (_, _) =>
        {
            if (submitting || !Valid()) return;
            submitting = true; body.IsEnabled = false; Availability();
            try
            {
                if (portProject)
                {
                    var target = Target();
                    if (checks.Current?.FreshFor(target, DateTimeOffset.UtcNow) != true) checkStatus.Text = "检测结果已过期，正在重新检查本机路径…";
                    var result = await checks.EnsureFreshAsync(target);
                    if (closed) return;
                    if (result is null || !result.FreshFor(Target(), DateTimeOffset.UtcNow)) { checkStatus.Text = result?.Message ?? "检测已取消，请重新检测。"; return; }
                }
                if (gateway.Slots.Shares.Count >= 3 || root && gateway.Slots.NextRoot is null) { validation.Text = "没有可用公网名额，请先取消一项发布。"; return; }
                dialog.DialogResult = true;
            }
            catch (Exception) when (!closed) { checkStatus.Text = "无法完成本机检测，请重新检测。"; }
            catch (Exception) when (closed) { }
            finally { if (!closed) { submitting = false; body.IsEnabled = true; Availability(); } }
        };
        dialog.Closed += (_, _) => { closed = true; revision++; checks.Dispose(); publishDrafts[project.Id] = new(slug.Text, root); };
        Update(); if (portProject) _ = RefreshAsync(false);
        if (dialog.ShowDialog() != true) return;
        if (!root) project.Slug = slug.Text;
        catalog.Save();
        Run("正在发布并验证…", token => PublishProjectAsync(project, root, token), true);
    }
    private async Task PublishProjectAsync(Project project, bool root, CancellationToken token)
    {
        var share = gateway.Slots.Reserve(project, root);
        Report("正在发布并验证…");
        try
        {
            var expected = account.Current?.Id; await account.RefreshAsync(token);
            if (expected != account.Current?.Id || account.Backend != "Running") throw new AccountIdentityException();
            if (!gateway.Active.ContainsKey(project.Id)) await StartProjectAsync(project, token);
            await gateway.ApplyAsync(token);
            if (share.RootPort is { } port && port != 443) await account.FunnelAsync(port, gateway.RootListeners[port], token);
            await VerifyPublicAsync(share.Url(account.Host, project), share.Marker, token);
            await account.RefreshAsync(token);
            if (expected != account.Current?.Id || account.Backend != "Running") throw new AccountIdentityException();
            share.Verified = true; share.Pending = false; gateway.Slots.Home ??= project.Id; project.LockedSlug = true;
            if (share.RootPort is { } rootPort) catalog.RootOwners[$"{account.Current?.Id}|{account.Host}|{rootPort}"] = project.Id;
            catalog.Save(); await gateway.ApplyAsync(token); publishDrafts.Remove(project.Id);
        }
        catch (Exception error)
        {
            if (share.RootPort is { } port && port != 443) account.StopFunnel(port);
            gateway.Slots.Release(project.Id);
            publishFailure = new(project.Id, root, (error as ProjectHttpException)?.StatusCode);
            retryAction = null; retryLocal = () => PublishDialog(project);
            await gateway.ApplyAsync(CancellationToken.None);
            if (project.Kind == "port" && !root && error is ProjectHttpException { StatusCode: 404 })
                throw new IOException($"发布未成功：项目路径 {project.PublicPath} 返回 HTTP 404。项目可能未配置此路径，可改用独立入口后重新确认发布。", error);
            throw;
        }
    }
}
