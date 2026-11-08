using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace DemoMux;
internal sealed class GuideWindow : Window
{
    private readonly WebView2 browser = new();
    private bool closed;
    public GuideWindow()
    {
        Title = "DemoMux 使用说明"; Width = 960; Height = 740; Content = browser;
        Closed += (_, _) => { closed = true; browser.Dispose(); };
        Loaded += async (_, _) =>
        {
            try
            {
                var runtime = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime");
                if (!File.Exists(Path.Combine(runtime, "msedgewebview2.exe"))) throw new IOException("安装包缺少固定版本 WebView2Runtime，请重新解压完整安装包。");
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
}
