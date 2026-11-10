using System.ComponentModel;
using System.Windows;

namespace DemoMux;

internal sealed partial class MainWindow
{
    private ITrayIcon? tray;
    private readonly Func<ITrayIcon> trayFactory;
    private bool exitRequested;

    private ITrayIcon EnsureTray()
    {
        if (tray is not null) return tray;
        tray = trayFactory();
        tray.OpenRequested += RestoreFromTray;
        tray.SettingsRequested += () => { RestoreFromTray(); OpenSettings(); };
        tray.ExitRequested += RequestExit;
        return tray;
    }
    private void InitializeTray()
    {
        try { var icon = EnsureTray(); if (catalog.CloseToTray && !icon.Show()) { progress = "系统托盘暂不可用，主窗口将保持打开。"; Render(); } }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        { progress = "系统托盘暂不可用，主窗口将保持打开。"; Render(); }
    }
    internal void SetCloseToTray(bool enabled)
    {
        if (enabled && !EnsureTray().Show())
        {
            if (!catalog.CloseToTray) tray?.Hide();
            throw new Win32Exception("无法创建系统托盘图标，关闭方式未更改。");
        }
        bool previous = catalog.CloseToTray;
        catalog.CloseToTray = enabled;
        try { catalog.Save(); }
        catch { catalog.CloseToTray = previous; if (!previous) tray?.Hide(); throw; }
        if (!enabled) tray?.Hide();
    }
    internal void RestoreFromTray()
    {
        if (exitRequested) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = lastMaximized ? WindowState.Maximized : WindowState.Normal;
        Activate();
    }
    internal void RequestExit() { exitRequested = true; Close(); }
    internal void PrepareForSessionEnd() => exitRequested = true;

    private void HandleClosing(object? sender, CancelEventArgs e)
    {
        if (!exitRequested && catalog.CloseToTray)
        {
            e.Cancel = true;
            try
            {
                if (!EnsureTray().Show()) throw new Win32Exception();
                SaveWindowPreference();
                foreach (var owned in OwnedWindows.Cast<Window>().ToArray()) owned.Close();
                Hide();
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException)
            {
                notice = Notice.Error; progress = "系统托盘暂不可用，窗口已保留。请重试，或在设置中选择关闭窗口退出应用。"; Render();
            }
            return;
        }
        exitRequested = true;
        if (operation is not null) { e.Cancel = true; closing = true; operation.Cancel(); }
        else SaveWindowPreference();
    }
}
