using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DemoMux;

internal sealed class Gateway : IDisposable
{
    public readonly Dictionary<Guid, PreparedContent> Active = [];
    public readonly PublicationSlots Slots = new();
    public readonly string Marker = Guid.NewGuid().ToString("N");
    public readonly int LocalPort = FreePort();
    private readonly int adminPort = FreePort();
    public readonly Dictionary<int, int> RootListeners = new() { [8443] = FreePort(), [10000] = FreePort() };
    public string? Host { get; set; }
    private ChildProcess? process;
    private readonly Component component;
    private readonly string session;
    private readonly Catalog catalog;
    private readonly int previewPort;
    private readonly string probe = "__demomux_" + Guid.NewGuid().ToString("N");
    public bool Running => process?.Running == true;
    public Gateway(Component component, string session, Catalog catalog, int previewPort = Runtime.PreviewPort) { this.component = component; this.session = session; this.catalog = catalog; this.previewPort = previewPort; }
    public static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    public async Task ApplyAsync(CancellationToken token)
    {
        var configuration = Configuration();
        if (Running)
        {
            using var admin = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
            using var payload = new ByteArrayContent(configuration);
            payload.Headers.ContentType = new("application/json");
            using var response = await admin.PostAsync($"http://127.0.0.1:{adminPort}/load", payload, token);
            if (!response.IsSuccessStatusCode) throw new IOException("路由更新失败：" + await response.Content.ReadAsStringAsync(token));
            return;
        }
        process?.Dispose(); process = null;
        using (var listener = new TcpListener(IPAddress.Loopback, previewPort))
        {
            try { listener.ExclusiveAddressUse = true; listener.Start(); }
            catch (SocketException e) { throw new IOException("8081 已被占用，请停止占用程序后重试。", e); }
        }
        var path = Path.Combine(session, "gateway.json");
        await File.WriteAllBytesAsync(path, configuration, token);
        process = ChildProcess.Start(component.Executable("caddy.exe"), "run", "--config", path);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1) };
        for (int i = 0; i < 40 && Running; i++)
        {
            token.ThrowIfCancellationRequested();
            try { if (await client.GetStringAsync($"http://127.0.0.1:{previewPort}/" + probe, token) == probe) return; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { }
            await Task.Delay(100, token);
        }
        throw new IOException("Caddy 启动失败。" + process.Output);
    }
    private static Dictionary<string, object> Route(string[]? paths, params object[] handlers)
    {
        var route = new Dictionary<string, object> { ["handle"] = handlers, ["terminal"] = true };
        if (paths is not null) route["match"] = new[] { new { path = paths } };
        return route;
    }
    private static object Redirect(string destination) => new { handler = "static_response", status_code = 307, headers = new Dictionary<string, string[]> { ["Location"] = [destination] } };
    private static object Header(string? marker) => new { handler = "headers", response = new { deferred = true, set = marker is null ? new Dictionary<string, string[]> { ["Cache-Control"] = ["no-store"] } : new Dictionary<string, string[]> { ["Cache-Control"] = ["no-store"], [PublicVerifier.Header] = [marker] } } };
    private static IEnumerable<object> Routes(PreparedContent content, string prefix, string? marker, string? publicAuthority = null)
    {
        var root = prefix == "/";
        if (!root) yield return Route([prefix.TrimEnd('/')], Header(marker), Redirect(prefix));
        var handlers = new List<object> { Header(marker) };
        if (content.Root is { } directory)
        {
            if (content.Entry != "index.html") yield return Route([prefix], Header(marker), Redirect(prefix + string.Join('/', content.Entry.Split('/').Select(Uri.EscapeDataString))));
            if (!root) handlers.Add(new { handler = "rewrite", strip_path_prefix = prefix.TrimEnd('/') });
            handlers.Add(new { handler = "file_server", root = directory });
        }
        // Stream headers immediately, including through Funnel's reverse proxy.
        // Removing a fixed length keeps a slow backend's initial tiny chunk from
        // making an intermediary buffer the headers until more body arrives.
        else handlers.Add(Proxy(content.Endpoint, prefix, publicAuthority));
        yield return Route(root ? null : [prefix + "*"], handlers.ToArray());
    }
    private static object Proxy(LocalHttpEndpoint endpoint, string prefix, string? publicAuthority)
    {
        if (!endpoint.Valid) throw new InvalidDataException("本机 HTTP 地址无效。");
        var response = new Dictionary<string, object> { ["delete"] = new[] { "Content-Length" } };
        if (publicAuthority is not null)
        {
            var host = Regex.Escape(endpoint.Host);
            var port = endpoint.Port == 80 ? "(?::80)?" : $":{endpoint.Port}";
            var path = prefix == "/" ? @"(/[^?#\\\s]*)?" : "(" + Regex.Escape(prefix) + @"[^?#\\\s]*)";
            response["replace"] = new Dictionary<string, object>
            {
                ["Location"] = new[] { new { search_regexp = $@"^(?i:(?:http:)?//{host}){port}{path}(\?[^#\\\s]*)?$",
                    replace = "https://" + publicAuthority + "${1}${2}" } }
            };
        }
        return new { handler = "reverse_proxy", flush_interval = -1, upstreams = new[] { new { dial = $"127.0.0.1:{endpoint.Port}" } },
            headers = new { request = new { set = new Dictionary<string, string[]> { ["Host"] = [endpoint.Authority], ["X-Forwarded-Proto"] = ["https"] } }, response } };
    }
    internal byte[] Configuration()
    {
        var servers = new Dictionary<string, object>();
        var primary = new List<object> { Route(["/" + probe], new { handler = "static_response", body = probe }) };
        var local = new List<object>();
        var guide = new PreparedContent(Path.Combine(AppContext.BaseDirectory, "Sample"), "index.html", 0);
        var home = Slots.Home is { } id ? catalog.Projects.FirstOrDefault(p => p.Id == id) : null;
        var homeShare = home is not null ? Slots.Shares.GetValueOrDefault(home.Id) : null;
        string? target = homeShare is null ? null : homeShare.RootPort is null ? home!.PublicPath : homeShare.RootPort == 443 ? "/" : homeShare.Url(Host!, home!).AbsoluteUri;
        primary.AddRange(homeShare is null ? Routes(guide, "/.demomux/", Marker) : [Route(["/.demomux", "/.demomux/*"], Header(homeShare.Marker), Redirect(target!))]);
        foreach (var project in catalog.Projects)
        {
            if (!Active.TryGetValue(project.Id, out var content)) continue;
            local.AddRange(Routes(content, project.LocalPath, null));
            if (!Slots.Shares.TryGetValue(project.Id, out var share)) continue;
            if (share.RootPort is null) primary.AddRange(Routes(content, project.PublicPath, share.Marker, share.Url(Host!, project).Authority));
            else if (share.RootPort != 443) servers["root-" + share.RootPort] = Server(RootListeners[share.RootPort.Value], Routes(content, "/", share.Marker, share.Url(Host!, project).Authority));
        }
        primary.Add(Route(["/p", "/p/*", "/.demomux/*"], new { handler = "static_response", status_code = 404 }));
        var rootShare = Slots.Shares.Values.FirstOrDefault(s => s.RootPort == 443);
        if (rootShare is not null) primary.AddRange(Routes(Active[rootShare.ProjectId], "/", rootShare.Marker, rootShare.Url(Host!, catalog.Projects.First(p => p.Id == rootShare.ProjectId)).Authority));
        else if (homeShare is not null) primary.Add(Route(null, Header(homeShare.Marker), Redirect(target!)));
        else primary.AddRange(Routes(guide, "/", Marker));
        local.Add(Route(null, new { handler = "static_response", status_code = 404 }));
        servers["primary"] = Server(previewPort, primary);
        servers["local"] = Server(LocalPort, local);
        return JsonSerializer.SerializeToUtf8Bytes(new { admin = new { listen = $"127.0.0.1:{adminPort}", config = new { persist = false } }, apps = new { http = new { servers } } });
    }
    private static object Server(int port, IEnumerable<object> routes) => new { listen = new[] { $"127.0.0.1:{port}" }, routes = routes.ToArray() };
    public void Dispose() { process?.Dispose(); process = null; foreach (var content in Active.Values) content.Dispose(); Active.Clear(); Slots.Clear(); }
}
