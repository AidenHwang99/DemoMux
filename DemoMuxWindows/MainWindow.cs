using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
namespace DemoMux;

internal sealed partial class MainWindow : Window
{
    private readonly Catalog catalog = Catalog.Load();
    private readonly Component caddyComponent = Runtime.LoadComponents().Single(c => c.Name == "Caddy");
    private readonly Component tailscaleComponent = Runtime.LoadComponents().Single(c => c.Name == "Tailscale");
    private readonly string session = Directory.CreateDirectory(Path.Combine(Runtime.Data, "Sessions", Guid.NewGuid().ToString("N"))).FullName;
    private readonly Gateway gateway;
    private readonly AccountSession account;
    private readonly StackPanel body = new();
    private readonly StackPanel footer = new() { Margin = new Thickness(28, 0, 28, 18) };
    private Window? accountPanel;
    private readonly List<int> sessionPorts = [];
    private CancellationTokenSource? operation;
    private Uri? authUrl;
    private bool ready, dashboard, closing, verifyingPublic;
    private readonly System.Windows.Threading.DispatcherTimer monitor = new() { Interval = TimeSpan.FromSeconds(2) };
    private string progress = "完成五步设置后进入控制台";
    internal static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    internal static readonly Brush Fore = Brush("#faf2f5"), Muted = Brush("#c8b6bf"), Surface = Brush("#22181e");
    public MainWindow()
    {
        gateway = new(caddyComponent, session, catalog); account = new(tailscaleComponent);
        account.Authorization += url => { authUrl = url; progress = "请完成 Tailscale 登录或 Funnel 授权"; Render(); };
        Title = "DemoMux"; Width = 1040; Height = 800; MinWidth = 720; MinHeight = 560;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/DemoMux.ico"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SourceInitialized += (_, _) => WindowMaterial.Apply(this);
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 13; Foreground = Fore;
        Background = new LinearGradientBrush(Brush("#331522").Color, Brush("#100d11").Color, 65);
        Resources.Add(typeof(Button), ButtonStyle());
        Resources.Add(typeof(ComboBox), Theme.ComboBoxStyle());
        Resources.Add(typeof(TextBox), new Style(typeof(TextBox)) { Setters = { new Setter(Control.BackgroundProperty, Surface), new Setter(Control.ForegroundProperty, Fore), new Setter(Control.PaddingProperty, new Thickness(8)) } });
        var layout = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = layout;
        body.Margin = new Thickness(28); AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (operation is null && dashboard && e.Data.GetData(DataFormats.FileDrop) is string[] paths) Run("正在导入 HTML…", token => { foreach (var path in paths) AddHtml(path); return Task.CompletedTask; }); };
        Loaded += (_, _) => Run("准备公网演示", StartupAsync, true);
        Closing += (_, e) => { if (operation is not null) { e.Cancel = true; closing = true; operation.Cancel(); } };
        monitor.Tick += (_, _) => { if (operation is null && ready && (!gateway.Running || !account.Running)) { ready = false; foreach (var share in gateway.Slots.Shares.Values) share.Verified = false; progress = "公网连接已断开，可重新启动演示。"; Render(); } };
        monitor.Start();
        Closed += (_, _) => { monitor.Stop(); account.Dispose(); gateway.Dispose(); Runtime.TryDelete(session); };
        Render();
    }
    private static Style ButtonStyle()
    {
        var template = new ControlTemplate(typeof(Button)); var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, Brush("#674052")); border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 8, 12, 8)); border.AppendChild(presenter); template.VisualTree = border;
        var style = new Style(typeof(Button)); style.Setters.Add(new Setter(Control.TemplateProperty, template)); style.Setters.Add(new Setter(Control.BackgroundProperty, Surface)); style.Setters.Add(new Setter(Control.ForegroundProperty, Fore)); style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 3, 8, 3)));
        style.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, Brush("#8e354c")) } });
        style.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(UIElement.OpacityProperty, .45) } }); return style;
    }
    internal static TextBlock Text(string value, double size = 13) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Fore, Margin = new Thickness(0, 4, 0, 4) };
    private Button Button(string caption, Action click, bool enabled = true, bool always = false)
    {
        var button = new Button { Content = caption, IsEnabled = enabled && (always || operation is null) }; button.Click += (_, _) => click(); System.Windows.Automation.AutomationProperties.SetName(button, caption); return button;
    }
    internal static WrapPanel Row(params UIElement[] elements) { var row = new WrapPanel(); foreach (var element in elements) row.Children.Add(element); return row; }
    private static Border Card(UIElement element) => new() { Child = element, Padding = new Thickness(16), Margin = new Thickness(0, 7, 0, 7), CornerRadius = new CornerRadius(14), Background = Brush("#d919151a"), BorderBrush = Brush("#44303b"), BorderThickness = new Thickness(1) };
    private static StackPanel Column(params UIElement[] elements) { var column = new StackPanel(); foreach (var element in elements) column.Children.Add(element); return column; }
    private void Render()
    {
        body.Children.Clear(); footer.Children.Clear();
        var eyebrow = Text(dashboard ? "本机项目  ·  公网演示" : "DEMOMUX  /  设置向导", 12); eyebrow.Foreground = Brush("#ff5b75");
        var verified = ready && gateway.Slots.Shares.Values.All(share => share.Verified);
        var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = Text(dashboard ? "DemoMux" : "准备公网演示", dashboard ? 36 : 27); title.FontWeight = FontWeights.Bold;
        header.Children.Add(Column(eyebrow, title));
        var status = new Border { Child = Text($"{(verifyingPublic ? "正在检测公网" : verified ? "入口已验证" : "公网待复核")}   ·   公网发布 {gateway.Slots.Shares.Count} / 3", 13), Padding = new Thickness(13, 7, 13, 7), CornerRadius = new CornerRadius(15), Background = Surface, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(status, 1); header.Children.Add(status); body.Children.Add(header);
        var notice = Text(progress); notice.Foreground = Muted; body.Children.Add(notice);
        if (!dashboard) body.Children.Add(Card(Column(Text("01  准备 Caddy\n02  准备 Tailscale\n03  登录\n04  启用 Funnel\n05  验证公网介绍页", 18), Row(Button("重试", () => Run("准备公网演示", StartupAsync, true)), Button("使用说明", OpenGuide, true, true)))));
        var identity = new DockPanel(); var manage = Button("管理账号", OpenAccountManager, true, true); manage.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(manage, Dock.Right); identity.Children.Add(manage);
        var identityIcon = Text("\uE77B", 30); identityIcon.FontFamily = new FontFamily("Segoe MDL2 Assets"); identityIcon.Foreground = Brush("#ffa3b4"); identityIcon.Margin = new Thickness(0, 0, 12, 0); identityIcon.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(identityIcon, Dock.Left); identity.Children.Add(identityIcon);
        var caption = Text("TAILSCALE  /  演示身份", 10); caption.Foreground = Brush("#ff5b75");
        identity.Children.Add(Column(caption, Text($"{account.IdentitySummary}\n所属网络：{account.Current?.Tailnet ?? "—"} · {(account.Running && account.Backend == "Running" ? "已连接" : "上次使用 · 未连接")}", 12)));
        body.Children.Add(Card(identity));
        if (accountPanel?.IsVisible == true) RenderAccountManager();
        if (authUrl is not null) body.Children.Add(Button("打开授权网页", () => Open(authUrl), true, true));
        if (operation is not null) body.Children.Add(Button("取消", () => operation?.Cancel(), true, true));
        if (!dashboard) return;
        var quick = new ComboBox { MinWidth = 145, Margin = new Thickness(8) }; quick.Items.Add("本次会话端口"); foreach (var port in sessionPorts) quick.Items.Add(port); quick.SelectedIndex = 0;
        quick.SelectionChanged += (_, _) => { if (quick.SelectedItem is int port) { var project = catalog.Projects.First(p => p.Kind == "port" && p.Port == port); Run("正在启动项目…", t => StartProjectAsync(project, t)); } };
        body.Children.Add(Row(Button("添加 HTML", AddHtmlDialog), Button("导入项目文件夹", AddFolder), Button("添加本机端口", AddPort), quick));
        body.Children.Add(Card(Column(Text("拖入 HTML 文件", 15), Text("有图片、CSS 或 JavaScript 的页面可导入项目文件夹"))));
        body.Children.Add(Text("01   正在分享", 20));
        if (gateway.Slots.Shares.Count == 0) body.Children.Add(Text("暂无公网项目。添加项目后，可从列表发布。"));
        foreach (var project in catalog.Projects.Where(p => gateway.Slots.Shares.ContainsKey(p.Id))) body.Children.Add(ProjectCard(project, true));
        body.Children.Add(Text("02   项目列表", 20));
        if (catalog.Projects.Count == 0) body.Children.Add(Text("可以拖入 HTML，或添加已经运行的本机服务。"));
        foreach (var project in catalog.Projects.Where(p => !gateway.Slots.Shares.ContainsKey(p.Id))) body.Children.Add(ProjectCard(project, false));
        var preference = new CheckBox { Content = "公网就绪后打开首页", IsChecked = catalog.OpenHome, VerticalAlignment = VerticalAlignment.Center, Foreground = Fore, Margin = new Thickness(8) };
        preference.Click += (_, _) => { catalog.OpenHome = preference.IsChecked == true; catalog.Save(); };
        footer.Children.Add(Card(Row(Button("使用说明", OpenGuide, true, true), Button("重新检测公网", () => Run("正在检测公网…", RecheckAsync, true)), Button("停止全部", () => Run("正在停止…", t => { account.Dispose(); gateway.Dispose(); ready = false; return Task.CompletedTask; })), Button("启动演示", () => Run("准备公网演示", StartupAsync, true)), preference)));
    }
    private void OpenAccountManager()
    {
        if (accountPanel is null) { accountPanel = Dialog("DemoMux · 账号与公网"); accountPanel.Width = 610; accountPanel.Height = 650; accountPanel.SizeToContent = SizeToContent.Manual; accountPanel.Closed += (_, _) => accountPanel = null; }
        RenderAccountManager(); accountPanel.Show(); accountPanel.Activate();
    }
    private void RenderAccountManager()
    {
        if (accountPanel is null) return;
        accountPanel.Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(22), Children = {
            Text("DEMOMUX  /  TAILSCALE", 11), Text("账号与公网", 28), Text($"{account.DisplayName}\n登录标识：{account.Current?.Account ?? "未登录"}\n所属网络：{account.Current?.Tailnet ?? "—"} · {account.ConnectionDescription}"),
            Text("账号之间共享项目列表。切换后只开启介绍页，项目由你手动发布。这里只管理 DemoMux 的私有实例。"),
            Row(Button("添加账号", () => ChangeAccount("添加账号", t => account.LoginAsync(true, false, t)), account.Running), Button("重新授权", () => ChangeAccount("重新授权", t => account.LoginAsync(false, true, t)), account.Running), Button("暂停", () => ChangeAccount("暂停", async t => { await account.ExecuteAsync(t, "down"); }, false), account.Running), Button("退出账号", () => ChangeAccount("退出账号", async t => { await account.ExecuteAsync(t, "logout"); }, false), account.Running)), AccountChoices()
        } } };
        var controls = (StackPanel)((ScrollViewer)accountPanel.Content).Content;
        if (authUrl is { } authorization) controls.Children.Add(Button("打开授权网页", () => Open(authorization), true, true));
        if (operation is not null) controls.Children.Add(Button("取消", () => operation?.Cancel(), true, true));
    }
    private UIElement AccountChoices()
    {
        var row = new WrapPanel(); foreach (var profile in account.Profiles.Where(p => !p.Selected)) row.Children.Add(Button("切换到 " + profile.Account, () => ChangeAccount("切换账号", async t => { await account.ExecuteAsync(t, "switch", profile.Id); await account.LoginAsync(false, false, t); }))); return row;
    }
    private UIElement ProjectCard(Project project, bool sharing)
    {
        var description = Text(project.Description, 12); description.ToolTip = project.Kind == "port" ? project.Description : project.Source;
        var name = Text(project.Name, 14); name.FontWeight = FontWeights.SemiBold;
        var content = Column(name, description);
        var controls = Row(Button("预览", () => Run("正在准备预览…", async t => { if (!gateway.Active.ContainsKey(project.Id)) await StartProjectAsync(project, t); Open(LocalUrl(project)); })), Button(gateway.Active.ContainsKey(project.Id) ? "重启" : "启动", () => Run("正在启动项目…", t => StartProjectAsync(project, t))), Button("停止", () => Run("正在停止项目…", t => StopProjectAsync(project, t))));
        if (sharing)
        {
            var share = gateway.Slots.Shares[project.Id]; var url = share.Url(account.Host, project);
            content.Children.Add(Text($"{(share.Pending ? "正在发布并验证…" : share.Verified ? "已发布" : "已发布 · 待复核")} · {url}", 12));
            if (gateway.Slots.Home == project.Id) content.Children.Add(Text("公网首页 · 原介绍页入口转向此项目"));
            controls.Children.Add(Button("复制链接", () => Copy(url.AbsoluteUri), !share.Pending, true)); controls.Children.Add(Button("打开", () => Open(url), !share.Pending, true));
            controls.Children.Add(Button("设为首页", () => Run("正在切换首页…", async t => { gateway.Slots.Home = project.Id; await gateway.ApplyAsync(t); })));
            controls.Children.Add(Button("取消发布", () => Run("正在取消发布…", t => UnpublishAsync(project, t))));
        }
        else controls.Children.Add(Button("发布", () => PublishDialog(project), ready));
        controls.Children.Add(Button("改名", () => { var name = Prompt("修改显示名称", "公网链接仍使用原项目标识。", project.Name); if (!string.IsNullOrWhiteSpace(name)) { project.Name = name; catalog.Save(); Render(); } }));
        controls.Children.Add(Button("重新定位", () => Relocate(project), !sharing));
        content.Children.Add(controls); return Card(content);
    }
    private async void Run(string caption, Func<CancellationToken, Task> action, bool publicOperation = false)
    {
        if (operation is not null) return;
        using var cancellation = new CancellationTokenSource(); operation = cancellation; verifyingPublic = publicOperation; progress = caption; Render();
        try { await action(cancellation.Token); progress = ready ? "入口已验证。项目由你手动发布。" : "操作完成。"; }
        catch (OperationCanceledException) { if (!dashboard) { account.StopFunnels(); ready = false; } progress = cancellation.IsCancellationRequested ? "操作已取消，可重试。" : "操作超时，请重试。"; }
        catch (AccountIdentityException error)
        {
            account.StopFunnels(); account.InvalidateIdentity(); gateway.Slots.Clear(); ready = false;
            try { if (gateway.Running) await gateway.ApplyAsync(CancellationToken.None); } catch { gateway.Dispose(); }
            progress = error.Message;
        }
        catch (Exception error) { progress = Regex.Replace(error.Message, @"https://(?:login|console)\.tailscale\.com/\S+", "[授权链接]"); if (progress.Length > 600) progress = progress[..600]; }
        finally { operation = null; verifyingPublic = false; authUrl = null; if (closing) Close(); else Render(); }
    }
    private void Report(string value) { progress = value; Render(); }
    private void OpenGuide() { new GuideWindow { Owner = this }.Show(); }
    internal static void Open(Uri url) { try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception e) { MessageBox.Show(e.Message, "无法打开浏览器"); } }
    private void Copy(string value) { try { Clipboard.SetText(value); } catch (System.Runtime.InteropServices.ExternalException) { progress = "剪贴板正在使用，请重试。"; Render(); } }
}
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(false, @"Local\DemoMux." + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value);
        try { if (!single.WaitOne(0)) { MessageBox.Show("DemoMux 已经运行。", "DemoMux"); return; } } catch (AbandonedMutexException) { }
        try { new Application().Run(new MainWindow()); } catch (Exception error) { MessageBox.Show(error.Message, "DemoMux 启动失败"); } finally { single.ReleaseMutex(); }
    }
}
