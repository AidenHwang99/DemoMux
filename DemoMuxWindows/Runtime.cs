using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace DemoMux;

internal sealed record Component(string Name, string Version, string Url, string Sha256, Dictionary<string, string> Files)
{
    public string DirectoryPath => Path.Combine(Runtime.Data, "Components", $"{Name}-{Version}");
    public string Executable(string name) => Path.Combine(DirectoryPath, name);

    private static async Task<bool> MatchesAsync(string path, string expected, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        try
        {
            await using var file = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            return hash.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public async Task<bool> IsInstalledAsync(CancellationToken token)
    {
        foreach (var (file, hash) in Files)
        {
            if (!await MatchesAsync(Executable(file), hash, token)) return false;
        }
        return Files.Count > 0;
    }

    public async Task InstallAsync(CancellationToken token)
    {
        if (await IsInstalledAsync(token)) return;
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps
            || Sha256.Length != 64 || Files.Count == 0)
        {
            throw new InvalidOperationException($"{Name} 组件发布信息未配置。");
        }
        var parent = Path.GetDirectoryName(DirectoryPath)!;
        Directory.CreateDirectory(parent);
        var stage = Directory.CreateDirectory(Path.Combine(parent, Guid.NewGuid().ToString("N"))).FullName;
        var archive = Path.Combine(stage, "archive.zip");
        var payload = Directory.CreateDirectory(Path.Combine(stage, "payload")).FullName;
        var previous = Path.Combine(stage, "previous");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            var download = deadline.Token;
            using var response = await Runtime.Internet.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, download);
            if (!response.IsSuccessStatusCode)
                throw new IOException($"{Name} 下载失败（HTTP {(int)response.StatusCode}）。");
            await using (var stream = await response.Content.ReadAsStreamAsync(download))
            await using (var destination = File.Create(archive))
            {
                var buffer = new byte[65536];
                long downloaded = 0;
                int count;
                while ((count = await stream.ReadAsync(buffer, download)) != 0)
                {
                    downloaded += count;
                    if (downloaded > 150L * 1024 * 1024) throw new IOException($"{Name} 下载包过大。");
                    await destination.WriteAsync(buffer.AsMemory(0, count), download);
                }
            }
            if (!await MatchesAsync(archive, Sha256, download)) throw new IOException($"{Name} 下载包 SHA-256 校验失败。");
            using (var zip = ZipFile.OpenRead(archive))
            {
                foreach (var (file, hash) in Files)
                {
                    var entry = zip.GetEntry(file) ?? throw new IOException($"{Name} 下载包缺少 {file}。");
                    var destination = Path.Combine(payload, file);
                    await using (var contents = entry.Open())
                    await using (var target = File.Create(destination))
                        await contents.CopyToAsync(target, download);
                    if (!await MatchesAsync(destination, hash, download)) throw new IOException($"{file} 校验失败。");
                }
            }

            token.ThrowIfCancellationRequested();
            if (Directory.Exists(DirectoryPath)) Directory.Move(DirectoryPath, previous);
            try { Directory.Move(payload, DirectoryPath); }
            catch
            {
                if (Directory.Exists(previous)) Directory.Move(previous, DirectoryPath);
                throw;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException($"{Name} 下载超时，请重试。");
        }
        finally { Runtime.TryDelete(stage); }
    }
}

