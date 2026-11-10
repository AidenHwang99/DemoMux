using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using DemoMux;

internal static class BundledComponentRegression
{
    internal static async Task RunAsync(string session, Action<bool, string> check)
    {
        var bundleRoot = Path.Combine(session, "offline-bundle");
        var bundle = Directory.CreateDirectory(Path.Combine(bundleRoot, "BundledComponents")).FullName;
        var bytes = "isolated component fixture"u8.ToArray();
        var zipPath = Path.Combine(bundle, "offline.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        using (var stream = zip.CreateEntry("fixture.exe").Open()) stream.Write(bytes);
        var component = new Component("OfflineFixture", Guid.NewGuid().ToString("N"), "https://127.0.0.1:1/offline.zip",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zipPath))), new() { ["fixture.exe"] = Convert.ToHexString(SHA256.HashData(bytes)) });
        List<string> progress = [];
        await component.InstallAsync(CancellationToken.None, progress.Add, bundleRoot);
        check(await component.IsInstalledAsync(CancellationToken.None), "offline bundled component installs with unreachable download URL");
        check(progress.Any(s => s.Contains("内置")) && progress.Any(s => s.Contains("SHA-256")) && !progress.Any(s => s.Contains("联网")), "bundled progress distinguishes prepare and verify without download");
        File.WriteAllText(zipPath, "corrupt");
        await component.InstallAsync(CancellationToken.None, null, bundleRoot);
        check(await component.IsInstalledAsync(CancellationToken.None), "valid installed component takes priority over bundle");
        File.WriteAllText(component.Executable("fixture.exe"), "corrupt installed");
        try { await component.InstallAsync(CancellationToken.None, null, bundleRoot); throw new Exception("accepted corrupt bundle"); }
        catch (IOException e) { check(e.Message.Contains("内置") && e.Message.Contains("修复"), "corrupt bundled archive requests repair without network fallback"); }
        File.Delete(zipPath); File.WriteAllText(Path.Combine(bundle, "complete.json"), "[]");
        try { await component.InstallAsync(CancellationToken.None, null, bundleRoot); throw new Exception("missing bundle accepted"); }
        catch (IOException e) { check(e.Message.Contains("缺失"), "missing component in full bundle cannot silently download"); }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await component.InstallAsync(canceled.Token, null, bundleRoot); throw new Exception("cancellation ignored"); }
        catch (OperationCanceledException) { check(true, "component preparation respects cancellation"); }
    }
}
