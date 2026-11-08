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
        try { await gateway.ApplyAsync(token); previous?.Dispose(); if (project.Kind == "port" && !sessionPorts.Contains(project.Port)) sessionPorts.Add(project.Port); }
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
        var dialog = Dialog("发布「" + project.Name + "」"); var stack = (StackPanel)dialog.Content;
        var method = new ComboBox { ItemsSource = new[] { "按项目名发布", "使用独立入口" }, SelectedIndex = project.Kind == "port" ? 1 : 0, Margin = new Thickness(0, 8, 0, 8) };
        var slug = new TextBox { Text = project.Slug, IsReadOnly = project.LockedSlug };
        var consent = new CheckBox { Content = "本机项目已配置为使用这个基础路径", Foreground = Fore };
        var detail = Text("");
        void Update() { var root = method.SelectedIndex == 1; slug.IsEnabled = !root; var port = gateway.Slots.NextRoot; var key = $"{account.Current?.Id}|{account.Host}|{port}"; var reused = catalog.RootOwners.TryGetValue(key, out var owner) && owner != project.Id; detail.Text = root ? $"https://{account.Host}{(port == 443 ? "" : ":" + port)}/\n适用于从根路径加载资源或 API 的项目。{(reused ? "之前分享的同一地址将指向这个新项目。" : "")}" : $"https://{account.Host}/p/{slug.Text}/\n适用于使用相对路径加载资源的 HTML。"; }
        method.SelectionChanged += (_, _) => Update(); slug.TextChanged += (_, _) => { consent.IsChecked = false; Update(); }; Update();
        stack.Children.Add(Text("选择发布方式并检查链接。任何拿到公网链接的人都可以访问。")); stack.Children.Add(method); stack.Children.Add(detail); stack.Children.Add(slug); if (project.Kind == "port") stack.Children.Add(consent);
        var ok = Button("发布", () => { if (method.SelectedIndex == 0 && (!Catalog.ValidSlug(slug.Text) || catalog.Projects.Any(p => p.Id != project.Id && p.Slug == slug.Text) || (project.Kind == "port" && consent.IsChecked != true))) { detail.Text = "标识无效或重复，或尚未确认基础路径。"; return; } dialog.DialogResult = true; }); stack.Children.Add(ok);
        if (dialog.ShowDialog() != true) return;
        project.Slug = slug.Text; catalog.Save(); bool root = method.SelectedIndex == 1;
        Run("正在发布并验证…", async token =>
        {
            var share = gateway.Slots.Reserve(project, root);
            try
            {
                var expected = account.Current?.Id; await account.RefreshAsync(token);
                if (expected != account.Current?.Id || account.Backend != "Running") throw new IOException("登录身份已改变，请重新启动演示。");
                if (!gateway.Active.ContainsKey(project.Id)) await StartProjectAsync(project, token);
                await gateway.ApplyAsync(token);
                if (share.RootPort is { } port && port != 443) await account.FunnelAsync(port, gateway.RootListeners[port], token);
                await PublicVerifier.VerifyAsync(share.Url(account.Host, project), share.Marker, token);
                await account.RefreshAsync(token);
                if (expected != account.Current?.Id || account.Backend != "Running") throw new IOException("发布期间登录身份已改变，请重新启动演示。");
                share.Verified = true; gateway.Slots.Home ??= project.Id; project.LockedSlug = true;
                if (share.RootPort is { } rootPort) catalog.RootOwners[$"{account.Current?.Id}|{account.Host}|{rootPort}"] = project.Id;
                catalog.Save(); await gateway.ApplyAsync(token);
            }
            catch { if (share.RootPort is { } port && port != 443) account.StopFunnel(port); gateway.Slots.Release(project.Id); await gateway.ApplyAsync(CancellationToken.None); throw; }
        });
    }
    private void ChangeAccount(string caption, Func<CancellationToken, Task> action, bool resume = true)
    {
        if (MessageBox.Show(this, caption + "会撤下当前分享，项目列表保留。", caption, MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
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
        var value = Prompt("添加本机 HTTP 服务", "先启动项目，再输入它在 127.0.0.1 上的端口。", "3000"); if (value is null) return;
        Run("正在检查端口…", async t => { if (!int.TryParse(value, out var port) || !Runtime.ValidProjectPort(port)) throw new IOException("请输入 1 到 65535 的端口，8081 为预览入口。"); await Runtime.CheckProjectAsync(port, t); catalog.Add(new Project { Kind = "port", Port = port, Name = "本机端口 " + port }); sessionPorts.Add(port); });
    }
    private void Relocate(Project project)
    {
        if (project.Kind == "port") { var value = Prompt("重新定位端口", "输入新的本机 HTTP 端口", project.Port.ToString()); if (int.TryParse(value, out var port) && Runtime.ValidProjectPort(port)) project.Port = port; }
        else { var picker = new OpenFileDialog { Filter = "HTML|*.html;*.htm" }; if (picker.ShowDialog(this) != true) return; if (project.Kind == "html") project.Source = picker.FileName; else { var folder = new OpenFolderDialog { Title = "重新选择项目根文件夹" }; if (folder.ShowDialog(this) != true) return; project.Source = folder.FolderName; project.Entry = Path.GetRelativePath(project.Source, picker.FileName).Replace('\\', '/'); } }
        catalog.Save(); Render();
    }
    private Window Dialog(string title) => new() { Owner = this, Title = title, Width = 550, SizeToContent = SizeToContent.Height, MaxHeight = 700, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Surface, Foreground = Fore, Content = new StackPanel { Margin = new Thickness(22) } };
    private string? Prompt(string title, string caption, string value)
    {
        var dialog = Dialog(title); var stack = (StackPanel)dialog.Content; var field = new TextBox { Text = value }; stack.Children.Add(Text(caption)); stack.Children.Add(field); var ok = new Button { Content = "确定", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(12, 6, 12, 6), IsDefault = true }; ok.Click += (_, _) => dialog.DialogResult = true; stack.Children.Add(ok); dialog.Loaded += (_, _) => { field.Focus(); field.SelectAll(); }; return dialog.ShowDialog() == true ? field.Text.Trim() : null;
    }
}
