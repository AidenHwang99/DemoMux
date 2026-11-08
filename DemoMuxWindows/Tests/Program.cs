using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using DemoMux;

static class Program
{
    static int passed;
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
    static void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
    static async Task Main(string[] args)
    {
        if (args.Contains("--job-parent"))
        {
            using var owned = ChildProcess.Start(Environment.GetEnvironmentVariable("ComSpec")!, "/c", "ping -n 60 127.0.0.1 >nul");
            Console.WriteLine(owned.Id); Console.Out.Flush(); await Task.Delay(Timeout.Infinite); return;
        }
        Environment.SetEnvironmentVariable("DEMOMUX_DATA_DIR", Path.Combine(Environment.CurrentDirectory, ".tools", "test-data"));
        var token = CancellationToken.None;
        var session = Directory.CreateTempSubdirectory("demomux-regression-").FullName;
        try
        {
            var slots = new PublicationSlots(); var projects = Enumerable.Range(1, 4).Select(i => new Project { Name = "Project " + i, Slug = "project-" + i }).ToArray();
            Check(slots.Reserve(projects[0], true).RootPort == 8443, "first root 8443");
            Check(slots.Reserve(projects[1], true).RootPort == 10000, "second root 10000");
            Check(slots.Reserve(projects[2], true).RootPort == 443, "third root 443");
            Reject(() => slots.Reserve(projects[3], false), "pending reservations enforce shared limit");
            slots.Release(projects[0].Id); Check(slots.Reserve(projects[3], false).RootPort is null, "release allows mixed path publication");
            Check(Catalog.ValidSlug("abc-123") && !Catalog.ValidSlug("../x") && !Catalog.ValidSlug("ABC"), "slug constraints");
            var source = Path.Combine(session, "source"); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "entry.html"), "<h1>snapshot one</h1>");
            File.WriteAllText(Path.Combine(source, "style.css"), "body{color:red}"); File.WriteAllText(Path.Combine(source, ".secret"), "secret");
            Directory.CreateDirectory(Path.Combine(source, "assets")); File.WriteAllText(Path.Combine(source, "assets", "a.txt"), "asset");
            var junction = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", Path.Combine(source, "linked"), Path.Combine(source, "assets") }, CreateNoWindow = true, UseShellExecute = false });
            junction!.WaitForExit(); if (junction.ExitCode != 0) throw new Exception("junction fixture failed");
            var project = projects[0]; project.Kind = "folder"; project.Source = source; project.Entry = "entry.html";
            using var prepared = await PreparedContent.PrepareAsync(project, session, token);
            Check(File.Exists(Path.Combine(prepared.Root!, "style.css")) && !File.Exists(Path.Combine(prepared.Root!, ".secret")) && !Directory.Exists(Path.Combine(prepared.Root!, "linked")), "snapshot keeps assets, excludes hidden and reparse points");
            File.WriteAllText(Path.Combine(source, "entry.html"), "changed");
            Check(File.ReadAllText(Path.Combine(prepared.Root!, "entry.html")).Contains("snapshot one"), "snapshot isolated from source edits");
            project.Entry = "../outside.html";
            try { using var bad = await PreparedContent.PrepareAsync(project, session, token); throw new Exception("accepted traversal"); } catch (IOException) { Check(true, "entry traversal rejected"); }
            project.Entry = "entry.html";
            var catalog = new Catalog { Projects = [project, projects[1]] };
            var component = Runtime.LoadComponents().Single(c => c.Name == "Caddy"); await component.InstallAsync(token);
            var previewPort = Gateway.FreePort(); var preview = $"http://127.0.0.1:{previewPort}/";
            using var gateway = new Gateway(component, session, catalog, previewPort) { Host = "example.test.ts.net" };
            gateway.Active[project.Id] = await PreparedContent.PrepareAsync(project, session, token);
            var share = gateway.Slots.Reserve(project, false);
            await gateway.ApplyAsync(token);
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
            var response = await client.GetAsync(preview + project.PublicPath.TrimStart('/'));
            Check(response.StatusCode == HttpStatusCode.TemporaryRedirect && response.Headers.Location?.OriginalString == project.PublicPath + "entry.html", "custom HTML entry redirects under path");
            Check(response.Headers.GetValues(PublicVerifier.Header).Single() == share.Marker, "current publication header");
            Check(await client.GetStringAsync(preview + project.PublicPath.TrimStart('/') + "style.css") == "body{color:red}", "relative assets served");
            Check((await client.GetAsync(preview + "p/unknown/")).StatusCode == HttpStatusCode.NotFound, "unknown paths never fall through");
            gateway.Slots.Home = project.Id; await gateway.ApplyAsync(token);
            Check((await client.GetAsync(preview)).Headers.Location?.OriginalString == project.PublicPath, "first project takes home");
            Check((await client.GetAsync(preview + ".demomux/")).Headers.Location?.OriginalString == project.PublicPath, "old guide entry follows home");
            gateway.Slots.Release(project.Id); await gateway.ApplyAsync(token);
            Check((await client.GetAsync(preview)).Headers.GetValues(PublicVerifier.Header).Single() == gateway.Marker, "last unpublish restores guide");
            var rootShare = gateway.Slots.Reserve(project, true); gateway.Slots.Home = project.Id; await gateway.ApplyAsync(token);
            Check((await client.GetAsync(preview)).Headers.Location?.AbsoluteUri == "https://example.test.ts.net:8443/", "independent entry takes home with correct HTTPS port");
            Check(await client.GetStringAsync($"http://127.0.0.1:{gateway.RootListeners[8443]}/style.css") == "body{color:red}", "independent root serves absolute resources");
            var servicePort = Gateway.FreePort();
            using var service = new HttpListener(); service.Prefixes.Add($"http://127.0.0.1:{servicePort}/"); service.Start();
            var serviceCancellation = new CancellationTokenSource();
            var serviceTask = Task.Run(async () => { try { while (!serviceCancellation.IsCancellationRequested) { var request = await service.GetContextAsync().WaitAsync(serviceCancellation.Token); request.Response.Headers["Content-Type"] = "text/html"; var bytes = System.Text.Encoding.UTF8.GetBytes("port project"); request.Response.ContentLength64 = bytes.Length; await request.Response.OutputStream.WriteAsync(bytes); request.Response.Close(); } } catch (OperationCanceledException) { } });
            var portProject = projects[1]; portProject.Kind = "port"; portProject.Port = servicePort;
            gateway.Active[portProject.Id] = await PreparedContent.PrepareAsync(portProject, session, token);
            var portShare = gateway.Slots.Reserve(portProject, false); await gateway.ApplyAsync(token);
            Check(await client.GetStringAsync(preview + portProject.PublicPath.TrimStart('/')) == "port project", "live local HTTP project proxies alongside static root");
            gateway.Slots.Release(portProject.Id); gateway.Active.Remove(portProject.Id, out var portContent); portContent?.Dispose(); await gateway.ApplyAsync(token);
            Check(await client.GetStringAsync($"http://127.0.0.1:{servicePort}/") == "port project", "stop port entry preserves user process");
            Check(await client.GetStringAsync($"http://127.0.0.1:{gateway.RootListeners[8443]}/style.css") == "body{color:red}", "stopping one entry preserves other root");
            Check((await client.GetAsync(preview + portProject.PublicPath.TrimStart('/'))).StatusCode == HttpStatusCode.NotFound, "unpublished path inaccessible");
            serviceCancellation.Cancel(); await serviceTask; serviceCancellation.Dispose();
            using (var occupied = new TcpListener(IPAddress.Loopback, 0))
            {
                occupied.Start(); using var conflict = new Gateway(component, session, catalog, ((IPEndPoint)occupied.LocalEndpoint).Port);
                try { await conflict.ApplyAsync(token); throw new Exception("occupied listener accepted"); } catch (IOException e) { Check(e.Message.Contains("占用"), "occupied preview diagnosed"); }
            }
            var good = new HttpResponseMessage(HttpStatusCode.OK); good.Headers.Add(PublicVerifier.Header, "marker"); PublicVerifier.ValidateResponse(good, new Uri("https://x.ts.net/p/demo/"), "marker"); Check(true, "public marker validation");
            Reject(() => PublicVerifier.ValidateResponse(good, new Uri("https://x.ts.net/"), "wrong"), "stale marker rejected");
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect); redirect.Headers.Add(PublicVerifier.Header, "marker"); redirect.Headers.Location = new Uri("https://evil.example/");
            Reject(() => PublicVerifier.ValidateResponse(redirect, new Uri("https://x.ts.net/p/demo/"), "marker"), "external redirect rejected");
            redirect.Headers.Location = new Uri("https://x.ts.net/p/other/"); Reject(() => PublicVerifier.ValidateResponse(redirect, new Uri("https://x.ts.net/p/demo/"), "marker"), "sibling project redirect rejected");
            var child = ChildProcess.Start(Environment.GetEnvironmentVariable("ComSpec")!, "/c", "ping -n 30 127.0.0.1 >nul"); var pid = child.Id; child.Dispose();
            Check(!System.Diagnostics.Process.GetProcesses().Any(p => p.Id == pid), "Job Object terminates owned child");
            using var parent = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--job-parent" }, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
            var ownedPid = int.Parse((await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!);
            using var ownedProcess = System.Diagnostics.Process.GetProcessById(ownedPid);
            Check(!ownedProcess.HasExited, "owned fixture running before forced parent termination");
            parent.Kill(); await parent.WaitForExitAsync();
            await ownedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(ownedProcess.HasExited, "forced parent termination closes Job and kills child");
            Console.WriteLine($"{passed} checks passed on {Environment.OSVersion}; .NET {Environment.Version}");
        }
        finally { Runtime.TryDelete(session); }
    }
}
