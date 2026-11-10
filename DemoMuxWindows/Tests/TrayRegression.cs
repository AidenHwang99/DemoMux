using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using DemoMux;

internal static class TrayRegression
{
    private sealed class Icon : ITrayIcon
    {
        public event Action? OpenRequested;
        public event Action? SettingsRequested;
        public event Action? ExitRequested;
        internal bool Available = true, Visible, Disposed;
        public bool Show() => Visible = Available;
        public void Hide() => Visible = false;
        public void Dispose() { Disposed = true; Hide(); }
        internal void Open() => OpenRequested?.Invoke();
        internal void Settings() => SettingsRequested?.Invoke();
        internal void Exit() => ExitRequested?.Invoke();
    }
    internal static void Run(Action<bool, string> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _ = dispatcher.InvokeAsync(async () =>
            {
                MainWindow? window = null;
                try
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var icon = new Icon();
                    window = new MainWindow(false, () => icon); window.Show();
                    var catalog = (Catalog)typeof(MainWindow).GetField("catalog", flags)!.GetValue(window)!;
                    var gateway = (Gateway)typeof(MainWindow).GetField("gateway", flags)!.GetValue(window)!;
                    check(!catalog.CloseToTray, "existing catalog defaults to closing the application rather than silently enabling background mode");
                    typeof(MainWindow).GetMethod("OpenSettings", flags)!.Invoke(window, null);
                    var settings = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "DemoMux · 设置");
                    var choice = Descendants(settings).OfType<ComboBox>().Single(c => AutomationProperties.GetAutomationId(c) == "settings-close-behavior");
                    settings.UpdateLayout();
                    Button SettingsButton(string caption) => Descendants(settings).OfType<Button>().Single(b => b.Content as string == caption);
                    var done = SettingsButton("完成"); var uninstall = SettingsButton("手动卸载 DemoMux…");
                    var diagnostic = SettingsButton("复制诊断信息"); var guide = SettingsButton("离线使用说明");
                    check(done.TranslatePoint(new Point(), settings).X > uninstall.TranslatePoint(new Point(), settings).X + uninstall.ActualWidth,
                        "settings footer separates uninstall on the left from Done on the right");
                    check(Math.Abs(diagnostic.TranslatePoint(new Point(), settings).X - guide.TranslatePoint(new Point(), settings).X) < 1 && diagnostic.TranslatePoint(new Point(), settings).X > settings.ActualWidth / 2,
                        "settings support actions share a right-aligned column in a regular window");
                    settings.Width = 440; settings.Height = 400; settings.UpdateLayout();
                    check(new[] { done, uninstall, diagnostic, guide, SettingsButton("打开数据文件夹"), SettingsButton("复制路径") }
                        .All(b => b.TranslatePoint(new Point(), settings).X >= 0 && b.TranslatePoint(new Point(b.ActualWidth, 0), settings).X <= settings.ActualWidth)
                        && done.TranslatePoint(new Point(0, done.ActualHeight), settings).Y < settings.ActualHeight,
                        "narrow short settings reflows actions within the window and preserves a visible fixed footer");
                    choice.SelectedIndex = 1;
                    check(catalog.CloseToTray && Catalog.Load().CloseToTray && icon.Visible, "settings enables and persists close-to-tray only after making an icon available");

                    var project = new Project { Name = "Tray fixture", Slug = "tray-fixture" }; catalog.Projects = [project];
                    var snapshot = Directory.CreateTempSubdirectory("demomux-tray-").FullName;
                    gateway.Active.Add(project.Id, new(snapshot, "index.html", 0));
                    gateway.Slots.Reserve(project, false);
                    var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    bool canceled = false, closed = false;
                    window.Closed += (_, _) => closed = true;
                    window.Run("托盘隔离任务", async token =>
                    {
                        using var registration = token.Register(() => { canceled = true; active.TrySetCanceled(token); });
                        await active.Task;
                    });
                    window.Close();
                    check(!closed && !window.IsVisible && !canceled && !icon.Disposed, "closing in tray mode hides the window without canceling the active operation or disposing the tray");
                    check(Directory.Exists(snapshot) && gateway.Active.ContainsKey(project.Id) && gateway.Slots.Shares.Count == 1,
                        "hiding preserves project snapshots, active gateway entries and publication reservations");
                    check(!window.OwnedWindows.Cast<Window>().Any(), "hiding the main window also closes owned settings windows");
                    icon.Open();
                    check(window.IsVisible && !closed && !canceled && gateway.Slots.Shares.Count == 1, "tray open restores the same running window and publication state");
                    window.WindowState = WindowState.Minimized; window.Close(); icon.Settings();
                    check(window.IsVisible && window.WindowState != WindowState.Minimized && window.OwnedWindows.Cast<Window>().Any(w => w.Title == "DemoMux · 设置"),
                        "tray settings restores a minimized hidden window and opens its settings");
                    window.Close(); icon.Exit();
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (!closed && DateTime.UtcNow < deadline) await Task.Delay(10);
                    check(closed && canceled && icon.Disposed && !icon.Visible, "explicit tray exit bypasses the preference, cancels pending work and removes the icon");
                    check(!Directory.Exists(snapshot) && gateway.Active.Count == 0 && gateway.Slots.Shares.Count == 0,
                        "explicit exit releases owned snapshots, gateway entries and publication slots");

                    icon = new Icon { Available = false };
                    window = new MainWindow(false, () => icon); window.Show();
                    catalog = (Catalog)typeof(MainWindow).GetField("catalog", flags)!.GetValue(window)!;
                    window.Close();
                    check(window.IsVisible && !icon.Disposed, "a missing system tray cannot strand a running application in an invisible window");
                    typeof(MainWindow).GetMethod("OpenSettings", flags)!.Invoke(window, null);
                    settings = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "DemoMux · 设置");
                    choice = Descendants(settings).OfType<ComboBox>().Single(c => AutomationProperties.GetAutomationId(c) == "settings-close-behavior");
                    choice.SelectedIndex = 0;
                    check(!catalog.CloseToTray && !Catalog.Load().CloseToTray && !icon.Visible, "settings can disable background mode and persist normal exit behavior");
                    choice.SelectedIndex = 1;
                    check(!catalog.CloseToTray && choice.SelectedIndex == 0 && Descendants(settings).OfType<TextBlock>().Any(t => t.Text.Contains("无法创建系统托盘图标")),
                        "failed tray creation rolls the setting back and explains the failure");
                    closed = false; window.Closed += (_, _) => closed = true; window.Close();
                    check(closed && icon.Disposed, "normal window close exits and cleans up when close-to-tray is disabled");

                    icon = new Icon(); window = new MainWindow(false, () => icon); window.Show(); window.SetCloseToTray(true);
                    closed = false; window.Closed += (_, _) => closed = true; window.PrepareForSessionEnd(); window.Close();
                    check(closed && icon.Disposed, "Windows session end bypasses close-to-tray instead of leaving a hidden process");
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    if (window is not null)
                    {
                        try { window.RequestExit(); } catch (InvalidOperationException) { }
                    }
                    var catalog = Catalog.Load(); catalog.CloseToTray = false; catalog.Save();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(20))) throw new Exception("Tray lifecycle regression timed out");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
