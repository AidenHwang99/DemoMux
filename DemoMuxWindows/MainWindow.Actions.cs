using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace DemoMux;
internal sealed partial class MainWindow
{
    private async Task ResumeAccountAsync(CancellationToken token)
    {
        account.StopFunnels(); gateway.Slots.Clear(); ready = false;
        if (gateway.Running) await gateway.ApplyAsync(token);
        await StartupAsync(token);
    }
    private async Task StartupAsync(CancellationToken token)
    {
        ready = false;
        Array.Fill(setupStates, SetupState.Pending);
        setupIdentity = null;
        Report("01 / 05  正在准备并校验 Caddy…"); await caddyComponent.InstallAsync(token);
        if (!gateway.Running) await gateway.ApplyAsync(token);
        setupStates[0] = SetupState.Complete;
        Report("02 / 05  正在准备并校验 Tailscale…"); await tailscaleComponent.InstallAsync(token); await account.StartAsync(token);
        setupStates[1] = SetupState.Complete;
        Report("03 / 05  正在连接账号…"); await account.LoginAsync(false, false, token); authUrl = null; gateway.Host = account.Host;
        setupIdentity = account.Current?.Id;
        setupStates[2] = SetupState.Complete;
        Report("04 / 05  正在启用 Funnel…"); await account.FunnelAsync(443, 8081, token); authUrl = null;
        setupStates[3] = SetupState.Complete;
        await CompleteSetupAsync(token);
    }
    private async Task CompleteSetupAsync(CancellationToken token)
    {
        await account.RefreshAsync(token);
        account.EnsureConnectedIdentity(setupIdentity);
        if (!account.FunnelRunning(443))
        {
            Report("04 / 05  正在恢复 Funnel…");
            await account.FunnelAsync(443, 8081, token);
            setupStates[3] = SetupState.Complete;
        }
        Report("05 / 05  正在验证公网介绍页…");
        retryAction = CompleteSetupAsync; retryCaption = "正在验证公网入口…"; retryPublic = true;
        await RecheckAsync(token);
        setupStates[4] = SetupState.Complete;
        dashboard = true;
        if (catalog.OpenHome) Open(new Uri($"https://{account.Host}/"));
    }
    private async Task RecheckAsync(CancellationToken token)
    {
        var expected = account.Current?.Id; await account.RefreshAsync(token);
        if (expected != account.Current?.Id || account.Backend != "Running") throw new AccountIdentityException();
        if (gateway.Slots.Shares.Count == 0) { ready = false; await VerifyPublicAsync(new Uri($"https://{account.Host}/"), gateway.Marker, token); ready = true; }
        else foreach (var share in gateway.Slots.Shares.Values) { share.Verified = false; await VerifyPublicAsync(share.Url(account.Host, catalog.Projects.First(p => p.Id == share.ProjectId)), share.Marker, token); share.Verified = true; }
    }
    private async Task VerifyPublicAsync(Uri url, string marker, CancellationToken token)
    {
        var identity = account.Current?.Id;
        await RunVerificationAsync(async t => {
                await account.RefreshAsync(t);
                account.EnsureConnectedIdentity(identity);
                await PublicVerifier.VerifyAsync(url, marker, t);
                await account.RefreshAsync(t);
                account.EnsureConnectedIdentity(identity);
            }, token);
    }
    internal async Task RunVerificationAsync(Func<CancellationToken, Task> check, CancellationToken token)
    {
        publicRetryActive = true;
        if (!dashboard) { setupStep = 5; setupStates[4] = SetupState.Running; }
        Render();
        try
        {
            await VerificationRetry.RunAsync(check, state => {
                if (!dashboard) setupStep = 5;
                retryAttempt = state.Attempt; retryWaiting = state.RetryInSeconds.HasValue;
                progress = state.RetryInSeconds is { } seconds ? $"{state.LastError} {seconds} 秒后自动重试 · 已尝试 {state.Attempt} 次" : $"正在校验公网入口 · 第 {state.Attempt} 次尝试";
                // Keep the visual tree and its compositor animation alive between ticks.
                if (noticeLabel is not null) noticeLabel.Text = progress;
                if (setupProgressLabel is not null) setupProgressLabel.Text = progress;
                if (accountProgressLabel is not null) accountProgressLabel.Text = progress;
                if (publicStatusLabel is not null) publicStatusLabel.Text = $"{(retryWaiting || retryAttempt > 1 ? "正在自动重试" : "正在检测公网")}   ·   公网发布 {gateway.Slots.Shares.Count} / 3";
                if (!dashboard && verificationStepLabel is not null) verificationStepLabel.Text = retryWaiting ? "等待自动重试" : $"第 {retryAttempt} 次校验";
            }, token);
        }
        finally { publicRetryActive = false; retryWaiting = false; }
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
        var window = new Window { Owner = this, Icon = Icon, Title = "DemoMux · " + title, Width = 540, SizeToContent = SizeToContent.Height, MaxHeight = Math.Min(760, SystemParameters.WorkArea.Height - 40), WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Content = new ScrollViewer { Focusable = false, Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
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
