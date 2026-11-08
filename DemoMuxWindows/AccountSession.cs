using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DemoMux;

internal sealed record AccountProfile(string Id, string Nickname, string Tailnet, string Account, bool Selected);
internal sealed class AccountIdentityException : IOException
{
    public AccountIdentityException() : base("登录身份已改变，已撤下分享。请确认账号后重新启动演示。") { }
}
internal sealed class AccountSession : IDisposable
{
    private readonly Component component;
    private ChildProcess? daemon;
    private readonly Dictionary<int, ChildProcess> funnels = [];
    private readonly string pipe = "demomux-" + Guid.NewGuid().ToString("N");
    private string Socket => @"--socket=\\.\pipe\" + pipe;
    public string Host { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string Backend { get; private set; } = "Stopped";
    public string ConnectionDescription => Backend switch { "Running" => "已连接", "Stopped" => "已暂停", "NoState" or "Starting" => "正在连接", "NeedsLogin" => "需要登录", "NeedsMachineAuth" => "等待设备授权", _ => "未连接" };
    public List<AccountProfile> Profiles { get; private set; } = [];
    public AccountProfile? Current => Profiles.FirstOrDefault(p => p.Selected);
    public string IdentitySummary => string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Equals(Current?.Account, StringComparison.OrdinalIgnoreCase)
        ? Current?.Account ?? "尚未连接账号" : $"{DisplayName} · {Current?.Account ?? "未登录"}";
    public bool Running => daemon?.Running == true;
    public void InvalidateIdentity()
    {
        Backend = "Unknown"; Host = ""; DisplayName = "";
        Profiles = Profiles.Select(profile => profile with { Selected = false }).ToList();
    }
    public event Action<Uri>? Authorization;
    public AccountSession(Component component) { this.component = component; }
    public async Task StartAsync(CancellationToken token)
    {
        if (Running) return;
        daemon?.Dispose();
        var legacy = Directory.CreateDirectory(Path.Combine(Runtime.Data, "Tailscale")).FullName;
        var directory = Directory.CreateDirectory(Path.Combine(legacy, "PrivateState"));
        // The upstream Windows service ACL helper also changes owner/group. A desktop
        // user only needs a protected DACL; retain the existing owner and primary group.
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
        var state = directory.FullName;
        var oldState = Path.Combine(legacy, "tailscaled.state"); var newState = Path.Combine(state, "tailscaled.state");
        if (File.Exists(oldState) && !File.Exists(newState)) { if (!File.Exists(oldState + ".pre-0.3.2.bak")) File.Copy(oldState, oldState + ".pre-0.3.2.bak", false); File.Copy(oldState, newState); }
        daemon = ChildProcess.Start(component.Executable("tailscaled.exe"), "--windows-mode=dev", "--tun=userspace-networking", Socket, $"--statedir={state}", $"--state={newState}", "--port=0");
        for (int i = 0; i < 50 && Running; i++)
        {
            if (await Runtime.OwnsPipeAsync(pipe, daemon.Id, token)) return;
            await Task.Delay(100, token);
        }
        throw new IOException("Tailscale 启动失败。" + daemon.Output);
    }
    public async Task<string> ExecuteAsync(CancellationToken token, params string[] arguments)
    {
        if (daemon is null || !Running || !await Runtime.OwnsPipeAsync(pipe, daemon.Id, token)) throw new IOException("DemoMux 私有 Tailscale 未连接。");
        using var command = ChildProcess.Start(component.Executable("tailscale.exe"), new[] { Socket }.Concat(arguments).ToArray());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(arguments.FirstOrDefault() is "up" or "login" ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(20));
        string? last = null;
        while (command.Running)
        {
            foreach (Match match in Regex.Matches(command.Output, "https://[^\\s\"<>]+"))
            {
                if (Uri.TryCreate(match.Value.TrimEnd('.', ',', ')'), UriKind.Absolute, out var url) && url.Host is "login.tailscale.com" or "console.tailscale.com" && last != url.AbsoluteUri)
                { last = url.AbsoluteUri; Authorization?.Invoke(url); }
            }
            await Task.Delay(150, deadline.Token);
        }
        await command.WaitAsync(token);
        if (command.ExitCode != 0) throw new IOException("Tailscale 操作未完成。" + command.Output);
        return command.Output;
    }
    public async Task LoginAsync(bool add, bool reauthorize, CancellationToken token)
    {
        var path = Path.Combine(Runtime.Data, "Tailscale", "demomux-hostname.txt");
        if (!File.Exists(path)) File.WriteAllText(path, "demomux-" + Guid.NewGuid().ToString("N")[..8]);
        // Keep this private backend running between short CLI connections. The
        // daemon remains inside our Job Object; no Windows service is installed.
        var args = new List<string> { add ? "login" : "up", "--accept-dns=false", "--accept-routes=false", "--unattended=true", "--hostname=" + File.ReadAllText(path), "--timeout=30m" };
        if (reauthorize) args.Add("--force-reauth");
        await ExecuteAsync(token, args.ToArray());
        for (int attempt = 0; attempt < 40; attempt++)
        {
            await RefreshAsync(token);
            if (Backend == "Running" && Current is not null && Host.Length > 0) break;
            if (Backend is not ("Starting" or "NoState")) break;
            await Task.Delay(500, token);
        }
        if (Backend != "Running" || Current is null || Host.Length == 0) throw new IOException("账号尚未连接，请等待设备授权后重试。");
    }
    public async Task RefreshAsync(CancellationToken token)
    {
        var before = await ExecuteAsync(token, "switch", "--list", "--json");
        var status = await ExecuteAsync(token, "status", "--json", "--peers=false");
        var after = await ExecuteAsync(token, "switch", "--list", "--json");
        ApplySnapshot(before, status, after);
    }
    internal void ApplySnapshot(string before, string statusJson, string after)
    {
        using var status = JsonDocument.Parse(statusJson);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var first = JsonSerializer.Deserialize<List<AccountProfile>>(before, options) ?? [];
        var profiles = JsonSerializer.Deserialize<List<AccountProfile>>(after, options) ?? [];
        if (profiles.Count(p => p.Selected) > 1 || profiles.Select(p => p.Id).Distinct().Count() != profiles.Count || profiles.Any(p => string.IsNullOrEmpty(p.Id))
            || first.FirstOrDefault(p => p.Selected)?.Id != profiles.FirstOrDefault(p => p.Selected)?.Id) throw new AccountIdentityException();
        Profiles = profiles;
        var root = status.RootElement;
        Backend = root.GetProperty("BackendState").GetString() ?? "Stopped";
        Host = ""; DisplayName = Current?.Nickname ?? "未登录";
        if (root.TryGetProperty("Self", out var self) && self.ValueKind == JsonValueKind.Object)
        {
            Host = self.TryGetProperty("DNSName", out var dnsName) && dnsName.ValueKind == JsonValueKind.String ? dnsName.GetString()?.TrimEnd('.') ?? "" : "";
            if (self.TryGetProperty("UserID", out var uid) && root.TryGetProperty("User", out var users) && users.ValueKind == JsonValueKind.Object && users.TryGetProperty(uid.ToString(), out var user) && user.ValueKind == JsonValueKind.Object)
            {
                DisplayName = user.GetProperty("DisplayName").GetString() ?? DisplayName;
                if (Backend == "Running" && user.GetProperty("LoginName").GetString() is { } principal && !principal.Equals(Current?.Account, StringComparison.OrdinalIgnoreCase))
                    throw new AccountIdentityException();
            }
        }
    }
    public async Task FunnelAsync(int port, int localPort, CancellationToken token)
    {
        StopFunnel(port);
        if (daemon is null || !await Runtime.OwnsPipeAsync(pipe, daemon.Id, token)) throw new IOException("私有 Tailscale 连接已关闭。");
        var child = ChildProcess.Start(component.Executable("tailscale.exe"), Socket, "funnel", $"--https={port}", $"http://127.0.0.1:{localPort}");
        funnels[port] = child;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(5));
            string? last = null;
            while (child.Running)
            {
                if (child.Output.Contains("Available on the internet:", StringComparison.Ordinal)) return;
                foreach (Match match in Regex.Matches(child.Output, "https://[^\\s\"<>]+"))
                    if (Uri.TryCreate(match.Value.TrimEnd('.', ',', ')'), UriKind.Absolute, out var url) && url.Host is "login.tailscale.com" or "console.tailscale.com" && last != url.AbsoluteUri)
                    { last = url.AbsoluteUri; Authorization?.Invoke(url); }
                await Task.Delay(150, deadline.Token);
            }
            throw new IOException("Funnel 未启动。" + child.Output);
        }
        catch { StopFunnel(port); throw; }
    }
    public void StopFunnel(int port) { if (funnels.Remove(port, out var child)) child.Dispose(); }
    public void StopFunnels() { foreach (var port in funnels.Keys.ToArray()) StopFunnel(port); }
    public void Dispose() { StopFunnels(); daemon?.Dispose(); daemon = null; Backend = "Stopped"; }
}
