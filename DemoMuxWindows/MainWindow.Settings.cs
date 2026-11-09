using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace DemoMux;

internal sealed partial class MainWindow
{
    private Window? settings;
    private CheckBox? openHomePreference;
    private static string ProductVersion => typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
    private static string WebViewVersion
    {
        get
        {
            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("DemoMux.webview2-runtime.json")!;
            using var document = System.Text.Json.JsonDocument.Parse(stream);
            return document.RootElement.GetProperty("version").GetString()!;
        }
    }
    internal static string BuildDiagnostics(bool publicReady, int publications) =>
        $"DemoMux {ProductVersion}\nWindows {Environment.OSVersion.Version}\n架构 {RuntimeInformation.OSArchitecture}\n.NET {Environment.Version}\n" +
        string.Join("\n", Runtime.LoadComponents().Select(c => c.Name + " " + c.Version)) +
        $"\nWebView2 {WebViewVersion}\n连接：{(publicReady ? "公网已验证" : "公网未就绪")}\n发布数量：{publications}";
    private void OpenSettings()
    {
        if (settings is not null) { settings.Activate(); return; }
        settings = Dialog("设置"); settings.Width = 640; settings.Height = 650;
        settings.Closed += (_, _) => settings = null;
        var feedback = Label("");
        void Try(Action action) { try { action(); feedback.Text = "操作完成。"; } catch (Exception e) { feedback.Text = "操作失败：" + e.Message; } }
        var open = new CheckBox { Content = "公网就绪后，在默认浏览器打开首页", IsChecked = catalog.OpenHome, Margin = new Thickness(0, 8, 0, 8) };
        var remember = new CheckBox { Content = "记住主窗口大小", IsChecked = catalog.RememberWindow, Margin = new Thickness(0, 8, 0, 8) };
        open.Click += (_, _) => Try(() => { catalog.OpenHome = open.IsChecked == true; catalog.Save(); if (openHomePreference is not null) openHomePreference.IsChecked = catalog.OpenHome; });
        remember.Click += (_, _) => Try(() => { catalog.RememberWindow = remember.IsChecked == true; catalog.Save(); });
        var content = Column(Heading("设置", 26), Card(Column(Heading("演示偏好"), open, remember,
            Label("应用没有开机自启。关闭主窗口会停止 DemoMux 私有组件。"))), Heading("组件与存储"));
        foreach (var component in Runtime.LoadComponents())
        {
            var status = Label(component.Name + " " + component.Version + " · 正在校验…"); content.Children.Add(status);
            _ = CheckComponentAsync(component, status);
        }
        content.Children.Add(Label("固定 WebView2 " + WebViewVersion + (File.Exists(Path.Combine(AppContext.BaseDirectory, "WebView2Runtime", "msedgewebview2.exe")) ? " · 随包提供" : " · 随包运行时缺失")));
        var usage = Label("正在读取私有目录占用…"); content.Children.Add(usage);
        _ = Task.Run(() => StorageBytes(Runtime.Data)).ContinueWith(task => Dispatcher.InvokeAsync(() => {
            usage.Text = task.IsFaulted ? "读取目录失败：" + task.Exception!.GetBaseException().Message : $"私有目录占用：{task.Result / 1048576.0:F1} MiB";
        }));
        content.Children.Add(Text(Runtime.Data));
        content.Children.Add(Row(Button("打开数据文件夹", () => Try(() => StorageShell.Open(Runtime.Data)), always: true),
            Button("复制路径", () => Try(() => Clipboard.SetText(Runtime.Data)), always: true)));
        content.Children.Add(Label("Components：下载组件；projects-v1.json：项目和偏好；Tailscale\\PrivateState：私有身份；CaddyData：网关数据；Sessions：会话快照；WebView2：说明页缓存。"));
        content.Children.Add(Heading("支持与诊断"));
        content.Children.Add(Row(Button("复制诊断信息", () => Try(() => Clipboard.SetText(BuildDiagnostics(ready, gateway.Slots.Shares.Count))), always: true), Button("离线使用说明", OpenGuide, always: true)));
        content.Children.Add(feedback); content.Margin = new Thickness(24);
        settings.Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        settings.Show();
    }
    private static async Task CheckComponentAsync(Component component, TextBlock label)
    {
        try { label.Text = component.Name + " " + component.Version + (await component.IsInstalledAsync(CancellationToken.None) ? " · 已下载并校验" : " · 未下载或校验失败"); }
        catch (Exception e) { label.Text = component.Name + " · 读取失败：" + e.Message; }
    }
    internal static long StorageBytes(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("数据目录包含链接，无法安全统计。");
        long bytes = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("目录包含链接，无法完整统计。");
            bytes += (attributes & FileAttributes.Directory) != 0 ? StorageBytes(entry) : new FileInfo(entry).Length;
        }
        return bytes;
    }
    private WindowPreference? lastNormalWindow;
    private bool lastMaximized;
    private void TrackWindowPreference()
    {
        if (WindowState == WindowState.Minimized) return;
        lastMaximized = WindowState == WindowState.Maximized;
        if (!lastMaximized) lastNormalWindow = WindowPlacement.Capture(this) ?? lastNormalWindow;
    }
    private void RestoreWindowPreference()
    {
        if (!catalog.RememberWindow || catalog.Window is not { } value) return;
        if (!double.IsFinite(value.Width) || !double.IsFinite(value.Height) || !double.IsFinite(value.Left) || !double.IsFinite(value.Top)) return;
        lastNormalWindow = value;
        lastMaximized = value.Maximized;
        try { WindowPlacement.Restore(this, value); }
        catch (System.ComponentModel.Win32Exception e) { MessageBox.Show("无法恢复窗口位置，将使用默认位置。\n" + e.Message, "DemoMux"); }
    }
    private void SaveWindowPreference()
    {
        if (!catalog.RememberWindow) return;
        TrackWindowPreference();
        if (lastNormalWindow is not { } value) return;
        catalog.Window = value with { Maximized = lastMaximized };
        try { catalog.Save(); } catch (Exception e) { MessageBox.Show(e.Message, "窗口偏好保存失败"); }
    }

}

internal static class StorageShell
{
    internal static void Open(string directory)
    {
        // Enumerating first distinguishes missing/inaccessible directories from empty ones.
        var entries = Directory.GetFileSystemEntries(directory);
        var target = entries.FirstOrDefault(p => Path.GetFileName(p) == "Components") ?? entries.FirstOrDefault();
        var pidl = ILCreateFromPathW(target ?? directory);
        if (pidl == IntPtr.Zero) throw new IOException("Windows Shell 无法定位数据目录。");
        try { Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0)); }
        finally { ILFree(pidl); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ILCreateFromPathW(string path);
    [DllImport("shell32.dll")] private static extern void ILFree(IntPtr pidl);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(IntPtr pidl, uint count, IntPtr children, uint flags);
}
