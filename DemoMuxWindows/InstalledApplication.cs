using System.IO;
using Microsoft.Win32;

namespace DemoMux;

internal static class InstalledApplication
{
    internal static string? PendingUninstall;
    internal static string? Uninstaller()
    {
        // Only use the registration matching this copy; never uninstall another installation.
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
        if (root is null) return null;
        foreach (var name in root.GetSubKeyNames().Where(n => n.StartsWith("DemoMux.Windows", StringComparison.Ordinal) && n.EndsWith("_is1", StringComparison.Ordinal)))
        {
            using var key = root.OpenSubKey(name);
            if (key?.GetValue("InstallLocation") is not string location ||
                !Path.TrimEndingDirectorySeparator(location).Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
            if (key.GetValue("UninstallString") is string command && command.Trim('"').Equals(path, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) return path;
        }
        return null;
    }
}
