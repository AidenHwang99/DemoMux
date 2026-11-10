using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DemoMux;

internal sealed record MonitorArea(string Device, Rect Work, double Scale, bool Primary);
internal static class WindowPlacement
{
    internal static Rect Resolve(WindowPreference value, MonitorArea monitor)
    {
        var area = monitor.Work; double scale = monitor.Scale;
        double width = Math.Clamp(value.Width * scale, Math.Min(720 * scale, area.Width), area.Width);
        double height = Math.Clamp(value.Height * scale, Math.Min(560 * scale, area.Height), area.Height);
        double x = value.OffsetX is { } ox && double.IsFinite(ox) ? area.Left + ox * scale : value.Left * scale;
        double y = value.OffsetY is { } oy && double.IsFinite(oy) ? area.Top + oy * scale : value.Top * scale;
        return new Rect(Math.Clamp(x, area.Left, area.Right - width), Math.Clamp(y, area.Top, area.Bottom - height), width, height);
    }
    internal static MonitorArea Choose(IReadOnlyList<MonitorArea> monitors, string? device) =>
        monitors.FirstOrDefault(m => m.Device == device) ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors.First();
    internal static void Restore(Window window, WindowPreference value)
    {
        var monitor = Choose(Monitors(), value.Monitor);
        var rect = Resolve(value, monitor);
        if (!SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, 0x14))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (value.Maximized) window.WindowState = WindowState.Maximized;
    }
    internal static WindowPreference? Capture(Window window)
    {
        if (window.WindowState != WindowState.Normal) return null;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return null;
        var monitor = ReadMonitor(MonitorFromWindow(handle, 2));
        double scale = GetDpiForWindow(handle) / 96.0;
        if (scale <= 0) scale = monitor.Scale;
        return new(rect.Left / scale, rect.Top / scale, (rect.Right - rect.Left) / scale, (rect.Bottom - rect.Top) / scale, false,
            monitor.Device, (rect.Left - monitor.Work.Left) / scale, (rect.Top - monitor.Work.Top) / scale);
    }
    private static IReadOnlyList<MonitorArea> Monitors()
    {
        var result = new List<MonitorArea>();
        MonitorCallback callback = (IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data) => { result.Add(ReadMonitor(monitor)); return true; };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) || result.Count == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }
    private static MonitorArea ReadMonitor(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
        if (!GetMonitorInfoW(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        double scale = GetDpiForMonitor(monitor, 0, out uint x, out _) == 0 ? x / 96.0 : 1;
        var work = info.Work;
        return new(info.Device, new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), scale, (info.Flags & 1) != 0);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public int Size; public NativeRect Monitor, Work; public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
