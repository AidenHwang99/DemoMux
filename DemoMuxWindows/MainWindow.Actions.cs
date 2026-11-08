using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace DemoMux;
internal sealed partial class MainWindow
{
    private async Task StartupAsync(CancellationToken token)
    {
        ready = false;
        Report("01 / 05  正在准备并校验 Caddy…"); await caddyComponent.InstallAsync(token);
        if (!gateway.Running) await gateway.ApplyAsync(token);
        Report("02 / 05  正在准备并校验 Tailscale…"); await tailscaleComponent.InstallAsync(token); await account.StartAsync(token);
        Report("03 / 05  正在连接账号…"); await account.LoginAsync(false, false, token); authUrl = null; gateway.Host = account.Host;
        Report("04 / 05  正在启用 Funnel…"); await account.FunnelAsync(443, 8081, token); authUrl = null;
        Report("05 / 05  正在验证公网介绍页…"); await RecheckAsync(token);
        dashboard = true;
        if (catalog.OpenHome) Open(new Uri($"https://{account.Host}/"));
    }
    private async Task RecheckAsync(CancellationToken token)
    {
        var expected = account.Current?.Id; await account.RefreshAsync(token);
        if (expected != account.Current?.Id || account.Backend != "Running") { account.StopFunnels(); gateway.Slots.Clear(); ready = false; await gateway.ApplyAsync(token); throw new IOException("登录身份已改变或已断开，请重新启动演示。"); }
        if (gateway.Slots.Shares.Count == 0) { ready = false; await PublicVerifier.VerifyAsync(new Uri($"https://{account.Host}/"), gateway.Marker, token); ready = true; }
        else foreach (var share in gateway.Slots.Shares.Values) { share.Verified = false; await PublicVerifier.VerifyAsync(share.Url(account.Host, catalog.Projects.First(p => p.Id == share.ProjectId)), share.Marker, token); share.Verified = true; }
    }
    private async Task StartProjectAsync(Project project, CancellationToken token)
    {
        var next = await PreparedContent.PrepareAsync(project, session, token);
        gateway.Active.TryGetValue(project.Id, out var previous); gateway.Active[project.Id] = next;
        try { await gateway.ApplyAsync(token); previous?.Dispose(); if (project.Kind == "port") sessionProjects.Add(project.Id); }
        catch { if (previous is null) gateway.Active.Remove(project.Id); else gateway.Active[project.Id] = previous; next.Dispose(); await gateway.ApplyAsync(CancellationToken.None); throw; }
    }
    private Uri LocalUrl(Project project) => project.Kind == "port" ? new($"http://127.0.0.1:{project.Port}/") : new($"http://127.0.0.1:{gateway.LocalPort}{project.LocalPath}");
    private async Task UnpublishAsync(Project project, CancellationToken token)
    {
        if (gateway.Slots.Shares.TryGetValue(project.Id, out var share) && share.RootPort is { } port && port != 443) account.StopFunnel(port);
        gateway.Slots.Release(project.Id); await gateway.ApplyAsync(token);
    }
    private async Task StopProjectAsync(Project project, CancellationToken token) { await UnpublishAsync(project, token); if (gateway.Active.Remove(project.Id, out var content)) content.Dispose(); await gateway.ApplyAsync(token); }
    private void PublishDialog(Project project)
    {
        if (gateway.Slots.Shares.Count >= 3) { progress = "三个公网名额已用完，请先取消一项。"; Render(); return; }
        var dialog = Dialog("发布项目"); var stack = DialogBody(dialog);
        var method = new ComboBox { ItemsSource = new[] { "按项目名发布", "使用独立入口" }, SelectedIndex = project.Kind == "port" ? 1 : 0, Margin = new Thickness(0, 8, 0, 8) };
        var slug = new TextBox { Text = project.Slug, IsReadOnly = project.LockedSlug };
        System.Windows.Automation.AutomationProperties.SetName(method, "发布方式");
        System.Windows.Automation.AutomationProperties.SetName(slug, "项目标识");
        var consent = new CheckBox { Content = "本机项目已配置为使用这个基础路径", Margin = new Thickness(0, 6, 0, 6) };
        var detail = Label(""); var error = Accent("", 12);
        var link = new TextBox { IsReadOnly = true, FontSize = 12 }; System.Windows.Automation.AutomationProperties.SetName(link, "预计公网链接");
        var copyPath = Button("复制基础路径", () => { try { Clipboard.SetText($"/p/{slug.Text}/"); copyPathFeedback(); } catch (System.Runtime.InteropServices.ExternalException) { error.Text = "剪贴板正在使用，请重试。"; } });
        void copyPathFeedback() { error.Text = "基础路径已复制。"; }
        var slugGroup = Column(Label("项目标识"), slug, Label(project.LockedSlug ? "首次发布后标识已固定；修改显示名称不会改变链接。" : "使用小写字母、数字和短横线，最多 48 个字符。", 11));
        var ok = Button("发布并验证", () => dialog.DialogResult = true, primary: true); ok.IsDefault = true;
        void Update()
        {
            var root = method.SelectedIndex == 1; slugGroup.Visibility = root ? Visibility.Collapsed : Visibility.Visible; copyPath.Visibility = !root && project.Kind == "port" ? Visibility.Visible : Visibility.Collapsed;
            consent.Visibility = !root && project.Kind == "port" ? Visibility.Visible : Visibility.Collapsed;
            var port = gateway.Slots.NextRoot; var key = $"{account.Current?.Id}|{account.Host}|{port}";
            var reused = catalog.RootOwners.TryGetValue(key, out var owner) && owner != project.Id;
            link.Text = root ? $"https://{account.Host}{(port == 443 ? "" : ":" + port)}/" : $"https://{account.Host}/p/{slug.Text}/";
            detail.Text = root ? "适用于从根路径加载资源或 API 的项目。" + (reused ? " 之前分享的同一地址将指向这个新项目。" : "") : "适用于使用相对路径加载资源的 HTML。";
            var valid = Catalog.ValidSlug(slug.Text) && !catalog.Projects.Any(p => p.Id != project.Id && p.Slug == slug.Text);
            error.Text = !root && !valid ? "项目标识无效或已被使用。" : !root && project.Kind == "port" && consent.IsChecked != true ? "请先复制基础路径，在原项目中配置后确认。" : "";
            ok.IsEnabled = root || valid && (project.Kind != "port" || consent.IsChecked == true);
        }
        method.SelectionChanged += (_, _) => Update(); slug.TextChanged += (_, _) => { consent.IsChecked = false; Update(); }; consent.Checked += (_, _) => Update(); consent.Unchecked += (_, _) => Update(); Update();
        stack.Children.Add(Text(project.Name, 17)); stack.Children.Add(Label("选择发布方式")); stack.Children.Add(method); stack.Children.Add(Card(Column(Label("预计公网链接", 11), link, detail))); stack.Children.Add(slugGroup); stack.Children.Add(copyPath); stack.Children.Add(consent); stack.Children.Add(error);
        stack.Children.Add(Label("任何拿到链接的人都可以访问。最多同时分享 3 个项目。", 11));
        var cancel = Button("取消", () => dialog.DialogResult = false); cancel.IsCancel = true; var buttons = Row(cancel, ok); buttons.HorizontalAlignment = HorizontalAlignment.Right; stack.Children.Add(buttons);
        if (dialog.ShowDialog() != true) return;
        project.Slug = slug.Text; catalog.Save(); bool root = method.SelectedIndex == 1;
        Run("正在发布并验证…", async token =>
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
                await PublicVerifier.VerifyAsync(share.Url(account.Host, project), share.Marker, token);
                await account.RefreshAsync(token);
                if (expected != account.Current?.Id || account.Backend != "Running") throw new AccountIdentityException();
                share.Verified = true; share.Pending = false; gateway.Slots.Home ??= project.Id; project.LockedSlug = true;
                if (share.RootPort is { } rootPort) catalog.RootOwners[$"{account.Current?.Id}|{account.Host}|{rootPort}"] = project.Id;
                catalog.Save(); await gateway.ApplyAsync(token);
            }
            catch { if (share.RootPort is { } port && port != 443) account.StopFunnel(port); gateway.Slots.Release(project.Id); await gateway.ApplyAsync(CancellationToken.None); throw; }
        }, true);
    }
    private void ChangeAccount(string caption, Func<CancellationToken, Task> action, bool resume = true)
    {
        if (!Confirm(caption, "当前公网分享将全部撤下。项目列表和源文件会保留，恢复后由你手动发布。")) return;
        Run(caption + "…", async t => { account.StopFunnels(); gateway.Slots.Clear(); ready = false; await gateway.ApplyAsync(t); await action(t); if (resume) await StartupAsync(t); else await account.RefreshAsync(t); });
    }
    private void AddHtmlDialog() { var picker = new OpenFileDialog { Filter = "HTML|*.html;*.htm", Multiselect = true }; if (picker.ShowDialog(this) == true) Run("正在添加 HTML…", t => { foreach (var path in picker.FileNames) AddHtml(path); return Task.CompletedTask; }); }
    private void AddHtml(string path) { if (!new[] { ".html", ".htm" }.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new IOException("请选择 HTML 文件。"); catalog.Add(new Project { Source = path, Name = Path.GetFileNameWithoutExtension(path) }); }
    private void AddFolder()
    {
        var folder = new OpenFolderDialog(); if (folder.ShowDialog(this) != true) return;
        var entry = new OpenFileDialog { InitialDirectory = folder.FolderName, Filter = "入口 HTML|*.html;*.htm" }; if (entry.ShowDialog(this) != true) return;
        var relative = Path.GetRelativePath(folder.FolderName, entry.FileName); if (relative.StartsWith("..") || Path.IsPathRooted(relative)) { progress = "入口必须位于所选文件夹中。"; Render(); return; }
        Run("正在导入项目…", t => { catalog.Add(new Project { Kind = "folder", Source = folder.FolderName, Entry = relative.Replace('\\', '/'), Name = Path.GetFileName(folder.FolderName) }); return Task.CompletedTask; });
    }
    private void AddPort()
    {
        var value = Prompt("添加本机 HTTP 服务", "先启动项目，再输入它在 127.0.0.1 上的端口。", "3000", text => ValidatePort(text)); if (value is null) return;
        Run("正在检查端口…", async t => { var port = int.Parse(value); await Runtime.CheckProjectAsync(port, t); var project = new Project { Kind = "port", Port = port, Name = "本机端口 " + port }; catalog.Add(project); sessionProjects.Add(project.Id); });
    }
    private void Relocate(Project project)
    {
        if (project.Kind == "port")
        {
            var value = Prompt("重新定位端口", "输入新的本机 HTTP 端口。已分享的项目请先取消发布。", project.Port.ToString(), text => ValidatePort(text, project.Id)); if (value is null) return;
            if (!int.TryParse(value, out var port) || !Runtime.ValidProjectPort(port) || catalog.Projects.Any(p => p.Id != project.Id && p.Kind == "port" && p.Port == port)) { progress = "端口无效或已在项目列表中。"; Render(); return; }
            project.Port = port;
        }
        else { var picker = new OpenFileDialog { Filter = "HTML|*.html;*.htm" }; if (picker.ShowDialog(this) != true) return; if (project.Kind == "html") project.Source = picker.FileName; else { var folder = new OpenFolderDialog { Title = "重新选择项目根文件夹" }; if (folder.ShowDialog(this) != true) return; project.Source = folder.FolderName; project.Entry = Path.GetRelativePath(project.Source, picker.FileName).Replace('\\', '/'); } }
        Run("正在更新项目位置…", async token =>
        {
            catalog.Save();
            if (gateway.Active.Remove(project.Id, out var previous)) previous.Dispose();
            if (gateway.Running) await gateway.ApplyAsync(token);
        });
    }
    private Window Dialog(string title)
    {
        var stack = Column(Accent("DEMOMUX", 10), Heading(title, 25)); stack.Margin = new Thickness(24);
        var window = new Window { Owner = this, Title = "DemoMux · " + title, Width = 540, SizeToContent = SizeToContent.Height, MaxHeight = Math.Min(760, SystemParameters.WorkArea.Height - 40), WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Theme.Apply(window); window.SourceInitialized += (_, _) => WindowMaterial.Apply(window); return window;
    }
    private static StackPanel DialogBody(Window dialog) => (StackPanel)((ScrollViewer)dialog.Content).Content;
    private string? ValidatePort(string text, Guid? except = null) => !int.TryParse(text, out var port) || !Runtime.ValidProjectPort(port) ? "请输入 1 到 65535 的端口，8081 留给预览入口。" : catalog.Projects.Any(p => p.Id != except && p.Kind == "port" && p.Port == port) ? "这个端口已经在项目列表中。" : null;
    private bool Confirm(string title, string caption)
    {
        var dialog = Dialog(title); var stack = DialogBody(dialog); stack.Children.Add(Label(caption));
        var cancel = Button("取消", () => dialog.DialogResult = false); cancel.IsCancel = true;
        var ok = Button("确认" + title, () => dialog.DialogResult = true, primary: true);
        var row = Row(cancel, ok); row.HorizontalAlignment = HorizontalAlignment.Right; row.Margin = new Thickness(0, 15, 0, 0); stack.Children.Add(row); dialog.Loaded += (_, _) => cancel.Focus();
        return dialog.ShowDialog() == true;
    }
    private string? Prompt(string title, string caption, string value, Func<string, string?>? validate = null)
    {
        var dialog = Dialog(title); var stack = DialogBody(dialog); var field = new TextBox { Text = value }; System.Windows.Automation.AutomationProperties.SetName(field, title);
        var error = Accent("", 12); var ok = Button("确定", () => dialog.DialogResult = true, primary: true); ok.IsDefault = true;
        void Update() { var issue = validate?.Invoke(field.Text.Trim()); error.Text = issue ?? ""; ok.IsEnabled = issue is null; }
        field.TextChanged += (_, _) => Update(); Update(); stack.Children.Add(Label(caption)); stack.Children.Add(field); stack.Children.Add(error);
        var cancel = Button("取消", () => dialog.DialogResult = false); cancel.IsCancel = true; var row = Row(cancel, ok); row.HorizontalAlignment = HorizontalAlignment.Right; row.Margin = new Thickness(0, 12, 0, 0); stack.Children.Add(row);
        dialog.Loaded += (_, _) => { field.Focus(); field.SelectAll(); }; return dialog.ShowDialog() == true ? field.Text.Trim() : null;
    }
}
