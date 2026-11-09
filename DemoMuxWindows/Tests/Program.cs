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
        if (args.Contains("--retry-ui") || args.Contains("--layout-ui"))
        {
            // Isolated UI fixture: no component startup, user identity or network changes.
            Environment.SetEnvironmentVariable("DEMOMUX_DATA_DIR", Path.Combine(Environment.CurrentDirectory, ".tools", args.Contains("--layout-ui") ? "layout-ui-data" : "retry-ui-data"));
            var thread = new Thread(() => {
                var app = new System.Windows.Application();
                var window = new MainWindow(false);
                window.Title = args.Contains("--layout-ui") ? "DemoMux · 布局测试" : "DemoMux · 重试测试";
                window.Loaded += (_, _) => {
                    if (!args.Contains("--layout-ui")) { window.Run("校验界面测试", t => window.RunVerificationAsync(_ => Task.FromException(new PublicVerificationException(PublicFailure.Timeout,"公网响应超时。")), t),true); return; }
                    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var catalog = (Catalog)typeof(MainWindow).GetField("catalog",flags)!.GetValue(window)!;
                    var gateway = (Gateway)typeof(MainWindow).GetField("gateway",flags)!.GetValue(window)!;
                    var account = (AccountSession)typeof(MainWindow).GetField("account",flags)!.GetValue(window)!;
                    ((System.Windows.Threading.DispatcherTimer)typeof(MainWindow).GetField("monitor",flags)!.GetValue(window)!).Stop();
                    const string profiles = "[{\"id\":\"fixture\",\"nickname\":\"布局示例账号\",\"tailnet\":\"example.test\",\"account\":\"demo@example.test\",\"selected\":true}]";
                    account.ApplySnapshot(profiles,"{\"BackendState\":\"Running\",\"Self\":{\"DNSName\":\"layout.example.ts.net\"}}",profiles);
                    catalog.Projects = [new Project { Name="产品演示页面",Slug="product-demo" },new Project { Name="本机服务 · 3000",Slug="local-service",Kind="port",Port=3000 },new Project { Name=new string('长',70),Slug="long-project",Kind="folder",Source="测试文件夹",Entry="index.html" }];
                    var share = gateway.Slots.Reserve(catalog.Projects[0],false); share.Verified=true; share.Pending=false; gateway.Slots.Home=catalog.Projects[0].Id;
                    typeof(MainWindow).GetField("dashboard",flags)!.SetValue(window,true); typeof(MainWindow).GetField("ready",flags)!.SetValue(window,true);
                    typeof(MainWindow).GetMethod("Render",flags | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(window,null);
                };
                app.Run(window);
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); return;
        }
        if (args.Length == 2 && args[0] == "--http-fixture") { await HttpFixture.RunAsync(args[1]); return; }
        if (args.Contains("--install-default") || args.Contains("--install-fresh"))
        {
            Environment.SetEnvironmentVariable("DEMOMUX_DATA_DIR", args.Contains("--install-fresh") ? Path.Combine(Environment.CurrentDirectory, ".tools", "fresh-install-" + Guid.NewGuid().ToString("N")) : null);
            Environment.SetEnvironmentVariable("DEMOMUX_COMPONENT_CACHE", null);
            foreach (var component in Runtime.LoadComponents()) { await component.InstallAsync(CancellationToken.None); Console.WriteLine(component.Name + " default AppData install and SHA-256 verified"); }
            return;
        }
        if (args.Length == 3 && args[0] == "--probe") { using var trace = Environment.GetEnvironmentVariable("DEMOMUX_NETWORK_TRACE") == "1" ? new NetworkTrace() : null; await PublicVerifier.VerifyAsync(new Uri(args[1]), args[2], CancellationToken.None); Console.WriteLine("Public DNS / strict HTTPS / current marker verified"); return; }
        if (args.Contains("--dns-diagnostic"))
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            foreach (var resolver in new[] { "https://cloudflare-dns.com/dns-query", "https://dns.google/resolve" })
            {
                Console.WriteLine("Proxy: " + HttpClient.DefaultProxy.GetProxy(new Uri(resolver)));
                try { using var request = new HttpRequestMessage(HttpMethod.Get, resolver + "?name=example.com&type=A"); request.Headers.Accept.ParseAdd("application/dns-json"); using var response = await client.SendAsync(request); Console.WriteLine(response.StatusCode + " " + await response.Content.ReadAsStringAsync()); }
                catch (Exception e) { Console.WriteLine(e.ToString()); }
            }
            return;
        }
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
            Exception? layoutFailure = null;
            var layoutThread = new Thread(() => {
                try {
                    var first = new System.Windows.Controls.Border { Height = 80 };
                    var second = new System.Windows.Controls.Border { Height = 160 };
                    var pair = new AdaptivePair(first, second, 1060, true);
                    foreach (var width in new[] { 720, 810, 920, 1000, 1440, 1920, 720 }) {
                        pair.Measure(new System.Windows.Size(width, double.PositiveInfinity));
                        pair.Arrange(new System.Windows.Rect(0, 0, width, pair.DesiredSize.Height));
                        var position = second.TranslatePoint(new System.Windows.Point(), pair);
                        Check(width < 1060 ? position.Y == 96 : position.Y == 0 && position.X > 0, "existing children reflow at content DIP width " + width);
                        Check(first.ActualHeight == 80 && second.ActualHeight == 160, "unequal column heights remain independent at " + width);
                    }
                } catch (Exception error) { layoutFailure = error; }
            });
            layoutThread.SetApartmentState(ApartmentState.STA); layoutThread.Start(); layoutThread.Join();
            if (layoutFailure is not null) throw layoutFailure;
            var oldCatalog = System.Text.Json.JsonSerializer.Deserialize<Catalog>("{\"Version\":1,\"OpenHome\":false}")!;
            Check(oldCatalog.RememberWindow && !oldCatalog.OpenHome && oldCatalog.Window is null, "legacy catalog preserves OpenHome and defaults window preference");
            var preferences = new Catalog { RememberWindow = false, Window = new(1, 2, 900, 700, true) };
            var restoredPreferences = System.Text.Json.JsonSerializer.Deserialize<Catalog>(System.Text.Json.JsonSerializer.Serialize(preferences))!;
            Check(!restoredPreferences.RememberWindow && restoredPreferences.Window == preferences.Window, "window preferences round trip without changing schema version");
            var package = Directory.CreateDirectory(Path.Combine(session, "package")).FullName;
            var isolatedData = Directory.CreateDirectory(Path.Combine(session, "data")).FullName;
            File.WriteAllText(Path.Combine(package, "app.exe"), "owned");
            File.WriteAllText(Path.Combine(package, "original.html"), "user project");
            var ownedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(package, "app.exe"))));
            var context = new UninstallContext(package, isolatedData, true);
            var manifest = new[] { new PackageFile("app.exe", ownedHash) };
            var plan = UninstallPlan.Create(context, manifest);
            Check(plan.Files.Count == 1 && plan.Preserved.Single().Contains("original.html"), "uninstall preflight retains mixed original project files");
            File.WriteAllText(Path.Combine(package, "app.exe"), "modified");
            Check(UninstallPlan.Create(context, manifest).Files.Count == 0, "modified package file excluded from uninstall");
            Reject(() => UninstallPlan.Create(context, [new("../outside.exe", ownedHash)]), "uninstall rejects manifest traversal");
            Reject(() => UninstallPlan.Create(context, [new("app.exe", ownedHash), new("APP.EXE", ownedHash)]), "uninstall rejects duplicate Windows paths");
            Reject(() => UninstallPlan.Create(context with { IsolatedTest = false }, manifest), "production uninstall refuses environment-directed custom data directory");
            Reject(() => UninstallPlan.Create(context with { ApplicationDirectory = Path.GetPathRoot(package)! }, manifest), "uninstall refuses volume root");
            Reject(() => UninstallPlan.Create(context, []), "uninstall refuses empty manifest");
            Check(File.Exists(Path.Combine(package, "original.html")) && File.Exists(Path.Combine(package, "app.exe")), "preflight never deletes or modifies files");
            int retries = 0; var events = new List<VerificationProgress>();
            await VerificationRetry.RunAsync(t => ++retries < 3 ? Task.FromException(new PublicVerificationException(PublicFailure.Timeout, "timeout")) : Task.CompletedTask, events.Add, token, (_, _) => Task.CompletedTask);
            Check(retries == 3 && events.Where(e => e.RetryInSeconds is null).Select(e => e.Attempt).SequenceEqual([1,2,3]), "transient failures retry and report attempts until success");
            Check(events.Where(e => e.RetryInSeconds.HasValue).Select(e => e.RetryInSeconds!.Value).SequenceEqual([2,1,4,3,2,1]), "retry backoff reports each countdown second");
            using (var stop = new CancellationTokenSource())
            {
                int calls = 0;
                try { await VerificationRetry.RunAsync(t => { calls++; throw new PublicVerificationException(PublicFailure.Connection, "offline"); }, state => { if (state.RetryInSeconds.HasValue) stop.Cancel(); }, stop.Token, (_, t) => Task.Delay(1,t)); throw new Exception("cancel ignored"); }
                catch (OperationCanceledException) { Check(calls == 1, "stop during countdown prevents the next attempt"); }
            }
            using (var stop = new CancellationTokenSource())
            {
                try { await VerificationRetry.RunAsync(async t => { stop.Cancel(); await Task.Delay(1000,t); }, _ => {}, stop.Token); throw new Exception("cancel ignored"); }
                catch (OperationCanceledException) { Check(true, "stop interrupts an active verification"); }
            }
            foreach (var error in new Exception[] { new PublicVerificationException(PublicFailure.Certificate, "certificate"), new InvalidDataException("marker or redirect"), new AccountIdentityException() })
            {
                int calls = 0;
                try { await VerificationRetry.RunAsync(t => { calls++; return Task.FromException(error); }, _ => {}, token, (_, _) => Task.CompletedTask); throw new Exception("unsafe retry"); }
                catch (Exception actual) when (ReferenceEquals(actual,error)) { Check(calls == 1, "nontransient failure is not retried: " + error.GetType().Name); }
            }
            var slots = new PublicationSlots(); var projects = Enumerable.Range(1, 4).Select(i => new Project { Name = "Project " + i, Slug = "project-" + i }).ToArray();
            Check(new[] { "eye","external-link","copy","cloud-upload","cloud-off","circle-play","circle-stop","ellipsis","user-round","check","chevron-down" }.All(name => SvgIcon.Load(name).Length > 0), "all shared SVG icons load into native vector geometry");
            Check(slots.Reserve(projects[0], true).RootPort == 8443, "first root 8443");
            Check(slots.Reserve(projects[1], true).RootPort == 10000, "second root 10000");
            Check(slots.Reserve(projects[2], true).RootPort == 443, "third root 443");
            Reject(() => slots.Reserve(projects[3], false), "pending reservations enforce shared limit");
            slots.Release(projects[0].Id); Check(slots.Reserve(projects[3], false).RootPort is null, "release allows mixed path publication");
            Check(Catalog.ValidSlug("abc-123") && !Catalog.ValidSlug("../x") && !Catalog.ValidSlug("ABC"), "slug constraints");
            var runtimeFolder = Path.Combine(session, "runtime-acl"); Directory.CreateDirectory(runtimeFolder);
            GuideWindow.GrantRuntimeReadAccess(runtimeFolder);
            var runtimeRules = new DirectoryInfo(runtimeFolder).GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>().ToArray();
            Check(new[] { "S-1-15-2-1", "S-1-15-2-2" }.All(id => runtimeRules.Any(rule => rule.IdentityReference.Value == id && rule.AccessControlType == System.Security.AccessControl.AccessControlType.Allow && (rule.FileSystemRights & System.Security.AccessControl.FileSystemRights.ReadAndExecute) == System.Security.AccessControl.FileSystemRights.ReadAndExecute && (rule.FileSystemRights & System.Security.AccessControl.FileSystemRights.Write) == 0)), "fixed runtime grants both AppContainer SIDs read and execute only");
            var cacheBefore = Environment.GetEnvironmentVariable("DEMOMUX_COMPONENT_CACHE");
            try
            {
                var cache = Path.Combine(session, "invalid-cache"); Directory.CreateDirectory(cache);
                File.WriteAllText(Path.Combine(cache, "fixture.zip"), "tampered"); Environment.SetEnvironmentVariable("DEMOMUX_COMPONENT_CACHE", cache);
                var integrityComponent = new Component("IntegrityFixture", "1", "https://example.invalid/fixture.zip", new string('0',64), new() { ["fixture.exe"] = new string('0',64) });
                try { await integrityComponent.InstallAsync(token); throw new Exception("tampered cache installed"); }
                catch (IOException e) { Check(e.Message.Contains("SHA-256") && !File.Exists(integrityComponent.Executable("fixture.exe")), "tampered cached component rejected before installation"); }
            }
            finally { Environment.SetEnvironmentVariable("DEMOMUX_COMPONENT_CACHE", cacheBefore); }
            using var account = new AccountSession(Runtime.LoadComponents().Single(c => c.Name == "Tailscale"));
            const string profile = "[{\"id\":\"one\",\"nickname\":\"Test\",\"tailnet\":\"test\",\"account\":\"user@example.test\",\"selected\":true}]";
            account.ApplySnapshot(profile, "{\"BackendState\":\"NoState\",\"Self\":{\"DNSName\":\"\",\"UserID\":0},\"User\":null}", profile);
            Check(account.Backend == "NoState" && account.Current?.Id == "one", "transitional null user status does not crash");
            Reject(() => account.EnsureConnectedIdentity("one"), "unsynchronized account cannot enter public verification");
            const string online = "{\"BackendState\":\"Running\",\"Self\":{\"DNSName\":\"fixture.ts.net.\",\"UserID\":1},\"User\":{\"1\":{\"DisplayName\":\"Display\",\"LoginName\":\"user@example.test\"}}}";
            account.ApplySnapshot(profile, online, profile); Check(account.DisplayName == "Display" && account.Host == "fixture.ts.net", "live identity and host parsed");
            account.EnsureConnectedIdentity("one"); Check(true, "confirmed live identity allows public verification");
            Reject(() => account.EnsureConnectedIdentity(null), "missing expected identity blocks public verification");
            Reject(() => account.ApplySnapshot(profile, online.Replace("user@example.test", "other@example.test"), profile), "mismatched live identity rejected");
            Reject(() => account.ApplySnapshot(profile, online, "[]"), "profile switch during identity query rejected");
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
            // Caddy retires idle connections when a reload changes the server set.
            // Each assertion checks the new routes over a fresh connection.
            client.DefaultRequestHeaders.ConnectionClose = true;
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
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start(); var tlsPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                using var rsa = System.Security.Cryptography.RSA.Create(2048);
                var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=fixture.ts.net", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
                using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(generated.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), null, System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.UserKeySet);
                var server = Task.Run(async () => { using var socket = await listener.AcceptTcpClientAsync(); using var ssl = new System.Net.Security.SslStream(socket.GetStream()); try { await ssl.AuthenticateAsServerAsync(certificate); } catch (System.Security.Authentication.AuthenticationException) { } catch (IOException) { } });
                try { await PublicVerifier.ProbeAsync(IPAddress.Loopback, new Uri($"https://fixture.ts.net:{tlsPort}/"), "marker", token); throw new Exception("untrusted TLS accepted"); }
                catch (IOException e) { Check(e.Message.Contains("证书"), "untrusted TLS certificate classified and rejected"); }
                await server.WaitAsync(TimeSpan.FromSeconds(5));
            }
            try { await PublicVerifier.ProbeAsync(IPAddress.Loopback, new Uri($"https://fixture.ts.net:{Gateway.FreePort()}/"), "marker", token); throw new Exception("closed port accepted"); }
            catch (IOException e) { Check(e.Message.Contains("连接失败"), "connection failure classified separately"); }
            Console.WriteLine($"{passed} checks passed on {Environment.OSVersion}; .NET {Environment.Version}");
        }
        finally { Runtime.TryDelete(session); }
    }
}
