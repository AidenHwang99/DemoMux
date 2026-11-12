using System.IO;
using System.Net;
using System.Net.Http;
using DemoMux;
using static PublicationPreflightRegression;

internal static class LocalHttpRegression
{
    internal static async Task CheckActualAsync(string address, Action<bool, string> check)
    {
        var endpoint = LocalHttpEndpoint.Parse(address) ?? throw new ArgumentException("Invalid local address");
        var catalog = Catalog.Load();
        catalog.Add(new Project { Kind = "port", Name = "Legacy port", Port = endpoint.Port });
        var file = Path.Combine(Runtime.Data, "projects-v1.json");
        var legacy = File.ReadAllText(file);
        var project = new Project { Kind = "port", Name = "Local host", Port = endpoint.Port, UpstreamHost = endpoint.UpstreamHost };
        await Runtime.CheckProjectAsync(endpoint, CancellationToken.None);
        catalog.Add(project);
        var reloaded = Catalog.Load();
        check(reloaded.Version == 2 && File.ReadAllText(file + ".pre-local-host.bak") == legacy, "catalog upgrade backs up the original version 1 file");
        check(reloaded.Projects.Single(p => p.Id == project.Id).Endpoint.Url == endpoint.Url
            && reloaded.Projects.Single(p => p.Id != project.Id).Endpoint.Host == LocalHttpEndpoint.Loopback, "reopened catalog retains both hosts and the preview URL");
        var target = new PublicationTarget(project.Id, endpoint.Port, true, "demomux-probe", endpoint.UpstreamHost);
        var root = await PublicationPreflight.CheckAsync(target, CancellationToken.None);
        var path = await PublicationPreflight.CheckAsync(target with { Root = false }, CancellationToken.None);
        check(root.Status == PreflightStatus.Accessible, "actual local project passes independent entry preflight");
        check(path.Status == PreflightStatus.Incompatible && path.Message.Contains("/assets/"), "actual local project explains out-of-prefix assets");
        Console.WriteLine(path.Message);
    }

    internal static async Task RunAsync(string session, Action<bool, string> check, CancellationToken token, bool browser = false)
    {
        check(LocalHttpEndpoint.Parse(" 8080 ") == LocalHttpEndpoint.Parse("http://127.0.0.1:8080/"), "legacy port and explicit numeric loopback normalize identically");
        check(LocalHttpEndpoint.Parse("HTTP://Console.LocalHost:8080/") == new LocalHttpEndpoint(8080, "console.localhost"), "custom localhost URL normalizes its host");
        check(LocalHttpEndpoint.Parse(new LocalHttpEndpoint(80, "console.localhost").AddressText) == new LocalHttpEndpoint(80, "console.localhost"), "editing a service on the HTTP default port retains its explicit port");
        var invalid = new[] { "8081", "0", "65536", "https://console.localhost:8080/", "http://console.localhost/", "http://console.localhost:8080/api", "http://console.localhost:8080/?x", "http://console.localhost:8080/#x", "http://a@localhost:8080", "http://192.168.1.10:8080/", "http://console.localhost.example.test:8080", "http://-a.localhost:8080", "http://localhost.:8080", "http://localhost:8080\\" };
        check(invalid.All(value => LocalHttpEndpoint.Parse(value) is null), "invalid URL shapes, non-loopback hosts and reserved ports are rejected");

        int port = 0;
        using var upstream = new Server((host, rawPath) => ReplyTo(host, rawPath, port));
        port = upstream.Port;
        var alpha = new Project { Kind = "port", Name = "Alpha", Slug = "alpha", Port = port, UpstreamHost = "alpha.localhost" };
        var beta = new Project { Kind = "port", Name = "Beta", Slug = "beta", Port = port, UpstreamHost = "beta.localhost" };
        var catalog = new Catalog { Projects = [alpha, beta] };
        check(catalog.ContainsEndpoint(alpha.Endpoint) && !catalog.ContainsEndpoint(alpha.Endpoint, alpha.Id)
            && !catalog.ContainsEndpoint(new(port, "other.localhost")), "duplicate detection distinguishes hosts on one port");
        var target = new PublicationTarget(alpha.Id, port, true, alpha.Slug, alpha.UpstreamHost);
        var root = await PublicationPreflight.CheckAsync(target, token);
        var wrongHost = await PublicationPreflight.CheckAsync(target with { UpstreamHost = null }, token);
        var badAssets = await PublicationPreflight.CheckAsync(target with { Root = false }, token);
        var goodPath = await PublicationPreflight.CheckAsync(new(beta.Id, port, false, beta.Slug, beta.UpstreamHost), token);
        check(root.Status == PreflightStatus.Accessible && wrongHost.Status == PreflightStatus.Incompatible && wrongHost.Message.Contains("alpha.localhost"), "preflight selects explicit host and diagnoses a different local host redirect");
        check(badAssets.Status == PreflightStatus.Incompatible && badAssets.Message.Contains("/assets/app.js") && goodPath.Status == PreflightStatus.Accessible, "preflight detects a base-tag asset escape and accepts in-prefix HTML assets");
        var redirecting = await PublicationPreflight.CheckAsync(new(beta.Id, port, false, "redirect", beta.UpstreamHost), token);
        check(redirecting.Status == PreflightStatus.Accessible, "preflight follows same custom host absolute redirect within project path");
        using (var large = new Server(_ => new(Body: "<!--" + new string(' ', 128 * 1024) + "--><script src='/outside.js'></script>", ContentType: "text/html")))
            check((await PublicationPreflight.CheckAsync(target with { Port = large.Port, Root = false }, token)).Status == PreflightStatus.Accessible,
                "static asset inspection is limited to the first 128 KiB");

        var component = Runtime.LoadComponents().Single(c => c.Name == "Caddy");
        await component.InstallAsync(token);
        var previewPort = Gateway.FreePort();
        using var gateway = new Gateway(component, session, catalog, previewPort) { Host = "fixture.ts.net" };
        gateway.Active[alpha.Id] = await PreparedContent.PrepareAsync(alpha, session, token);
        gateway.Active[beta.Id] = await PreparedContent.PrepareAsync(beta, session, token);
        var independent = gateway.Slots.Reserve(alpha, true);
        var named = gateway.Slots.Reserve(beta, false);
        await gateway.ApplyAsync(token);
        var rootUrl = new Uri($"http://127.0.0.1:{gateway.RootListeners[independent.RootPort!.Value]}/");
        var namedUrl = new Uri($"http://127.0.0.1:{previewPort}{beta.PublicPath}");
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        check((await client.GetStringAsync(new Uri(rootUrl, "api"), token)) == "alpha"
            && (await client.GetStringAsync(new Uri(namedUrl, "api"), token)) == "beta", "real Caddy isolates the independent and named hosts on the same backend port");
        await CheckRedirect(rootUrl, "absolute", new Uri(independent.Url(gateway.Host, alpha), "next?x=1&y=2").AbsoluteUri, 307);
        await CheckRedirect(namedUrl, "absolute", new Uri(named.Url(gateway.Host, beta), "next?x=1&y=2").AbsoluteUri, 307);
        await CheckRedirect(namedUrl, "relative", "next?from=relative", 302);
        await CheckRedirect(namedUrl, "outside", $"http://beta.localhost:{port}/outside", 307);
        await CheckRedirect(namedUrl, "different-host", $"http://alpha.localhost:{port}/p/beta/next", 307);
        using (var escaped = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect))
        {
            escaped.Headers.Add(PublicVerifier.Header, named.Marker);
            escaped.Headers.Location = new Uri("https://fixture.ts.net/p/beta/%2F..%2F..%2Foutside");
            try { PublicVerifier.ValidateResponse(escaped, named.Url(gateway.Host, beta), named.Marker); throw new Exception("escaped path accepted"); }
            catch (InvalidDataException) { check(true, "public verification rejects encoded project-path escapes"); }
            var entry = named.Url(gateway.Host, beta);
            escaped.Headers.Location = new Uri("../next", UriKind.Relative);
            PublicVerifier.ValidateResponse(escaped, new Uri(entry, "nested/page"), named.Marker, entry: entry);
            check(true, "public redirect chains retain the original project scope when navigating back from a nested path");
        }
        if (browser)
        {
            Console.WriteLine("ROOT=" + rootUrl);
            Console.WriteLine("NAMED=" + namedUrl);
            await Task.Delay(Timeout.Infinite, token);
        }

