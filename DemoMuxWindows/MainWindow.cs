using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DemoMux;

internal sealed class MainWindow : Window
{
    private readonly Component caddyComponent;
    private readonly Component tailscaleComponent;
    private readonly string sessionDirectory;
    private readonly List<int> sessionPorts = [];
    private readonly DispatcherTimer monitor = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock localState = new();
    private readonly TextBlock publicState = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock routeState = new();
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox portField = new() { Width = 86, Padding = new Thickness(7, 4, 7, 4) };
    private readonly ComboBox ports = new() { MinWidth = 155 };
    private readonly Button startLocal, stopLocal, openLocal, applyPort, useSample;
    private readonly Button publish, stopPublic, authorize, copy;
    private ChildProcess? caddy, daemon, login, funnel;
    private CancellationTokenSource? operation;
    private Uri? authUrl, publicUrl;
    private int? projectPort;
    private bool caddyInstalled, tailscaleInstalled, sharingPending, closing, rebuildingPorts;

    public MainWindow()
    {
        var components = Runtime.LoadComponents();
        caddyComponent = components.Single(component => component.Name == "Caddy");
        tailscaleComponent = components.Single(component => component.Name == "Tailscale");
        sessionDirectory = Directory.CreateDirectory(Path.Combine(Runtime.Data, "Sessions", Guid.NewGuid().ToString("N"))).FullName;
        projectPort = Runtime.LoadPort();

        Title = "DemoMux";
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/DemoMux.ico"));
        Width = 710;
        SizeToContent = SizeToContent.Height;
        MinWidth = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        FontSize = 14;

        startLocal = MakeButton("启动本机预览", (_, _) => Begin("正在准备本机预览…", StartLocalAsync));
        stopLocal = MakeButton("停止本机预览", (_, _) => { StopSharing(); Stop(ref caddy); Refresh(); });
        openLocal = MakeButton("打开本机预览", (_, _) => OpenUrl(new Uri(Runtime.PreviewUrl)));
        applyPort = MakeButton("切换到此端口", (_, _) => ApplyPort());
        useSample = MakeButton("恢复内置示例", (_, _) => SwitchTo(null));
        publish = MakeButton("启用公网分享", (_, _) => Begin("正在准备 Tailscale…", StartPublicAsync, sharing: true));
        stopPublic = MakeButton("停止公网分享", (_, _) =>
        {
            if (sharingPending) operation?.Cancel();
            else StopSharing();
            Refresh();
        });
        authorize = MakeButton("打开授权网页", (_, _) => { if (authUrl is not null) OpenUrl(authUrl); });
        copy = MakeButton("复制链接", (_, _) =>
        {
            if (publicUrl is not { } url) return;
            try { Clipboard.SetText(url.AbsoluteUri); message.Text = "已复制公网链接。"; }
            catch (System.Runtime.InteropServices.ExternalException) { message.Text = "剪贴板正在使用，请重试。"; }
        });

        portField.Text = projectPort?.ToString() ?? "";
        AutomationProperties.SetName(portField, "项目端口");
        portField.KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Enter) { args.Handled = true; ApplyPort(); }
        };
        AutomationProperties.SetName(ports, "本次会话端口");
        ports.SelectionChanged += (_, _) =>
        {
            if (!rebuildingPorts && ports.SelectedItem is ComboBoxItem { Tag: int port })
            {
                ports.SelectedIndex = 0;
                SwitchTo(port);
            }
        };
        RebuildPorts();

        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(new TextBlock
        {
            Text = "DemoMux", FontSize = 32, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(196, 25, 58))
        });
        body.Children.Add(new TextBlock { Text = "本机项目预览与公网演示", Margin = new Thickness(0, 1, 0, 10) });
        body.Children.Add(new Separator());
        body.Children.Add(Row(Heading("本机预览"), localState));
        body.Children.Add(Row(startLocal, stopLocal, openLocal));
        body.Children.Add(Row(new TextBlock { Text = "项目端口" }, portField, applyPort, useSample));
        body.Children.Add(Row(new TextBlock { Text = "本次会话" }, ports));
        body.Children.Add(Row(routeState));
        body.Children.Add(new Separator());
        body.Children.Add(Row(Heading("公网分享"), publicState));
        body.Children.Add(Row(publish, stopPublic, authorize, copy));
        body.Children.Add(message);
        Content = body;

        Loaded += (_, _) =>
        {
            Begin("正在检查依赖…", async token =>
            {
                caddyInstalled = await caddyComponent.IsInstalledAsync(token);
                tailscaleInstalled = await tailscaleComponent.IsInstalledAsync(token);
            });
            monitor.Tick += (_, _) => Monitor();
            monitor.Start();
        };
        Closing += (_, args) =>
        {
            if (operation is null) return;
            args.Cancel = true;
            closing = true;
            operation.Cancel();
        };
        Closed += (_, _) =>
        {
            monitor.Stop();
            StopSharing();
            Stop(ref caddy);
            Runtime.TryDelete(sessionDirectory);
        };
        Refresh();
    }

    private static Button MakeButton(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5) };
        button.Click += click;
        return button;
    }

    private static TextBlock Heading(string title) => new() { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold };

    private static FrameworkElement Row(params FrameworkElement[] controls)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 5, 0, 5) };
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 8, 3);
            control.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(control);
        }
        return row;
    }

    private void Refresh()
    {
        bool busy = operation is not null;
        bool running = caddy?.Running == true;
        localState.Text = running ? "运行中" : caddyInstalled ? "组件就绪 · 已停止" : "首次启动时下载 Caddy";
        publicState.Text = publicUrl?.AbsoluteUri ?? (authUrl is not null ? "等待网页授权" :
            sharingPending ? "正在连接" : tailscaleInstalled ? "组件就绪 · 已停止" : "按需下载 Tailscale");
        routeState.Text = $"当前展示 {(projectPort is { } p ? $"项目端口 {p}" : "内置示例")} · 127.0.0.1:8081";
        startLocal.IsEnabled = !busy && !running;
        stopLocal.IsEnabled = !busy && running;
        openLocal.IsEnabled = !busy && running;
        applyPort.IsEnabled = portField.IsEnabled = !busy;
        ports.IsEnabled = !busy && sessionPorts.Count > 0;
        useSample.IsEnabled = !busy && projectPort is not null;
        publish.IsEnabled = !busy && running && publicUrl is null;
        stopPublic.IsEnabled = sharingPending || (!busy && publicUrl is not null);
        authorize.IsEnabled = authUrl is not null;
        copy.IsEnabled = publicUrl is not null;
    }

    private async void Begin(string progress, Func<CancellationToken, Task> action, bool sharing = false)
    {
        if (operation is not null) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        sharingPending = sharing;
        message.Text = progress;
        Refresh();
        try
        {
            await action(cancellation.Token);
            message.Text = "手动启动。关闭 DemoMux 会停止本机预览和公网分享。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { message.Text = "操作已取消。"; }
        catch (Exception error) { message.Text = SafeMessage(error.Message); }
        finally
        {
            operation = null;
            sharingPending = false;
            if (closing) Close();
            else Refresh();
        }
    }

    private async Task StartLocalAsync(CancellationToken token)
    {
        if (projectPort is { } port) await Runtime.CheckProjectAsync(port, token);
        if (!caddyInstalled) message.Text = "正在下载并校验 Caddy…";
        await caddyComponent.InstallAsync(token);
        caddyInstalled = true;
        caddy = await Runtime.StartCaddyAsync(caddyComponent, sessionDirectory, projectPort, token);
    }

    private void ApplyPort()
    {
        if (!int.TryParse(portField.Text, out var port) || !Runtime.ValidProjectPort(port))
        {
            message.Text = "请输入 1 到 65535 的项目端口。8081 是 DemoMux 的预览入口。";
            return;
        }
        SwitchTo(port);
    }

    private void SwitchTo(int? next)
    {
        if (projectPort == next) return;
        Begin("正在切换展示来源…", async token =>
        {
            if (next is { } port) await Runtime.CheckProjectAsync(port, token);
            var running = caddy?.Running == true;
            try
            {
                if (running)
                {
                    Stop(ref caddy);
                    caddy = await Runtime.StartCaddyAsync(caddyComponent, sessionDirectory, next, token);
                }
                Runtime.SavePort(next);
            }
            catch
            {
                if (running && !token.IsCancellationRequested)
                {
                    Stop(ref caddy);
                    try { caddy = await Runtime.StartCaddyAsync(caddyComponent, sessionDirectory, projectPort, token); }
                    catch (Exception error)
                    {
                        StopSharing();
                        throw new IOException("切换失败，原预览也未能恢复。请重新启动本机预览。", error);
                    }
                }
                throw;
            }
            projectPort = next;
            portField.Text = next?.ToString() ?? "";
            if (next is { } selected)
            {
                sessionPorts.Remove(selected);
                sessionPorts.Insert(0, selected);
                RebuildPorts();
            }
        });
    }

    private void RebuildPorts()
    {
        rebuildingPorts = true;
        ports.Items.Clear();
        ports.Items.Add(new ComboBoxItem
        {
            Content = sessionPorts.Count == 0 ? "暂无切换记录" : "快速切换端口", IsEnabled = false
        });
        foreach (var port in sessionPorts) ports.Items.Add(new ComboBoxItem { Content = port.ToString(), Tag = port });
        ports.SelectedIndex = 0;
        rebuildingPorts = false;
    }

    private async Task StartPublicAsync(CancellationToken token)
    {
        try
        {
            if (!tailscaleInstalled) message.Text = "正在下载并校验 Tailscale…";
            await tailscaleComponent.InstallAsync(token);
            tailscaleInstalled = true;
            var state = Directory.CreateDirectory(Path.Combine(Runtime.Data, "Tailscale")).FullName;
            var pipe = "demomux-" + Guid.NewGuid().ToString("N");
            var socket = @"--socket=\\.\pipe\" + pipe;
            var cli = tailscaleComponent.Executable("tailscale.exe");
            daemon = ChildProcess.Start(tailscaleComponent.Executable("tailscaled.exe"),
                "--windows-mode=dev", "--tun=userspace-networking", socket, $"--statedir={state}", "--port=0");
            var server = daemon;
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (!server.Running || attempt >= 30) throw new IOException("Tailscale 启动失败。 " + server.Output);
                if (await Runtime.OwnsPipeAsync(pipe, server.Id, token)) break;
                await Task.Delay(100, token);
            }

            var hostnamePath = Path.Combine(state, "demomux-hostname.txt");
            if (!File.Exists(hostnamePath))
                File.WriteAllText(hostnamePath, "demomux-" + Guid.NewGuid().ToString("N")[..8]);
            var hostname = File.ReadAllText(hostnamePath);
            login = ChildProcess.Start(cli, socket, "up", "--accept-dns=false", "--accept-routes=false",
                $"--hostname={hostname}", "--timeout=5m");
            while (login.Running)
            {
                ReadAuthorization(login.Output, "请在浏览器中登录 Tailscale。");
                await Task.Delay(180, token);
            }
            await login.WaitAsync(token);
            if (login.ExitCode != 0) throw new IOException("Tailscale 登录未完成。 " + login.Output);
            Stop(ref login);
            authUrl = null;
            Refresh();

            if (!await Runtime.OwnsPipeAsync(pipe, server.Id, token)) throw new IOException("Tailscale 本地连接已关闭。");
            funnel = ChildProcess.Start(cli, socket, "funnel", "--https=443", Runtime.PreviewUrl);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            while (funnel.Running && server.Running)
            {
                var output = funnel.Output;
                if (output.Contains("Available on the internet:", StringComparison.Ordinal)
                    && FindUrl(output, url => url.Host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase)) is { } address)
                {
                    publicUrl = address;
                    authUrl = null;
                    return;
                }
                ReadAuthorization(output, "请在浏览器中确认 Funnel 授权。");
                try { await Task.Delay(180, deadline.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { throw new IOException("Funnel 启用超时，请重试。"); }
            }
            throw new IOException("Funnel 未启动。 " + funnel.Output);
        }
        catch { StopSharing(); throw; }
    }

    private void ReadAuthorization(string output, string prompt)
    {
        var link = FindUrl(output, url => url.Host is "login.tailscale.com" or "console.tailscale.com");
        if (link is null || link == authUrl) return;
        authUrl = link;
        message.Text = prompt;
        Refresh();
    }

    private static Uri? FindUrl(string text, Func<Uri, bool> match)
    {
        foreach (Match candidate in Regex.Matches(text, "https://[^\\s\"<>]+"))
            if (Uri.TryCreate(candidate.Value.TrimEnd('.', ',', ')'), UriKind.Absolute, out var url) && match(url)) return url;
        return null;
    }

    private void OpenUrl(Uri url)
    {
        try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { message.Text = "无法打开浏览器，请检查系统的默认浏览器设置。"; }
    }

    private static string SafeMessage(string text)
    {
        var redacted = Regex.Replace(text, @"https://(?:login|console)\.tailscale\.com/\S+", "[授权链接]");
        return redacted.Length > 480 ? redacted[..480] + "…" : redacted;
    }

    private void Monitor()
    {
        if (operation is not null) return;
        if (caddy is { Running: false })
        {
            Stop(ref caddy);
            StopSharing();
            message.Text = "Caddy 已退出，可重新启动本机预览。";
        }
        else if (funnel is { Running: false } || daemon is { Running: false })
        {
            StopSharing();
            message.Text = "公网分享已断开，可重新启用。";
        }
        Refresh();
    }

    private static void Stop(ref ChildProcess? child)
    {
        var owned = child;
        child = null;
        owned?.Dispose();
    }

    private void StopSharing()
    {
        Stop(ref funnel);
        Stop(ref login);
        Stop(ref daemon);
        authUrl = publicUrl = null;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(false, @"Local\DemoMux." + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value);
        try
        {
            if (!single.WaitOne(0))
            {
                MessageBox.Show("DemoMux 已经运行。", "DemoMux");
                return;
            }
        }
        catch (AbandonedMutexException) { }

        try { new Application().Run(new MainWindow()); }
        catch (Exception error) { MessageBox.Show(error.Message, "DemoMux 启动失败"); }
        finally { single.ReleaseMutex(); }
    }
}
