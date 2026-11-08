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
    private readonly List<int> sessionPorts = [];
    private CancellationTokenSource? operation;
    private Uri? authUrl;
    private bool ready, dashboard, closing;
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
        Resources.Add(typeof(TextBox), new Style(typeof(TextBox)) { Setters = { new Setter(Control.BackgroundProperty, Surface), new Setter(Control.ForegroundProperty, Fore), new Setter(Control.PaddingProperty, new Thickness(8)) } });
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Margin = new Thickness(28); AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (operation is null && dashboard && e.Data.GetData(DataFormats.FileDrop) is string[] paths) Run("正在导入 HTML…", token => { foreach (var path in paths) AddHtml(path); return Task.CompletedTask; }); };
        Loaded += (_, _) => Run("准备公网演示", StartupAsync);
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
        body.Children.Clear();
        body.Children.Add(Text(dashboard ? "本机项目  ·  公网演示" : "DEMOMUX  /  设置向导", 12));
        body.Children.Add(Text(dashboard ? "DemoMux" : "准备公网演示", 36));
        var verified = ready && gateway.Slots.Shares.Values.All(share => share.Verified);
        body.Children.Add(Text($"{(operation is not null ? "正在检测公网" : verified ? "入口已验证" : "公网待复核")}   ·   公网发布 {gateway.Slots.Shares.Count} / 3", 14));
        body.Children.Add(Text(progress));
        if (!dashboard) body.Children.Add(Card(Column(Text("01  准备 Caddy\n02  准备 Tailscale\n03  登录\n04  启用 Funnel\n05  验证公网介绍页", 18), Row(Button("重试", () => Run("准备公网演示", StartupAsync)), Button("使用说明", OpenGuide, true, true)))));
        body.Children.Add(Card(Column(Text(account.DisplayName.Length > 0 ? account.DisplayName : "尚未连接账号", 18), Text($"登录标识：{account.Current?.Account ?? "未登录"}   所属网络：{account.Current?.Tailnet ?? "—"}   {account.Backend}"),
            Row(Button("添加账号", () => ChangeAccount("添加账号", t => account.LoginAsync(true, false, t))), Button("重新授权", () => ChangeAccount("重新授权", t => account.LoginAsync(false, true, t))), Button("暂停", () => ChangeAccount("暂停", async t => { await account.ExecuteAsync(t, "down"); }, false)), Button("退出账号", () => ChangeAccount("退出账号", async t => { await account.ExecuteAsync(t, "logout"); }, false))), AccountChoices())));
        if (authUrl is not null) body.Children.Add(Button("打开授权网页", () => Open(authUrl), true, true));
        if (operation is not null) body.Children.Add(Button("取消", () => operation?.Cancel(), true, true));
        if (!dashboard) return;
        var quick = new ComboBox { MinWidth = 145, Margin = new Thickness(8), Foreground = Brushes.Black }; quick.Items.Add("本次会话端口"); foreach (var port in sessionPorts) quick.Items.Add(port); quick.SelectedIndex = 0;
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
        body.Children.Add(Card(Row(Button("使用说明", OpenGuide, true, true), Button("重新检测公网", () => Run("正在检测公网…", RecheckAsync)), Button("停止全部", () => Run("正在停止…", t => { account.Dispose(); gateway.Dispose(); ready = false; return Task.CompletedTask; })), Button("启动演示", () => Run("准备公网演示", StartupAsync)), preference)));
    }
    private UIElement AccountChoices()
    {
        var row = new WrapPanel(); foreach (var profile in account.Profiles.Where(p => !p.Selected)) row.Children.Add(Button("切换到 " + profile.Account, () => ChangeAccount("切换账号", async t => { await account.ExecuteAsync(t, "switch", profile.Id); await account.LoginAsync(false, false, t); }))); return row;
    }
    private UIElement ProjectCard(Project project, bool sharing)
    {
        var content = Column(Text(project.Name, 16), Text(project.Description));
        var controls = Row(Button("预览", () => Run("正在准备预览…", async t => { if (!gateway.Active.ContainsKey(project.Id)) await StartProjectAsync(project, t); Open(LocalUrl(project)); })), Button(gateway.Active.ContainsKey(project.Id) ? "重启" : "启动", () => Run("正在启动项目…", t => StartProjectAsync(project, t))), Button("停止", () => Run("正在停止项目…", t => StopProjectAsync(project, t))));
        if (sharing)
        {
            var share = gateway.Slots.Shares[project.Id]; var url = share.Url(account.Host, project);
            content.Children.Add(Text($"{(share.Verified ? "已发布" : "已发布 · 待复核")} · {url}"));
            if (gateway.Slots.Home == project.Id) content.Children.Add(Text("公网首页 · 原介绍页入口转向此项目"));
            controls.Children.Add(Button("复制链接", () => Copy(url.AbsoluteUri), true, true)); controls.Children.Add(Button("打开", () => Open(url), true, true));
            controls.Children.Add(Button("设为首页", () => Run("正在切换首页…", async t => { gateway.Slots.Home = project.Id; await gateway.ApplyAsync(t); })));
            controls.Children.Add(Button("取消发布", () => Run("正在取消发布…", t => UnpublishAsync(project, t))));
        }
        else controls.Children.Add(Button("发布", () => PublishDialog(project), ready));
        controls.Children.Add(Button("改名", () => { var name = Prompt("修改显示名称", "公网链接仍使用原项目标识。", project.Name); if (!string.IsNullOrWhiteSpace(name)) { project.Name = name; catalog.Save(); Render(); } }));
        controls.Children.Add(Button("重新定位", () => Relocate(project), !sharing));
        content.Children.Add(controls); return Card(content);
    }
    private async void Run(string caption, Func<CancellationToken, Task> action)
    {
        if (operation is not null) return;
        using var cancellation = new CancellationTokenSource(); operation = cancellation; progress = caption; Render();
        try { await action(cancellation.Token); progress = ready ? "入口已验证。项目由你手动发布。" : "操作完成。"; }
        catch (OperationCanceledException) { if (!dashboard) { account.StopFunnels(); ready = false; } progress = cancellation.IsCancellationRequested ? "操作已取消，可重试。" : "操作超时，请重试。"; }
        catch (Exception error) { progress = Regex.Replace(error.Message, @"https://(?:login|console)\.tailscale\.com/\S+", "[授权链接]"); if (progress.Length > 600) progress = progress[..600]; }
        finally { operation = null; authUrl = null; if (closing) Close(); else Render(); }
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
