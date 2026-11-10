using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text.Json;
using DemoMux;

internal static class ManualRemovalRegression
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var app = Directory.CreateDirectory(Path.Combine(root, "中文 空格 & 程序")).FullName;
        var data = Directory.CreateDirectory(Path.Combine(root, "独立 数据")).FullName;
        var exe = Path.Combine(app, "DemoMux.exe"); File.WriteAllText(exe, "owned fixture");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe)));
        var manifest = Path.Combine(app, "package-manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new[] { new PackageFile("DemoMux.exe", hash) }));
        var clean = ManualRemoval.Inspect(app, data);
        check(clean.DedicatedPackage && clean.Package!.Files.Count == 1, "manual removal recognizes a fully verified dedicated package");
        check(clean.Instructions(false).Contains("请保留整个 Tailscale") && !clean.Instructions(false).Contains("- Tailscale"), "manual preserve mode excludes identity from cleanup list");
        check(clean.Instructions(true).Contains("- Tailscale") && clean.Instructions(true).Contains("- projects-v1.json"), "manual clear mode lists local identity and preferences explicitly");
        check(clean.Instructions(true).Contains("其他 DemoMux 副本") && clean.Instructions(true).Contains("不清空回收站"), "manual instructions protect other copies and recycle bin contents");
        var original = Path.Combine(app, "原始项目.html"); File.WriteAllText(original, "original project");
        var mixed = ManualRemoval.Inspect(app, data);
        check(!mixed.DedicatedPackage && mixed.Package!.Preserved.Any(p => p.Contains("原始项目.html")), "mixed package warns against removing the entire directory");
        Directory.CreateDirectory(Path.Combine(app, "用户空目录"));
        check(ManualRemoval.Inspect(app, data).Package!.Preserved.Any(p => p.Contains("用户空目录")), "unknown empty directory remains a preserved item");
        var report = (mixed with { Warning = "<script>alert('x')</script>" }).WriteInstructions(false, Path.Combine(root, "独立卸载说明"));
        var html = File.ReadAllText(report);
        check(html.Contains("&lt;script&gt;") && !html.Contains("<script>"), "manual HTML report escapes content and has no executable script");
        check(new DirectoryInfo(Path.GetDirectoryName(report)!).GetAccessControl().AreAccessRulesProtected, "manual instructions use a protected private directory");
        foreach (var directory in new[] { Path.Combine(app, "report"), Path.Combine(data, "report") })
        {
            try { mixed.WriteInstructions(true, directory); throw new Exception("unsafe report location accepted"); }
            catch (IOException) { check(!Directory.Exists(directory), "manual report cannot be written into a removal target"); }
        }
        File.WriteAllText(exe, "user modified application");
        var modified = ManualRemoval.Inspect(app, data);
        check(!modified.DedicatedPackage && modified.Warning is not null, "modified main executable prevents a dedicated-directory recommendation");
        File.WriteAllText(manifest, "[null]");
        check(ManualRemoval.Inspect(app, data).Warning is not null, "malformed package entries produce explicit feedback");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new[] { new PackageFile("../outside", hash) }));
        check(ManualRemoval.Inspect(app, data).Warning is not null, "manual preview refuses manifest traversal");
        var absent = ManualRemoval.Inspect(data, data);
        check(absent.Package is null && absent.Warning is not null && !absent.DedicatedPackage, "missing manifest allows instructions without implying directory ownership");
        check(File.ReadAllText(original) == "original project" && File.ReadAllText(exe) == "user modified application" && !Directory.EnumerateFileSystemEntries(data).Any(),
            "manual preview and instructions never delete or mutate originals or data");
    }
}
