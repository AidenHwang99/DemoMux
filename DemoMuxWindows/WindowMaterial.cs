using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
namespace DemoMux;
internal static class WindowMaterial
{
    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        int dark = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        // The explicit WPF surface remains readable when system transparency is off.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || SystemParameters.HighContrast) return;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        if (key?.GetValue("EnableTransparency") is int enabled && enabled == 0) return;
        int backdrop = 2;
        _ = DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
