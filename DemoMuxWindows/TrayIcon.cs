using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DemoMux;

internal interface ITrayIcon : IDisposable
{
    event Action? OpenRequested;
    event Action? SettingsRequested;
    event Action? ExitRequested;
    bool Show();
    void Hide();
}

internal sealed class TrayIcon : ITrayIcon
{
    private const int Callback = 0x8001;
    private const string ActivationHost = "DemoMux background host v1";
    private readonly HwndSource source;
    private readonly IntPtr icon;
    private readonly bool ownsIcon, listenForActivation;
    private readonly uint taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private bool shown, wanted, disposed;
    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    internal TrayIcon(bool listenForActivation)
    {
        this.listenForActivation = listenForActivation;
        // A hidden top-level window receives Explorer's TaskbarCreated broadcast.
        source = new HwndSource(new HwndSourceParameters(listenForActivation ? ActivationHost : "DemoMux isolated notification icon") { WindowStyle = unchecked((int)0x80000000), Width = 0, Height = 0 });
        source.AddHook(Message);
        ExtractIconExW(Environment.ProcessPath!, 0, out var large, out var small, 1);
        icon = small != IntPtr.Zero ? small : large;
        ownsIcon = icon != IntPtr.Zero;
        if (large != IntPtr.Zero && large != icon) DestroyIcon(large);
        if (icon == IntPtr.Zero) icon = LoadIconW(IntPtr.Zero, new IntPtr(32512));
    }

    public bool Show()
    {
        if (disposed) return false;
        wanted = true;
        var data = Data();
        if (shown && Shell_NotifyIconW(1, ref data)) return true;
        shown = Shell_NotifyIconW(0, ref data);
        // Keep legacy callback handling if the shell does not support version 4.
        if (shown) { data.Version = 4; Shell_NotifyIconW(4, ref data); }
        return shown;
    }

    public void Hide()
    {
        wanted = false;
        if (!shown) return;
        var data = Data(); Shell_NotifyIconW(2, ref data); shown = false;
    }

    private IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (disposed) return IntPtr.Zero;
        if (taskbarCreated != 0 && (uint)message == taskbarCreated)
        {
            shown = false;
            if (wanted && !Show()) Post(() => OpenRequested?.Invoke());
        }
        else if (listenForActivation && ActivationMessage != 0 && (uint)message == ActivationMessage)
        {
            handled = true; Post(() => OpenRequested?.Invoke());
        }
        else if (message == Callback)
        {
            handled = true;
            int action = (int)(lParam.ToInt64() & 0xffff);
            if (action is 0x400 or 0x401 or 0x202 or 0x203) Post(() => OpenRequested?.Invoke());
            else if (action is 0x7b or 0x205) Post(Menu);
        }
        return IntPtr.Zero;
    }

    private void Post(Action action) => source.Dispatcher.BeginInvoke(() => { if (!disposed) action(); });
    private void Menu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) { OpenRequested?.Invoke(); return; }
        try
        {
            AppendMenuW(menu, 0, 1, "打开 DemoMux");
            AppendMenuW(menu, 0, 2, "设置");
            AppendMenuW(menu, 0x800, 0, null);
            AppendMenuW(menu, 0, 3, "退出 DemoMux");
            SetMenuDefaultItem(menu, 1, false);
            GetCursorPos(out var point); SetForegroundWindow(source.Handle);
            uint selected = TrackPopupMenuEx(menu, 0x102, point.X, point.Y, source.Handle, IntPtr.Zero);
            PostMessageW(source.Handle, 0, IntPtr.Zero, IntPtr.Zero);
            if (selected == 1) OpenRequested?.Invoke();
            else if (selected == 2) SettingsRequested?.Invoke();
            else if (selected == 3) ExitRequested?.Invoke();
        }
        finally { DestroyMenu(menu); }
    }

    private NotifyData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyData>(), Window = source.Handle, Id = 1,
        Flags = 1 | 2 | 4 | 0x80, CallbackMessage = Callback, Icon = icon,
        Tip = "DemoMux · 后台运行", Info = "", InfoTitle = ""
    };

    public void Dispose()
    {
        if (disposed) return;
        Hide(); disposed = true; source.RemoveHook(Message); source.Dispose();
        if (ownsIcon) DestroyIcon(icon);
    }

    private static readonly uint ActivationMessage = RegisterWindowMessageW("DemoMux.ShowMainWindow.v1");
    internal static bool ActivateExisting()
    {
        var existing = FindWindowW(null, ActivationHost);
        return existing != IntPtr.Zero && ActivationMessage != 0 && PostMessageW(existing, ActivationMessage, IntPtr.Zero, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint operation, ref NotifyData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string? className, string name);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, bool byPosition);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
