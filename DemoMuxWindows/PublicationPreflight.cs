using System.IO;
using System.Net.Http;

namespace DemoMux;

internal enum PreflightStatus { Accessible, Incompatible, Unavailable, Uncertain }
internal sealed record PublicationTarget(Guid ProjectId, int Port, bool Root, string Slug)
{
    internal string Path => Root ? "/" : $"/p/{Slug}/";
}
internal sealed record PublicationPreflightResult(PublicationTarget Target, PreflightStatus Status, string Message, DateTimeOffset CheckedAt)
{
    internal bool FreshFor(PublicationTarget target, DateTimeOffset now) => Target == target && Status == PreflightStatus.Accessible
        && now >= CheckedAt && now - CheckedAt <= TimeSpan.FromSeconds(30);
}
internal static class PublicationPreflight
{
    private sealed record Probe(int? Code, string? Problem = null, bool Unavailable = false, bool RedirectRejected = false)
    {
        internal bool Accessible => Code is >= 200 and < 300;
    }
    internal static async Task<PublicationPreflightResult> CheckAsync(PublicationTarget target, CancellationToken token, TimeSpan? timeout = null)
    {
        if (!Runtime.ValidProjectPort(target.Port) || !target.Root && !Catalog.ValidSlug(target.Slug)) throw new ArgumentException("预检目标无效。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(8));
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
        PublicationPreflightResult Result(PreflightStatus status, string message) => new(target, status, message, DateTimeOffset.UtcNow);
        try
        {
            var homeTask = ProbeAsync(client, target.Port, "/", deadline.Token);
            var pathTask = target.Root ? homeTask : ProbeAsync(client, target.Port, target.Path, deadline.Token);
            await Task.WhenAll(homeTask, pathTask);
            token.ThrowIfCancellationRequested();
            var home = await homeTask; var path = await pathTask;
            if (path.Accessible) return Result(PreflightStatus.Accessible, "本机路径可访问。资源、API 和交互仍需浏览器验证，发布后还会进行公网校验。");
            if (!target.Root && home.Accessible && path.Code == 404)
                return Result(PreflightStatus.Incompatible, $"这个项目可以从首页访问，但不支持 {target.Path}。建议使用独立入口，无需修改项目。");
            if (path.RedirectRejected) return Result(PreflightStatus.Incompatible, target.Root
                ? "项目首页跳转到了其他地址，无法确认这个入口可用。请检查本机项目的跳转配置。"
                : "目标地址跳转到了当前端口或项目路径以外，无法按此路径发布。请检查项目配置，或返回独立入口。");
            if (path.Code is 401 or 403) return Result(PreflightStatus.Uncertain, $"项目返回 HTTP {path.Code}，需要身份或访问权限，无法确认该路径是否可用。请检查后重新检测。");
            if (path.Unavailable || path.Code >= 500) return Result(PreflightStatus.Unavailable, path.Problem ?? $"项目服务返回 HTTP {path.Code}，请先确认本机服务正常，再重新检测。");
            return Result(PreflightStatus.Uncertain, path.Problem ?? $"目标路径返回 HTTP {path.Code}，无法确认可用性。请检查项目路径或重新检测。");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return Result(PreflightStatus.Unavailable, "本机检测超时（8 秒内未完成）。请确认项目已启动，再重新检测。");
        }
    }
    private static async Task<Probe> ProbeAsync(HttpClient client, int port, string prefix, CancellationToken token)
    {
        var current = new Uri($"http://127.0.0.1:{port}{prefix}");
        try
        {
            for (int redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                int code = (int)response.StatusCode;
                if (code is not (301 or 302 or 303 or 307 or 308)) return new(code);
                if (response.Headers.Location is not { } location || !Uri.TryCreate(current, location, out var next)) return new(code, "项目跳转缺少有效目标，无法确认该路径是否可用。");
                if (next.Scheme != "http" || next.Host != "127.0.0.1" || next.Port != port || next.UserInfo.Length != 0 || next.Fragment.Length != 0
                    || !next.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
                    || Uri.UnescapeDataString(next.AbsolutePath).Split('/').Any(p => p is "." or ".." || p.Contains('\\')))
                    return new(code, RedirectRejected: true);
                if (redirects >= 3) return new(code, "项目跳转超过三次，无法确认可用性。请检查项目跳转或重新检测。");
                current = next;
            }
        }
        catch (HttpRequestException) { return new(null, "本机服务无法连接，请先启动项目并确认端口，再重新检测。", Unavailable: true); }
    }
}

// One dialog owns one session. Versioning prevents late responses from enabling publication after input changes or closure.
internal sealed class PublicationPreflightSession : IDisposable
{
    private readonly Func<PublicationTarget, CancellationToken, Task<PublicationPreflightResult>> check;
    private readonly object gate = new();
    private CancellationTokenSource? pending;
    private long generation;
    private bool disposed;
    private PublicationPreflightResult? current;
    internal PublicationPreflightSession(Func<PublicationTarget, CancellationToken, Task<PublicationPreflightResult>>? check = null) =>
        this.check = check ?? ((target, token) => PublicationPreflight.CheckAsync(target, token));
    internal PublicationPreflightResult? Current { get { lock (gate) return current; } }
    internal void Invalidate()
    {
        CancellationTokenSource? old;
        lock (gate) { generation++; current = null; old = pending; pending = null; }
        Cancel(old);
    }
    internal async Task<PublicationPreflightResult?> CheckAsync(PublicationTarget target, bool debounce)
    {
        CancellationTokenSource source, old; long version;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            version = ++generation; current = null; old = pending!; pending = source = new();
        }
        Cancel(old);
        try
        {
            if (debounce) await Task.Delay(400, source.Token);
            var result = await check(target, source.Token);
            lock (gate)
            {
                if (disposed || generation != version || source.IsCancellationRequested || result.Target != target) return null;
                return current = result;
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { return null; }
        finally
        {
            lock (gate) { if (pending == source) pending = null; }
            source.Dispose();
        }
    }
    internal async Task<PublicationPreflightResult?> EnsureFreshAsync(PublicationTarget target)
    {
        var cached = Current;
        return cached?.FreshFor(target, DateTimeOffset.UtcNow) == true ? cached : await CheckAsync(target, false);
    }
    public void Dispose()
    {
        lock (gate) { disposed = true; }
        Invalidate();
    }
    private static void Cancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); } catch (ObjectDisposedException) { }
    }
}

internal sealed class ProjectHttpException(int statusCode) : IOException($"项目错误：HTTP {statusCode}。")
{
    internal int StatusCode { get; } = statusCode;
}
