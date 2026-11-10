using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Security.AccessControl;
using System.Security.Principal;
namespace DemoMux;
internal sealed class GuideWindow : Window
{
    private readonly WebView2 browser = new();
    private bool closed;
    public GuideWindow()
    {
        Title = "DemoMux 使用说明"; Width = 960; Height = 740; Content = browser;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/DemoMux.ico"));
        Theme.Apply(this); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) => WindowMaterial.Apply(this);
        Closed += (_, _) => { closed = true; browser.Dispose(); };
        Loaded += async (_, _) =>
        {
            try
            {
                var runtime = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime");
                if (!File.Exists(Path.Combine(runtime, "msedgewebview2.exe"))) throw new IOException("安装包缺少固定版本 WebView2Runtime，请重新运行安装包修复，或重新解压完整便携包。");
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) await Task.Run(() => GrantRuntimeReadAccess(runtime));
                var environment = await CoreWebView2Environment.CreateAsync(runtime, Path.Combine(Runtime.Data, "WebView2"));
                if (closed) return;
                await browser.EnsureCoreWebView2Async(environment);
                if (closed) return;
                browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                browser.CoreWebView2.SetVirtualHostNameToFolderMapping("guide.demomux.invalid", Path.Combine(AppContext.BaseDirectory, "Sample"), CoreWebView2HostResourceAccessKind.DenyCors);
                browser.CoreWebView2.NavigationStarting += (_, args) =>
                {
                    if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var url) && url.Host == "guide.demomux.invalid" && url.Scheme == "https") return;
                    args.Cancel = true;
                    if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var external) && external.Scheme is "http" or "https") MainWindow.Open(external);
                };
                browser.CoreWebView2.NewWindowRequested += (_, args) => { args.Handled = true; if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var url) && url.Scheme is "http" or "https") MainWindow.Open(url); };
                browser.Source = new Uri("https://guide.demomux.invalid/index.html");
            }
            catch (Exception e) { if (!closed) Content = MainWindow.Text(e.Message); }
        };
    }
    internal static void GrantRuntimeReadAccess(string path)
    {
        var directory = new DirectoryInfo(path);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("请将完整应用解压到本机普通文件夹后打开说明。");
        var security = directory.GetAccessControl();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        bool changed = false;
        foreach (var id in new[] { "S-1-15-2-1", "S-1-15-2-2" })
        {
            var sid = new SecurityIdentifier(id);
            if (rules.Any(rule => rule.IdentityReference.Equals(sid) && rule.AccessControlType == AccessControlType.Allow
                && (rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute
                && (rule.InheritanceFlags & (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)) == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit))) continue;
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            changed = true;
        }
        if (changed) directory.SetAccessControl(security);
    }
}