        async Task CheckRedirect(Uri origin, string path, string expected, int status)
        {
            using var response = await client.GetAsync(new Uri(origin, path), token);
            check((int)response.StatusCode == status && response.Headers.Location?.OriginalString == expected,
                $"Caddy {origin.AbsolutePath}{path} preserves redirect status and scope (received {response.Headers.Location})");
        }
    }

    private static Reply ReplyTo(string host, string rawPath, int port)
    {
        var path = rawPath.Split('?')[0];
        if (host == $"127.0.0.1:{port}") return new(307, $"http://alpha.localhost:{port}/");
        bool alpha = host == $"alpha.localhost:{port}";
        if (!alpha && host != $"beta.localhost:{port}") return new(421);
        var prefix = alpha ? "/" : "/p/beta/";
        var name = alpha ? "alpha" : "beta";
        if (alpha && path == "/p/alpha/") return new(Body: "<base href='/assets/'><script src='app.js'></script>", ContentType: "text/html");
        if (path == "/p/redirect/") return new(307, $"http://{host}/p/redirect/next?x=1");
        if (path == "/p/redirect/next") return new();
        if (path == prefix + "absolute") return new(307, $"http://{host}{prefix}next?x=1&y=2");
        if (path == prefix + "relative") return new(302, "next?from=relative");
        if (path == prefix + "outside") return new(307, $"http://{host}/outside");
        if (path == prefix + "different-host") return new(307, $"http://alpha.localhost:{port}{prefix}next");
        if (path == prefix + "api" || path == prefix + "next") return new(Body: name);
        if (path == prefix + "style.css") return new(Body: "#result { color: rgb(17, 34, 51); }", ContentType: "text/css");
        if (path == prefix + "script.js") return new(Body: "fetch('api').then(r => r.text()).then(text => document.querySelector('#result').textContent = text);", ContentType: "application/javascript");
        if (path != prefix && path != "/") return new(404);
        return new(Body: "<!doctype html><meta charset=utf-8><link rel=stylesheet href=style.css><script defer src=script.js></script><p id=result>loading</p>", ContentType: "text/html");
    }
}