internal static class Runtime
{
    public static readonly string Data = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoMux");
    public static readonly HttpClient Internet = new(new HttpClientHandler { AllowAutoRedirect = true });
    private static readonly HttpClient Local = new(new HttpClientHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        UseCookies = false
    }) { Timeout = TimeSpan.FromSeconds(2) };
    public const int PreviewPort = 8081;
    public const string PreviewUrl = "http://127.0.0.1:8081/";

    public static Component[] LoadComponents()
    {
        using var manifest = Assembly.GetExecutingAssembly().GetManifestResourceStream("DemoMux.dependencies.json")!;
        return JsonSerializer.Deserialize<Component[]>(manifest, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static int? LoadPort()
    {
        try
        {
            return int.TryParse(File.ReadAllText(Path.Combine(Data, "project-port.txt")), out var port)
                && ValidProjectPort(port) ? port : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static bool ValidProjectPort(int port) => port is >= 1 and <= 65535 && port != PreviewPort;

    public static void SavePort(int? port)
    {
        var path = Path.Combine(Data, "project-port.txt");
        File.WriteAllText(path + ".tmp", port?.ToString() ?? "");
        File.Move(path + ".tmp", path, true);
    }

    public static async Task CheckProjectAsync(int port, CancellationToken token)
    {
        try { using var response = await Local.GetAsync($"http://127.0.0.1:{port}/", HttpCompletionOption.ResponseHeadersRead, token); }
        catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or TaskCanceledException)
        {
            throw new IOException($"端口 {port} 的本机 HTTP 服务没有响应。请先启动项目。", error);
        }
    }

    public static async Task<ChildProcess> StartCaddyAsync(Component component, string sessionDirectory,
        int? projectPort, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, PreviewPort);
            listener.ExclusiveAddressUse = true;
            listener.Start();
        }
        catch (SocketException error)
        {
            throw new IOException("8081 已被占用。请停止占用它的程序后重试。", error);
        }

        // The private probe confirms the listener belongs to this launch, even after a bind race.
        var probe = "__demomux_" + Guid.NewGuid().ToString("N");
        var path = Path.Combine(sessionDirectory, "caddy.json");
        await File.WriteAllBytesAsync(path, CreateCaddyConfiguration(projectPort, probe), token);
        var child = ChildProcess.Start(component.Executable("caddy.exe"), "run", "--config", path);
        try
        {
            for (int attempt = 0; attempt < 30 && child.Running; attempt++)
            {
                try
                {
                    if (await Local.GetStringAsync(PreviewUrl + probe, token) == probe) return child;
                }
                catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or TaskCanceledException) { }
                await Task.Delay(100, token);
            }
            throw new IOException("Caddy 未能启动本机预览。 " + child.Output);
        }
        catch { child.Dispose(); throw; }
    }

    internal static byte[] CreateCaddyConfiguration(int? projectPort, string probe)
    {
        var route = projectPort is { } port
            ? new
            {
                handler = "reverse_proxy",
                upstreams = new[] { new { dial = $"127.0.0.1:{port}" } },
                headers = new
                {
                    request = new { set = new Dictionary<string, string[]> { ["Host"] = [$"127.0.0.1:{port}"] } },
                    response = new { set = new Dictionary<string, string[]> { ["Cache-Control"] = ["no-store"] } }
                }
            } as object
            : new { handler = "file_server", root = Path.Combine(AppContext.BaseDirectory, "Sample") };
        var config = new
        {
            admin = new { disabled = true, config = new { persist = false } },
            storage = new { module = "file_system", root = Path.Combine(Data, "CaddyData") },
            apps = new
            {
                http = new
                {
                    servers = new
                    {
                        preview = new
                        {
                            listen = new[] { "127.0.0.1:8081" },
                            routes = new object[]
                            {
                                new { match = new[] { new { path = new[] { "/" + probe } } },
                                      handle = new[] { new { handler = "static_response", body = probe } }, terminal = true },
                                new { handle = new[] { route } }
                            }
                        }
                    }
                }
            }
        };
        return JsonSerializer.SerializeToUtf8Bytes(config);
    }

    public static async Task<bool> OwnsPipeAsync(string name, int expectedPid, CancellationToken token)
    {
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(200, token);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid))
                throw new IOException("无法验证 Tailscale 命名管道。");
            if (pid != expectedPid) throw new IOException("Tailscale 管道属于其他进程，已停止操作。");
            return true;
        }
        catch (TimeoutException) { return false; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
