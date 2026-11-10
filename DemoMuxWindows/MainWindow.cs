using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private readonly StackPanel footer = new() { Margin = new Thickness(24, 0, 24, 14) };
    private readonly ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private Window? accountPanel;
    private Border? dropArea;
    private readonly HashSet<Guid> sessionProjects = [];
    private CancellationTokenSource? operation;
    private Func<CancellationToken, Task>? retryAction;
    private Action? retryLocal;
    private string retryCaption = "";
    private bool retryPublic;
    private Uri? authUrl;
    private bool ready, dashboard, closing, verifyingPublic;
    private bool publicRetryActive, retryWaiting;
    private int retryAttempt;
    private readonly bool isolatedTest;
    private int setupStep;
    private enum SetupState { Pending, Running, Complete, Stopped, Failed }
    private readonly SetupState[] setupStates = new SetupState[5];
    private string? setupIdentity;
    private enum Notice { Info, Busy, Success, Error }
    private Notice notice;
    private readonly System.Windows.Threading.DispatcherTimer monitor = new() { Interval = TimeSpan.FromSeconds(2) };
    private string progress = "完成五步设置后，开始分享你的项目。";
    internal static SolidColorBrush Brush(string hex) => Theme.Color(hex);
    internal static readonly Brush Fore = Theme.Text, Muted = Theme.Muted, Surface = Theme.Surface;
    public MainWindow(bool startAutomatically = true, Func<ITrayIcon>? trayFactory = null)
    {
        isolatedTest = !startAutomatically;
        this.trayFactory = trayFactory ?? (() => new TrayIcon(!isolatedTest));
        gateway = new(caddyComponent, session, catalog); account = new(tailscaleComponent);
        account.Authorization += url => { authUrl = url; progress = "请在浏览器完成 Tailscale 登录或 Funnel 授权。"; Render(); };
        Title = "DemoMux"; Width = 920; Height = 720; MinWidth = 720; MinHeight = 560;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/DemoMux.ico"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SourceInitialized += (_, _) => { WindowMaterial.Apply(this); RestoreWindowPreference(); if (startAutomatically) InitializeTray(); };
        Theme.Apply(this);
        var layout = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        scroller.Content = body; layout.Children.Add(scroller); Content = layout; body.Margin = new Thickness(24, 20, 24, 12); body.MaxWidth = 1200; body.HorizontalAlignment = HorizontalAlignment.Center;
        Loaded += (_, _) => TrackWindowPreference();
        LocationChanged += (_, _) => TrackWindowPreference();
        SizeChanged += (_, _) => TrackWindowPreference();
        StateChanged += (_, _) => Dispatcher.InvokeAsync(TrackWindowPreference);
        AllowDrop = true;
        DragOver += (_, e) => { var accept = CanDrop(e); e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None; if (dropArea is not null) dropArea.BorderBrush = accept ? Theme.Link : Theme.Border; e.Handled = true; };
        DragLeave += (_, _) => { if (dropArea is not null) dropArea.BorderBrush = Theme.Border; };
        Drop += (_, e) => { if (CanDrop(e) && e.Data.GetData(DataFormats.FileDrop) is string[] paths) Run("正在添加 HTML…", token => { foreach (var path in paths) AddHtml(path); return Task.CompletedTask; }); e.Handled = true; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control) { OpenSettings(); e.Handled = true; return; }
            if (e.Key == Key.Escape && operation is not null) { operation.Cancel(); e.Handled = true; return; }
            if (operation is not null || !dashboard) return;
            if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) AddFolder(); else AddHtmlDialog(); e.Handled = true; }
            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { AddPort(); e.Handled = true; }
            if (e.Key == Key.F5 && ready) { Run("正在检测公网…", RecheckAsync, true); e.Handled = true; }
        };
        bool startupStarted = false;
        if (startAutomatically) Loaded += (_, _) => { if (startupStarted) return; startupStarted = true; Run("准备公网演示", StartupAsync, true); };
        Closing += HandleClosing;
        monitor.Tick += (_, _) => { if (operation is null && ready && (!gateway.Running || !account.Running)) { ready = false; foreach (var share in gateway.Slots.Shares.Values) share.Verified = false; notice = Notice.Error; progress = "公网连接已断开，请重新启动演示。"; retryAction = StartupAsync; retryCaption = "准备公网演示"; retryPublic = true; Render(); } };
        monitor.Start();
        Closed += (_, _) => { monitor.Stop(); tray?.Dispose(); account.Dispose(); gateway.Dispose(); Runtime.TryDelete(session); };
        Render();
    }
    private bool CanDrop(DragEventArgs e) => operation is null && dashboard && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths && paths.All(path => File.Exists(path) && new[] { ".html", ".htm" }.Contains(Path.GetExtension(path).ToLowerInvariant()));
    internal static TextBlock Text(string value, double size = 13) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Fore, Margin = new Thickness(0, 2, 0, 2), LineHeight = size * 1.35 };
    private Button Button(string caption, Action click, bool enabled = true, bool always = false, bool primary = false)
    {
        var button = new Button { Content = caption, IsEnabled = enabled && (always || operation is null) };
        if (primary) { button.Background = Theme.Action; button.FontWeight = FontWeights.SemiBold; }
        button.Click += (_, _) => click(); System.Windows.Automation.AutomationProperties.SetName(button, caption); return button;
    }
    internal static WrapPanel Row(params UIElement[] elements) { var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center }; foreach (var element in elements) row.Children.Add(element); return row; }
    private static Border Card(UIElement element) => new() { Child = element, Padding = new Thickness(14), Margin = new Thickness(0, 6, 0, 6), CornerRadius = new CornerRadius(12), Background = Theme.Surface, BorderBrush = Theme.Border, BorderThickness = new Thickness(1) };
    private static StackPanel Column(params UIElement[] elements) { var column = new StackPanel(); foreach (var element in elements) column.Children.Add(element); return column; }
    internal async void Run(string caption, Func<CancellationToken, Task> action, bool publicOperation = false)
    {
        if (operation is not null) return;
        retryAction = action; retryLocal = null; publishFailure = null; retryCaption = caption; retryPublic = publicOperation; retryAttempt = 0;
        using var cancellation = new CancellationTokenSource(); operation = cancellation; verifyingPublic = publicOperation; notice = Notice.Busy; progress = caption; Render();
        try { await action(cancellation.Token); notice = Notice.Success; progress = caption switch {
            "正在添加 HTML…" or "正在导入项目…" or "正在检查端口…" => "项目已添加。可以先本机预览，再发布分享。",
            "正在发布并验证…" => "发布完成，公网链接已验证。复制链接即可分享。",
            "正在取消发布…" => "已取消发布，公网名额已释放。",
            "正在停止项目…" => "项目预览与发布已停止。",
            "正在停止…" => "演示已停止。项目和账号已保留，可随时重新启动。",
            "正在启动项目…" => "项目已启动。源文件更新后，可从「更多」重新启动。",
            _ => ready ? "入口已验证。选择项目，开始分享。" : "操作完成。"
        }; }
        catch (OperationCanceledException) { MarkSetup(SetupState.Stopped); if (!dashboard) StopSetupFunnels(); notice = Notice.Info; progress = cancellation.IsCancellationRequested ? retryAttempt > 0 ? "已停止公网校验，尚未完成验证。可稍后重新校验。" : "操作已取消，可以重试。" : "操作超时，请重试。"; }
        catch (AccountIdentityException error)
        {
            publishFailure = null; retryLocal = null;
            Array.Fill(setupStates, SetupState.Pending, 2, 3);
            setupIdentity = null;
            if (!dashboard && setupStep is >= 3 and <= 5) setupStates[setupStep - 1] = SetupState.Failed;
            account.StopFunnels(); account.InvalidateIdentity(); gateway.Slots.Clear(); ready = false;
            try { if (gateway.Running) await gateway.ApplyAsync(CancellationToken.None); } catch { gateway.Dispose(); }
            notice = Notice.Error; progress = error.Message; retryAction = StartupAsync; retryCaption = "准备公网演示"; retryPublic = true;
        }
        catch (Exception error) { MarkSetup(SetupState.Failed); notice = Notice.Error; progress = Regex.Replace(error.Message, @"https://(?:login|console)\.tailscale\.com/\S+", "[授权链接]"); if (progress.Length > 600) progress = progress[..600]; }
        finally { operation = null; verifyingPublic = false; authUrl = null; if (closing) Close(); else Render(); }
    }
    private void Report(string value) { progress = value; var step = Regex.Match(value, @"^0([1-5]) / 05"); if (step.Success) { setupStep = int.Parse(step.Groups[1].Value); setupStates[setupStep - 1] = SetupState.Running; } Render(); }
    private void MarkSetup(SetupState state) { if (!dashboard && setupStep is >= 1 and <= 5 && setupStates[setupStep - 1] == SetupState.Running) setupStates[setupStep - 1] = state; }
    private void StopSetupFunnels()
    {
        account.StopFunnels(); ready = false;
        if (setupStates[3] is SetupState.Complete or SetupState.Running) setupStates[3] = SetupState.Stopped;
        if (setupStates[4] is SetupState.Complete or SetupState.Running) setupStates[4] = SetupState.Stopped;
    }
    private void Retry() { if (retryLocal is not null) retryLocal(); else if (retryAction is not null) Run(retryCaption, retryAction, retryPublic); }
    private void OpenGuide() { new GuideWindow { Owner = this }.Show(); }
    internal static void Open(Uri url) { try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception e) { MessageBox.Show(e.Message, "无法打开浏览器"); } }
    private void Copy(string value) { retryAction = null; retryLocal = null; try { Clipboard.SetText(value); notice = Notice.Success; progress = "链接已复制，可以粘贴分享。"; } catch (System.Runtime.InteropServices.ExternalException) { retryLocal = () => Copy(value); notice = Notice.Error; progress = "剪贴板正在使用，请重试。"; } Render(); }
}
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var installerGuard = new Mutex(false, @"Local\DemoMux.InstallationGuard");
        using var single = new Mutex(false, @"Local\DemoMux." + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value);
        try { if (!single.WaitOne(0)) { if (!TrayIcon.ActivateExisting()) MessageBox.Show("DemoMux 已经运行，请从任务栏或系统托盘打开已有窗口。", "DemoMux"); return; } } catch (AbandonedMutexException) { }
        try
        {
            var app = new Application(); var window = new MainWindow();
            app.SessionEnding += (_, _) => window.PrepareForSessionEnd();
            app.Run(window);
        }
        catch (Exception error) { MessageBox.Show(error.Message, "DemoMux 启动失败"); }
        finally
        {
            single.ReleaseMutex();
            // Start the registered uninstaller only after owned services and the app mutex close.
            if (InstalledApplication.PendingUninstall is { } uninstall)
            {
                installerGuard.Dispose();
                try { Process.Start(new ProcessStartInfo(uninstall) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception error) { MessageBox.Show("无法打开卸载程序，请从 Windows 已安装的应用重试。\n" + error.Message, "DemoMux"); }
            }
        }
    }
}
