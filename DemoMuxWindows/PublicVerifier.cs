using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;

namespace DemoMux;

internal static class PublicVerifier
{
    public const string Header = "X-DemoMux-Verification";
    private static bool Public(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork && b[0] is not (0 or 10 or 127) && b[0] < 224
            && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31)
            && !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] is >= 64 and <= 127);
    }
    private static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        foreach (var resolver in new[] { "https://cloudflare-dns.com/dns-query", "https://dns.google/resolve" })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{resolver}?name={Uri.EscapeDataString(host)}&type=A");
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await client.SendAsync(request, token);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (!json.RootElement.TryGetProperty("Answer", out var answers)) continue;
                var addresses = answers.EnumerateArray().Where(a => a.GetProperty("type").GetInt32() == 1)
                    .Select(a => IPAddress.TryParse(a.GetProperty("data").GetString(), out var ip) ? ip : null)
                    .Where(ip => ip is not null && Public(ip)).Cast<IPAddress>().Distinct().Take(4).ToArray();
                if (addresses.Length > 0) return addresses;
            }
            catch (Exception e) when (!token.IsCancellationRequested && e is HttpRequestException or TaskCanceledException or JsonException) { }
        }
        throw new IOException("公共 DNS 尚未返回入口，请等待或重试。");
    }
    public static async Task VerifyAsync(Uri url, string marker, CancellationToken token, Uri? expectedRedirect = null)
    {
        if (url.Scheme != "https" || !url.Host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase) || !new[] { 443, 8443, 10000 }.Contains(url.Port)) throw new IOException("公网入口地址无效。");
        var addresses = await ResolveAsync(url.Host, token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var checks = addresses.Select(ip => ProbeAsync(ip, url, marker, cancellation.Token, expectedRedirect)).ToList();
        var errors = new List<Exception>();
        while (checks.Count > 0)
        {
            var completed = await Task.WhenAny(checks); checks.Remove(completed);
            try { await completed; cancellation.Cancel(); await Task.WhenAll(checks.Select(async task => { try { await task; } catch { } })); return; }
            catch (Exception e) { errors.Add(e); }
        }
        token.ThrowIfCancellationRequested();
        throw errors.FirstOrDefault(e => e is InvalidDataException) ?? errors[0];
    }
    private static async Task ProbeAsync(IPAddress ip, Uri url, string marker, CancellationToken token, Uri? expected)
    {
        bool certificateError = false;
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false, UseCookies = false,
            SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, errors) => { certificateError = errors != SslPolicyErrors.None; return errors == SslPolicyErrors.None; } },
            ConnectCallback = async (context, ct) =>
            {
                var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(ip, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            var current = url;
            for (int hop = 0; hop < 6; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                ValidateResponse(response, current, marker, expected);
                if ((int)response.StatusCode is not (>= 300 and < 400)) return;
                current = new Uri(current, response.Headers.Location!);
                if (expected is not null) return;
            }
            throw new InvalidDataException("项目跳转次数过多。");
        }
        catch (TaskCanceledException e) when (!token.IsCancellationRequested) { throw new IOException("公网响应超时，请重试。", e); }
        catch (HttpRequestException e) when (certificateError) { throw new IOException("HTTPS 证书校验错误。", e); }
        catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.SecureConnectionError) { throw new IOException("HTTPS 握手中断，请重试。", e); }
        catch (HttpRequestException e) { throw new IOException("公网连接失败，请检查网络后重试。", e); }
    }
    internal static void ValidateResponse(HttpResponseMessage response, Uri current, string marker, Uri? expected = null)
    {
        if (!response.Headers.TryGetValues(Header, out var values) || !values.SequenceEqual([marker])) throw new InvalidDataException("公网入口未返回本次发布标记。");
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
        {
            if (response.Headers.Location is not { } location) throw new InvalidDataException("项目跳转缺少目标。");
            var next = new Uri(current, location);
            if (next.Scheme != "https" || next.Host != current.Host || next.UserInfo.Length != 0 || next.Fragment.Length != 0
                || (expected is not null ? next != expected : next.Port != current.Port || !next.AbsolutePath.StartsWith(current.AbsolutePath.EndsWith('/') ? current.AbsolutePath : current.AbsolutePath[..(current.AbsolutePath.LastIndexOf('/') + 1)], StringComparison.Ordinal)))
                throw new InvalidDataException("项目跳转到了公网入口以外，请检查基础路径或改用独立入口。");
        }
        else if (status is < 200 or >= 400) throw new InvalidDataException($"项目错误：HTTP {status}。");
    }
}
