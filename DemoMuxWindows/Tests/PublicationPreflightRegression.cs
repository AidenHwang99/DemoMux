using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using DemoMux;

internal static class PublicationPreflightRegression
{
    internal sealed record Reply(int Status = 200, string? Location = null, int Delay = 0);
    internal sealed class Server : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Func<string, Reply> response;
        internal readonly ConcurrentQueue<string> Requests = new();
        internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        internal Server(Func<string, Reply> response) { this.response = response; listener.Start(); _ = AcceptAsync(); }
        private async Task AcceptAsync()
        {
            try { while (!stop.IsCancellationRequested) { var socket = await listener.AcceptTcpClientAsync(stop.Token); _ = ServeAsync(socket); } }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
        }
        private async Task ServeAsync(TcpClient socket)
        {
            using (socket)
            try
            {
                var stream = socket.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var first = await reader.ReadLineAsync(stop.Token); if (first is null) return;
                while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 }) { }
                string path = first.Split(' ')[1]; Requests.Enqueue(path);
                var reply = response(path); if (reply.Delay > 0) await Task.Delay(reply.Delay, stop.Token);
                var headers = $"HTTP/1.1 {reply.Status} Fixture\r\nConnection: close\r\nContent-Length: 2\r\n" + (reply.Location is null ? "" : $"Location: {reply.Location}\r\n") + "\r\nok";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or System.IO.IOException or ObjectDisposedException) { }
        }
        public void Dispose() { stop.Cancel(); listener.Stop(); }
    }
    internal static async Task RunAsync(Action<bool, string> check)
    {
        PublicationTarget Target(int port, bool root = false, string slug = "demo") => new(Guid.NewGuid(), port, root, slug);
        using (var server = new Server(path => new(path == "/" ? 200 : 404)))
        {
            var result = await PublicationPreflight.CheckAsync(Target(server.Port), CancellationToken.None);
            check(result.Status == PreflightStatus.Incompatible && result.Message.Contains("不支持 /p/demo/") && result.Message.Contains("独立入口"), "preflight explains root success and project-path 404 before publication");
            check((await PublicationPreflight.CheckAsync(Target(server.Port, true), CancellationToken.None)).Status == PreflightStatus.Accessible, "recommended independent entry checks the backend root");
        }
        using (var server = new Server(_ => new()))
        {
            var result = await PublicationPreflight.CheckAsync(Target(server.Port), CancellationToken.None);
            check(result.Status == PreflightStatus.Accessible, "base-path-aware backend passes local path preflight");
            check(result.Message.Contains("资源、API 和交互仍需") && !result.Message.Contains("完全兼容"), "SPA fallback success does not claim full asset or interaction compatibility");
        }
        using (var server = new Server(path => new(path == "/" ? 404 : 200)))
            check((await PublicationPreflight.CheckAsync(Target(server.Port), CancellationToken.None)).Status == PreflightStatus.Accessible, "working base path remains usable when the backend root has no page");
        foreach (var status in new[] { 401, 403 })
        {
            using var server = new Server(path => new(path == "/" ? 200 : status));
            check((await PublicationPreflight.CheckAsync(Target(server.Port), CancellationToken.None)).Status == PreflightStatus.Uncertain, "HTTP " + status + " is not accepted as compatible");
        }
        using (var server = new Server(_ => new(503)))
            check((await PublicationPreflight.CheckAsync(Target(server.Port), CancellationToken.None)).Status == PreflightStatus.Unavailable, "backend server failure has a service-unavailable result");
        using (var server = new Server(path => path == "/p/demo/" ? new(302, "/p/demo/page") : new()))
            check((await PublicationPreflight.CheckAsync(Target(server.Port), CancellationToken.None)).Status == PreflightStatus.Accessible, "same-port redirect within project prefix is accepted");
        foreach (var destination in new[] { "/login", "/p/demolition/", "http://example.invalid/", "http://127.0.0.1:1/p/demo/", "/p/demo/%2e%2e/private", "/p/demo/%5c..%5cprivate" })
        {
            using var server = new Server(path => path == "/" ? new() : new(302, destination));
            check((await PublicationPreflight.CheckAsync(Target(server.Port), CancellationToken.None)).Status == PreflightStatus.Incompatible, "redirect boundary rejected: " + destination);
            check(server.Requests.Count == 2, "rejected redirect is not followed outside the selected prefix");
        }
        using (var server = new Server(path => path == "/" || path.EndsWith("/3") ? new() : new(302, path == "/p/demo/" ? "/p/demo/1" : "/p/demo/" + (int.Parse(path.Split('/').Last()) + 1))))
            check((await PublicationPreflight.CheckAsync(Target(server.Port), CancellationToken.None)).Status == PreflightStatus.Accessible, "three same-prefix redirect hops are allowed");
        using (var server = new Server(path => path == "/" ? new() : new(302, path + "next/")))
        {
            check((await PublicationPreflight.CheckAsync(Target(server.Port), CancellationToken.None)).Status == PreflightStatus.Uncertain, "more than three redirects cannot enable publication");
            check(server.Requests.Count == 5, "redirect limit bounds requests to root plus four project-path requests");
        }
        using (var server = new Server(_ => new(Delay: 3000)))
            check((await PublicationPreflight.CheckAsync(Target(server.Port), CancellationToken.None, TimeSpan.FromMilliseconds(100))).Status == PreflightStatus.Unavailable, "one deadline bounds the entire local preflight");
        using (var cancellation = new CancellationTokenSource())
        using (var server = new Server(_ => new(Delay: 3000)))
        {
            var task = PublicationPreflight.CheckAsync(Target(server.Port), cancellation.Token); cancellation.Cancel();
            try { await task; throw new Exception("cancellation ignored"); } catch (OperationCanceledException) { check(true, "closing cancels in-flight local HTTP preflight"); }
        }
        check((await PublicationPreflight.CheckAsync(Target(Gateway.FreePort()), CancellationToken.None)).Status == PreflightStatus.Unavailable, "closed local service is diagnosed separately from path mismatch");

        var a = Target(3000, slug: "old"); var b = a with { Slug = "new" };
        PublicationPreflightResult Good(PublicationTarget target) => new(target, PreflightStatus.Accessible, "fixture", DateTimeOffset.UtcNow);
        var oldReply = new TaskCompletionSource<PublicationPreflightResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newReply = new TaskCompletionSource<PublicationPreflightResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var session = new PublicationPreflightSession((target, _) => target == a ? oldReply.Task : newReply.Task))
        {
            var first = session.CheckAsync(a, false); var second = session.CheckAsync(b, false);
            newReply.SetResult(Good(b)); await second; oldReply.SetResult(Good(a)); await first;
            check(session.Current?.Target == b, "late old-path response cannot overwrite the newest selection");
            session.Invalidate(); check(session.Current is null, "mode changes immediately invalidate the old compatibility result");
        }
        int requests = 0;
        using (var session = new PublicationPreflightSession((target, _) => { Interlocked.Increment(ref requests); return Task.FromResult(Good(target)); }))
        {
            await Task.WhenAll(session.CheckAsync(a, true), session.CheckAsync(b, true));
            check(requests == 1 && session.Current?.Target == b, "400ms debounce probes only the final edited path");
            await session.EnsureFreshAsync(b); check(requests == 1, "publish click reuses a matching result within 30 seconds");
        }
        int refreshes = 0;
        using (var session = new PublicationPreflightSession((target, _) => Task.FromResult(Good(target) with { CheckedAt = DateTimeOffset.UtcNow.AddSeconds(++refreshes == 1 ? -31 : 0) })))
        {
            await session.CheckAsync(a, false); await session.EnsureFreshAsync(a);
            check(refreshes == 2, "publish click rechecks an expired result");
        }
        var late = new TaskCompletionSource<PublicationPreflightResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new PublicationPreflightSession((_, _) => late.Task); var inFlight = closed.CheckAsync(a, false);
        closed.Dispose(); late.SetResult(Good(a)); await inFlight;
        check(closed.Current is null, "dialog closure discards even a transport response that ignores cancellation");
        var cached = Good(a);
        check(!cached.FreshFor(a with { Root = true }, cached.CheckedAt) && !cached.FreshFor(a with { Port = 3001 }, cached.CheckedAt)
            && !cached.FreshFor(a with { ProjectId = Guid.NewGuid() }, cached.CheckedAt) && !cached.FreshFor(b, cached.CheckedAt), "cache identity includes project, port, mode and slug");
        check(!cached.FreshFor(a, cached.CheckedAt.AddSeconds(31)) && !cached.FreshFor(a, cached.CheckedAt.AddSeconds(-1)), "expired or future-dated results cannot enable publication");
        using var response404 = new HttpResponseMessage(HttpStatusCode.NotFound);
        response404.Headers.Add(PublicVerifier.Header, "marker");
        try { PublicVerifier.ValidateResponse(response404, new Uri("https://fixture.ts.net/p/demo/"), "marker"); throw new Exception("404 accepted"); }
        catch (ProjectHttpException error) { check(error.StatusCode == 404, "public project HTTP failure preserves its numeric status code"); }
    }
}
