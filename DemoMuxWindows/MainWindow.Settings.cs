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
        settings = Dialog("设置"); settings.Width = 640; settings.MinWidth = 440; settings.MinHeight = Math.Min(400, settings.MaxHeight);
        settings.Height = Math.Min(650, settings.MaxHeight); settings.SizeToContent = SizeToContent.Manual; settings.ResizeMode = ResizeMode.CanResize;
        settings.Closed += (_, _) => { settings = null; settingsPage++; };
        RenderSettings(); settings.Show();
    }
    private int settingsPage;
    private void RenderSettings()
    {
        if (settings is null) return;
        settingsPage++;
        var feedback = Label("");
        void Try(Action action) { try { action(); feedback.Text = "操作完成。"; } catch (Exception e) { feedback.Text = "操作失败：" + e.Message; } }
        Button ActionButton(string caption, Action action)
        {
            var button = Button(caption, action, always: true); button.Margin = new Thickness(0, 3, 0, 3); button.MinWidth = 120;
            button.HorizontalAlignment = HorizontalAlignment.Right; button.VerticalAlignment = VerticalAlignment.Center; return button;
        }
        UIElement ActionRow(string title, string description, UIElement action)
        {
            var details = Column(Heading(title, 14), Label(description, 11)); details.VerticalAlignment = VerticalAlignment.Center;
            return new AdaptivePair(details, action, 480) { Margin = new Thickness(0, 5, 0, 5) };
        }
        var open = new CheckBox { Content = "公网就绪后，在默认浏览器打开首页", IsChecked = catalog.OpenHome, Margin = new Thickness(0, 8, 0, 8) };
        var remember = new CheckBox { Content = "记住主窗口大小", IsChecked = catalog.RememberWindow, Margin = new Thickness(0, 8, 0, 8) };
        var closeBehavior = new ComboBox { ItemsSource = new[] { "退出应用（停止演示）", "隐藏到系统托盘（继续分享）" }, SelectedIndex = catalog.CloseToTray ? 1 : 0 };
        System.Windows.Automation.AutomationProperties.SetName(closeBehavior, "关闭主窗口时");
        System.Windows.Automation.AutomationProperties.SetAutomationId(closeBehavior, "settings-close-behavior");
        bool updatingCloseBehavior = false;
        closeBehavior.SelectionChanged += (_, _) =>
        {
            if (updatingCloseBehavior) return;
            Try(() =>
            {
                try { SetCloseToTray(closeBehavior.SelectedIndex == 1); }
                finally { updatingCloseBehavior = true; closeBehavior.SelectedIndex = catalog.CloseToTray ? 1 : 0; updatingCloseBehavior = false; }
            });
        };
        open.Click += (_, _) => Try(() => { catalog.OpenHome = open.IsChecked == true; catalog.Save(); if (openHomePreference is not null) openHomePreference.IsChecked = catalog.OpenHome; });
        remember.Click += (_, _) => Try(() => { catalog.RememberWindow = remember.IsChecked == true; catalog.Save(); });
        var closeLabel = Label("关闭主窗口时"); closeLabel.VerticalAlignment = VerticalAlignment.Center;
        var content = Column(Heading("设置", 26), Card(Column(Heading("演示偏好"), open, remember,
            new AdaptivePair(closeLabel, closeBehavior, 480),
            Label("托盘模式下分享继续运行；点击图标恢复窗口，右键可退出。图标可能收纳在任务栏 ^ 中。应用没有开机自启。", 11))));
        var components = Column(Heading("组件与存储"));
        foreach (var component in Runtime.LoadComponents())
        {
            var status = Label(component.Name + " " + component.Version + " · 正在校验…"); components.Children.Add(status);
            _ = CheckComponentAsync(component, status);
        }
        components.Children.Add(Label("固定 WebView2 " + WebViewVersion + (File.Exists(Path.Combine(AppContext.BaseDirectory, "WebView2Runtime", "msedgewebview2.exe")) ? " · 随包提供" : " · 随包运行时缺失")));
        content.Children.Add(Card(components));
        var usage = Label("正在读取私有目录占用…");
        _ = Task.Run(() => StorageBytes(Runtime.Data)).ContinueWith(task => Dispatcher.InvokeAsync(() => {
            usage.Text = task.IsFaulted ? "读取目录失败：" + task.Exception!.GetBaseException().Message : $"私有目录占用：{task.Result / 1048576.0:F1} MiB";
        }));
        var folder = ActionButton("打开数据文件夹", () => Try(() => StorageShell.Open(Runtime.Data)));
        var copyPath = ActionButton("复制路径", () => Try(() => Clipboard.SetText(Runtime.Data))); copyPath.MinWidth = 88;
        folder.Margin = new Thickness(0, 3, 8, 3);
        var storageActions = Row(folder, copyPath); storageActions.HorizontalAlignment = HorizontalAlignment.Right;
        content.Children.Add(Card(Column(Heading("数据目录", 15), Text(Runtime.Data),
            new AdaptivePair(usage, storageActions, 480),
            Label("保存项目列表、偏好和私有身份，以及组件、会话快照和说明页缓存。", 11))));
        content.Children.Add(Card(Column(Heading("支持与诊断"),
            ActionRow("诊断信息", "复制版本和运行状态，供排查问题。", ActionButton("复制诊断信息", () => Try(() => Clipboard.SetText(BuildDiagnostics(ready, gateway.Slots.Shares.Count))))),
            ActionRow("使用说明", "离线查看操作步骤和常见问题。", ActionButton("离线使用说明", OpenGuide)))));
        content.Children.Add(feedback); content.Margin = new Thickness(24);
        var done = ActionButton("完成", () => settings?.Close()); done.MinWidth = 88; done.IsCancel = true;
        ShowSettingsContent(content, Split(Row(Button("手动卸载 DemoMux…", OpenManualRemoval, always: true)), Row(done)));

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
        if (target is not null) { Reveal(target); return; }
        OpenFolder(directory);
    }
    internal static void OpenFolder(string directory)
    {
        _ = Directory.GetFileSystemEntries(directory);
        // Selecting a folder PIDL with zero children reveals it in its parent.
        // ShellExecute with a separate directory argument opens the actual contents, including empty folders.
        var result = ShellExecuteW(IntPtr.Zero, "open", directory, null, null, 1).ToInt64();
        if (result <= 32) throw new IOException("Windows Shell 无法打开数据目录（错误 " + result + "）。");
    }
    internal static void Reveal(string path)
    {
        var item = ILCreateFromPathW(path);
        if (item == IntPtr.Zero) throw new IOException("Windows Shell 无法定位目标。");
        IntPtr parent = IntPtr.Zero, children = IntPtr.Zero;
        try
        {
            parent = ILClone(item);
            if (parent == IntPtr.Zero || !ILRemoveLastID(parent)) throw new IOException("无法定位目标的父目录。");
            children = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(children, ILFindLastID(item));
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(parent, 1, children, 0));
        }
        finally { if (children != IntPtr.Zero) Marshal.FreeHGlobal(children); if (parent != IntPtr.Zero) ILFree(parent); ILFree(item); }
    }
    [DllImport("shell32.dll")] private static extern IntPtr ILClone(IntPtr item);
    [DllImport("shell32.dll")] private static extern bool ILRemoveLastID(IntPtr item);
    [DllImport("shell32.dll")] private static extern IntPtr ILFindLastID(IntPtr item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ShellExecuteW(IntPtr window, string verb, string file, string? parameters, string? directory, int show);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ILCreateFromPathW(string path);
    [DllImport("shell32.dll")] private static extern void ILFree(IntPtr pidl);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(IntPtr pidl, uint count, IntPtr children, uint flags);
}
