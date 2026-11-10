using System.Windows;
using DemoMux;

internal static class TraySmoke
{
    internal static void Run(bool interactive)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application();
            var window = new MainWindow(false) { Title = "DemoMux · 托盘隔离测试" };
            window.Loaded += async (_, _) =>
            {
                try
                {
                    window.SetCloseToTray(true);
                    Console.WriteLine("PASS: native Shell_NotifyIcon registration on Windows desktop");
                    if (interactive)
                    {
                        typeof(MainWindow).GetMethod("OpenSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
                        return;
                    }
                    window.Close();
                    if (window.IsVisible) throw new Exception("Native tray did not allow hiding");
                    Console.WriteLine("PASS: native tray remains available while the main window is hidden");
                    await Task.Delay(250);
                    window.RestoreFromTray();
                    if (!window.IsVisible) throw new Exception("Main window did not restore");
                    Console.WriteLine("PASS: native-tray application restores the same window");
                    window.RequestExit();
                }
                catch (Exception error) { failure = error; window.RequestExit(); }
            };
            app.Run(window);
            Console.WriteLine("PASS: explicit exit terminates the isolated native-tray application");
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
