using System.IO;
using System.Net;
using System.Text.Json;

namespace DemoMux;

// This class only inspects files and writes instructions. It never removes application or data files.
internal sealed record ManualRemoval(string ApplicationDirectory, string DataDirectory, UninstallPlan? Package, string? Warning)
{
    internal static readonly string[] RuntimeItems = ["Components", "Sessions", "CaddyData", "WebView2"];
    internal static readonly string[] IdentityItems = ["Tailscale", "projects-v1.json", "project-port.txt", "project-port.txt.pre-0.3.2.bak"];
    internal bool DedicatedPackage => Package is { Files.Count: > 0, Preserved.Count: 0 };
    internal static ManualRemoval Inspect(string applicationDirectory, string dataDirectory)
    {
        var app = Path.GetFullPath(applicationDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var data = Path.GetFullPath(dataDirectory);
        try
        {
            var path = Path.Combine(app, "package-manifest.json");
            var manifest = JsonSerializer.Deserialize<PackageFile[]>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new IOException("包清单为空。");
            // The manifest cannot hash itself. It remains a preserved item during automatic preflight;
            // for this manual-only recommendation verify the rest and account for that one metadata file.
            var plan = UninstallPlan.InspectPackage(app, manifest);
            var preserved = plan.Preserved.Where(p => !p.Equals(path + "（非包文件）", StringComparison.OrdinalIgnoreCase)).ToList();
            plan = plan with { Preserved = preserved.AsReadOnly() };
            if (!plan.Files.Any(p => Path.GetFileName(p.Path).Equals("DemoMux.exe", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("清单未能确认 DemoMux.exe 的归属。");
            return new(app, data, plan, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new(app, data, null, "无法完整校验包文件：" + e.Message + " 请勿直接删除整个程序目录。");
        }
    }
    internal string Instructions(bool clearIdentity)
    {
        var scope = clearIdentity ? RuntimeItems.Concat(IdentityItems) : RuntimeItems;
        var text = $"DemoMux 手动卸载\n\n本说明不执行删除。请在 DemoMux 完全退出后，由你在 Explorer 中选择文件并送入回收站。\n\n" +
            $"1. 程序位置：{ApplicationDirectory}\n" +
            (DedicatedPackage ? "当前包文件校验通过，未发现混放或修改文件。请再次检查退出后的目录内容；仅在仍是 DemoMux 专用目录时，将此程序文件夹送入回收站。\n"
                : "目录未确认是专用包目录。不要删除整个文件夹；只删除下方已校验包文件，保留其他文件和修改过的文件。\n") +
            $"\n2. 数据位置：{DataDirectory}\n" +
            (clearIdentity ? "你选择了手动清除项目列表、本机登录与偏好。" : "你选择了保留项目列表、本机登录与偏好。请保留整个 Tailscale 目录、projects-v1.json 和旧偏好文件。") +
            "\n可手动清理以下 DemoMux 自有项目（仅处理此数据位置内的同名项，操作前检查是否混放原始文件）：\n" + string.Join("\n", scope.Select(name => "- " + name)) +
            "\n\n3. 保留原始 HTML、导入项目文件夹、外部本机服务、其他 DemoMux 副本，以及系统独立安装的 Tailscale、Caddy、Edge、WebView2、.NET。\n" +
            "不删除在线账号或云端设备记录，不清空回收站。若提示文件占用，先确认 DemoMux 已完全退出；不要结束其他系统服务。\n" +
            "清理后的空目录可手动移除。若保留登录和偏好，重新解压后可沿用，组件需要重新下载。\n" +
            "本说明保存在独立临时目录；阅读完后可将本说明及其临时目录一并手动删除。\n";
        if (Warning is { } warning) text += "\n校验反馈：" + warning + "\n";
        if (Package is { } package)
        {
            text += "\n已校验包文件：\n" + string.Join("\n", package.Files.Select(p => Path.GetRelativePath(ApplicationDirectory, p.Path)));
            text += "\n\n另可手动移除包清单 package-manifest.json。\n保留／未确认项目：\n" + string.Join("\n", package.Preserved);
        }
        return text;
    }
    internal string WriteInstructions(bool clearIdentity, string temporaryDirectory)
    {
        // Keep instructions outside the two locations the user may remove.
        var directory = Path.GetFullPath(temporaryDirectory);
        foreach (var root in new[] { ApplicationDirectory, DataDirectory })
            if (directory.Equals(root, StringComparison.OrdinalIgnoreCase) || directory.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("说明文件必须保存在程序和数据目录之外。");
        Directory.CreateDirectory(directory);
        var security = new System.Security.AccessControl.DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(security);
        var path = Path.Combine(directory, "DemoMux-手动卸载.html");
        var text = Instructions(clearIdentity);
        File.WriteAllText(path, "<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><title>DemoMux 手动卸载</title>" +
            "<meta name=\"viewport\" content=\"width=device-width\"><style>body{background:#100d11;color:#faf2f5;max-width:960px;margin:32px auto;padding:24px;font:16px/1.65 system-ui}pre{white-space:pre-wrap;overflow-wrap:anywhere}</style><h1>DemoMux 手动卸载</h1><pre>" +
            WebUtility.HtmlEncode(text) + "</pre></html>");
        return path;
    }
}
