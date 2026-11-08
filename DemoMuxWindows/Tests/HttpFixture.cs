using System.Net;
using System.Text;
using System.IO;
internal static class HttpFixture
{
    public static async Task RunAsync(string modeFile)
    {
        using var listener = new HttpListener(); listener.Prefixes.Add("http://127.0.0.1:43123/"); listener.Start();
        Console.WriteLine("Fixture ready on 43123");
        while (true)
        {
            var context = await listener.GetContextAsync();
            _ = Task.Run(async () =>
            {
                try
                {
                    var mode = File.Exists(modeFile) ? File.ReadAllText(modeFile).Trim() : "good";
                    if (context.Request.Url!.AbsolutePath == "/asset.txt") mode = "asset";
                    if (mode == "redirect") { context.Response.StatusCode = 302; context.Response.RedirectLocation = "https://example.invalid/"; context.Response.Close(); return; }
                    if (mode == "delay") await Task.Delay(20000);
                    var html = mode == "asset" ? "root asset loaded" : "<!doctype html><meta charset='utf-8'><title>DemoMux HTTP fixture</title><h1>HTTP port fixture</h1><script>fetch('/asset.txt').then(r=>r.text()).then(t=>document.body.append(t))</script>";
                    if (mode == "slow") html += new string(' ', 2 * 1024 * 1024);
                    var data = Encoding.UTF8.GetBytes(html); context.Response.ContentType = "text/html; charset=utf-8"; context.Response.ContentLength64 = data.Length;
                    if (mode == "slow") { await context.Response.OutputStream.WriteAsync(data.AsMemory(0, 128)); await context.Response.OutputStream.FlushAsync(); await Task.Delay(20000); await context.Response.OutputStream.WriteAsync(data.AsMemory(128)); }
                    else await context.Response.OutputStream.WriteAsync(data);
                    context.Response.Close();
                }
                catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException) { context.Response.Abort(); }
            });
        }
    }
}
