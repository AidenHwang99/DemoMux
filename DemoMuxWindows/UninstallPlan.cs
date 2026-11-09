using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;

namespace DemoMux;

internal sealed record UninstallContext(string ApplicationDirectory, string DataDirectory, bool IsolatedTest = false);
internal sealed record PackageFile(string Path, string Sha256);
internal sealed record PlannedFile(string Path, string Sha256);
internal sealed record UninstallResult(IReadOnlyList<string> Restored, IReadOnlyList<string> Recycled, IReadOnlyList<string> Unrecovered);
internal interface IUninstallRecovery { UninstallResult Restore(); }

// Preflight only. Execution must be delegated to the independently verified native helper.
internal sealed record UninstallPlan(ReadOnlyCollection<PlannedFile> Files, ReadOnlyCollection<string> Preserved)
{
    internal static UninstallPlan Create(UninstallContext context, IEnumerable<PackageFile> manifest)
    {
        var root = ValidateRoot(context.ApplicationDirectory);
        var data = ValidateRoot(context.DataDirectory);
        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoMux");
        if (!context.IsolatedTest && !data.Equals(Path.GetFullPath(standard), StringComparison.OrdinalIgnoreCase))
            throw new IOException("自定义数据目录未确认归属，停止自动卸载：" + data);
        var files = new List<PlannedFile>();
        var preserved = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest)
        {
            if (string.IsNullOrWhiteSpace(item.Path) || Path.IsPathRooted(item.Path) || item.Path.Contains(':') || item.Path.Split('/', '\\').Any(p => p is "" or "." or ".."))
                throw new IOException("包清单路径越界。");
            if (item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit)) throw new IOException("包清单校验值无效。");
            var path = Path.GetFullPath(Path.Combine(root, item.Path));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !unique.Add(path)) throw new IOException("包清单重复或越界。");
            ValidateAncestors(path);
            if (!File.Exists(path)) { preserved.Add(path + "（缺失）"); continue; }
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            if (hash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase)) files.Add(new(path, hash));
            else preserved.Add(path + "（已修改）");
        }
        if (unique.Count == 0) throw new IOException("缺少应用包清单，停止自动卸载。");
        // Enumerate without following links. Unknown files remain visible in the preview.
        Scan(root);
        return new(files.AsReadOnly(), preserved.AsReadOnly());
        void Scan(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) { preserved.Add(path + "（链接）"); continue; }
                if ((attributes & FileAttributes.Directory) != 0) Scan(path);
                else if (!unique.Contains(path)) preserved.Add(path + "（非包文件）");
            }
        }
    }
    private static string ValidateRoot(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar);
        if (full.Length < 4 || full.Equals(home, StringComparison.OrdinalIgnoreCase) || Path.GetPathRoot(full)?.TrimEnd('\\') == full)
            throw new IOException("拒绝根目录或用户主目录。");
        if (!Directory.Exists(full)) throw new IOException("目录缺失：" + full);
        ValidateAncestors(full); return full;
    }
    private static void ValidateAncestors(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("拒绝重解析点：" + current);
    }
}
