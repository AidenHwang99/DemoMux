using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DemoMux;

internal sealed partial class MainWindow
{
    private void ShowSettingsContent(UIElement content, UIElement actions)
    {
        if (settings is null) return;
        var root = new DockPanel();
        var footer = new Border { Child = actions, Padding = new Thickness(16, 10, 16, 10), Background = Theme.Surface };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        settings.Content = root;
    }
    private async void OpenManualRemoval()
    {
        if (settings is null) return;
        var window = settings; int page = ++settingsPage;
        ShowSettingsContent(Column(Heading("手动卸载", 26), Label("正在只读核对当前包文件…")), Row(Button("返回设置", RenderSettings, always: true)));
        try
        {
            var removal = await Task.Run(() => ManualRemoval.Inspect(AppContext.BaseDirectory, Runtime.Data));
            if (settings != window || settingsPage != page) return;
            RenderManualRemoval(removal);
        }
        catch (Exception e)
        {
            if (settings == window && settingsPage == page)
                ShowSettingsContent(Column(Heading("无法准备卸载说明"), Text(e.Message)), Row(Button("返回设置", RenderSettings, always: true)));
        }
    }
    private void RenderManualRemoval(ManualRemoval removal)
    {
        var feedback = Accent("");
        void Try(Action action) { try { action(); feedback.Text = "操作完成。未删除任何文件。"; } catch (Exception e) { feedback.Text = "操作失败：" + e.Message; } }
        var clear = new CheckBox { Content = "同时手动清除项目列表、本机登录和偏好", IsChecked = true, Margin = new Thickness(0, 10, 0, 10) };
        var scope = Label("");
        void Update() => scope.Text = clear.IsChecked == true
            ? "退出后，可手动清理下方数据位置中的 DemoMux 组件、缓存、Tailscale 私有身份及 projects-v1.json。"
            : "退出后，只清理 Components、Sessions、CaddyData、WebView2。保留整个 Tailscale 目录和 projects-v1.json，以便重新解压后沿用登录和偏好。";
        clear.Checked += (_, _) => Update(); clear.Unchecked += (_, _) => Update(); Update();
        string WriteReport() => removal.WriteInstructions(clear.IsChecked == true, Path.Combine(Path.GetTempPath(), "DemoMux-ManualRemoval-" + Guid.NewGuid().ToString("N")));
        var body = Column(Heading("手动卸载 DemoMux", 26), Text("由你在 Explorer 中选择文件并送入回收站。此入口只生成说明、打开位置和退出应用，不会自动删除程序或持久数据。"),
            Card(Column(Heading("1. 当前程序副本", 16), Text(removal.ApplicationDirectory),
                Label(removal.DedicatedPackage ? "当前文件清单校验通过；删除前请再次确认目录内没有新加入的原始文件。" : "未确认是专用程序目录。请勿删除整个目录；详细说明列出可确认的包文件和应保留项。"),
                Row(Button("定位程序文件夹", () => Try(() => StorageShell.Reveal(removal.ApplicationDirectory)), always: true), Button("复制程序路径", () => Try(() => Clipboard.SetText(removal.ApplicationDirectory)), always: true)))),
            Card(Column(Heading("2. 本地数据", 16), Text(removal.DataDirectory), clear, scope,
                Row(Button("打开数据位置", () => Try(() => StorageShell.OpenFolder(removal.DataDirectory)), always: true), Button("复制数据路径", () => Try(() => Clipboard.SetText(removal.DataDirectory)), always: true)))),
            Label("保留原始 HTML、导入项目文件夹、外部本机服务、其他应用副本及系统独立组件。不删除在线账号或云端设备记录，不清空回收站。"),
            Label("点击下方按钮后，卸载说明会独立打开，并打开两个位置；应用退出后仍可阅读说明。DemoMux 退出后再手动清理；若目录混放文件，请按说明逐项处理。"),
            feedback);
        if (removal.Warning is { } warning) body.Children.Add(Accent(warning));
        body.Children.Add(Button("打开详细卸载说明", () => Try(() => OpenLocalReport(WriteReport())), always: true));
        body.Margin = new Thickness(24);
        var exit = Button("打开位置并退出 DemoMux", () => { }, always: true, primary: true);
        exit.Click += (_, _) =>
        {
            exit.IsEnabled = false;
            try
            {
                var report = WriteReport();
                // Explicit exit bypasses the close-to-tray preference and waits for active operations.
                OpenLocalReport(report);
                StorageShell.Reveal(removal.ApplicationDirectory);
                StorageShell.OpenFolder(removal.DataDirectory);
                feedback.Text = "说明和位置已打开，正在等待当前操作取消并退出。请在应用完全退出后再清理文件。";
                RequestExit();
            }
            catch (Exception e) { feedback.Text = "无法完成定位，应用保持打开：" + e.Message; exit.IsEnabled = true; }
        };
        ShowSettingsContent(body, Row(Button("返回设置", RenderSettings, always: true), exit));
    }
    private static void OpenLocalReport(string path) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
}
