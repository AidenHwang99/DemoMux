using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DemoMux;

internal sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Kind { get; set; } = "html";
    public string Source { get; set; } = "";
    public string Entry { get; set; } = "index.html";
    public int Port { get; set; }
    public bool LockedSlug { get; set; }
    public string PublicPath => $"/p/{Slug}/";
    public string LocalPath => $"/project/{Id:N}/";
    public string Description => Kind == "port" ? $"本机端口 · {Port}" : $"{(Kind == "html" ? "单文件 HTML" : "HTML 项目文件夹")} · {Source}";
}

internal sealed class Catalog
{
    public int Version { get; set; } = 1;
    public List<Project> Projects { get; set; } = [];
    public Dictionary<string, Guid> RootOwners { get; set; } = [];
    public bool OpenHome { get; set; } = true;
    private static string FilePath => Path.Combine(Runtime.Data, "projects-v1.json");
    public static bool ValidSlug(string value) => Regex.IsMatch(value, "^[a-z0-9](?:[a-z0-9-]{0,46}[a-z0-9])?$");
    public static Catalog Load()
    {
        Directory.CreateDirectory(Runtime.Data);
        if (File.Exists(FilePath))
        {
            var value = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(FilePath)) ?? throw new IOException("项目目录为空。");
            if (value.Version != 1 || value.Projects.Any(p => !ValidSlug(p.Slug) || p.Kind is not ("html" or "folder" or "port") || (p.Kind == "port" && !Runtime.ValidProjectPort(p.Port)))
                || value.Projects.Select(p => p.Id).Distinct().Count() != value.Projects.Count
                || value.Projects.Select(p => p.Slug).Distinct().Count() != value.Projects.Count)
                throw new IOException("项目目录版本不兼容或包含重复条目，原文件已保留。");
            return value;
        }
        var result = new Catalog();
        var legacy = Path.Combine(Runtime.Data, "project-port.txt");
        if (File.Exists(legacy))
        {
            if (!File.Exists(legacy + ".pre-0.3.2.bak")) File.Copy(legacy, legacy + ".pre-0.3.2.bak", false);
            if (Runtime.LoadPort() is { } port) result.Add(new Project { Name = $"本机端口 {port}", Kind = "port", Port = port });
        }
        result.Save();
        return result;
    }
    public void Save()
    {
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
    public void Add(Project project)
    {
        if (Projects.Any(p => p.Kind == project.Kind && (p.Kind == "port" ? p.Port == project.Port : p.Source.Equals(project.Source, StringComparison.OrdinalIgnoreCase) && p.Entry == project.Entry)))
            throw new IOException("这个项目已在列表中。");
        var suggested = Regex.Replace(project.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        project.Slug = suggested.Length is > 0 and <= 48 && !Projects.Any(p => p.Slug == suggested) ? suggested : "project-" + project.Id.ToString("N");
        Projects.Add(project);
        Save();
    }
}

internal sealed record PreparedContent(string? Root, string Entry, int Port) : IDisposable
{
    public void Dispose() { if (Root is not null) Runtime.TryDelete(Root); }
    public static async Task<PreparedContent> PrepareAsync(Project project, string session, CancellationToken token)
    {
        if (project.Kind == "port")
        {
            await Runtime.CheckProjectAsync(project.Port, token);
            return new(null, "", project.Port);
        }
        return await Task.Run(() =>
        {
            var output = Path.Combine(session, Guid.NewGuid().ToString("N"));
            try
            {
                CheckPlain(project.Source);
                Directory.CreateDirectory(output);
                if (project.Kind == "html") File.Copy(project.Source, Path.Combine(output, "index.html"));
                else
                {
                    var source = Path.GetFullPath(project.Source).TrimEnd(Path.DirectorySeparatorChar);
                    var data = Path.GetFullPath(Runtime.Data);
                    if (source == Path.GetPathRoot(source)?.TrimEnd('\\') || Within(data, source) || Within(source, data))
                        throw new IOException("请选择专用项目文件夹，范围不能包含 DemoMux 数据目录。");
                    ValidateEntry(source, project.Entry);
                    CopyFolder(source, output, token);
                    if (!File.Exists(Path.Combine(output, project.Entry))) throw new IOException("入口 HTML 已失效或被排除，请重新定位。");
                }
                token.ThrowIfCancellationRequested();
                return new PreparedContent(output, project.Kind == "html" ? "index.html" : project.Entry.Replace('\\', '/'), 0);
            }
            catch { Runtime.TryDelete(output); throw; }
        }, token);
    }
    private static bool Within(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    private static void CheckPlain(string path)
    {
        if ((!File.Exists(path) && !Directory.Exists(path)) || (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Hidden)) != 0)
            throw new IOException("源文件失效或包含链接，请重新定位后启动。");
    }
    private static void ValidateEntry(string root, string entry)
    {
        if (Path.IsPathRooted(entry) || entry.Split('/', '\\').Any(p => p.Length == 0 || p.StartsWith('.'))) throw new IOException("入口必须是文件夹内的 HTML。");
        var current = root;
        foreach (var part in entry.Split('/', '\\')) { current = Path.Combine(current, part); CheckPlain(current); }
        if (!File.Exists(current) || !new[] { ".html", ".htm" }.Contains(Path.GetExtension(current).ToLowerInvariant())) throw new IOException("请选择 HTML 入口。");
    }
    private static void CopyFolder(string source, string target, CancellationToken token)
    {
        foreach (var child in Directory.EnumerateFileSystemEntries(source))
        {
            token.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(child);
            if (Path.GetFileName(child).StartsWith('.') || (attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0) continue;
            var destination = Path.Combine(target, Path.GetFileName(child));
            if ((attributes & FileAttributes.Directory) != 0) { Directory.CreateDirectory(destination); CopyFolder(child, destination, token); }
            else File.Copy(child, destination);
        }
    }
}

internal sealed record Share(Guid ProjectId, int? RootPort)
{
    public string Marker { get; } = Guid.NewGuid().ToString("N");
    public bool Verified { get; set; }
    public Uri Url(string host, Project project) => new($"https://{host}{(RootPort is { } port && port != 443 ? ":" + port : "")}{(RootPort is null ? project.PublicPath : "/")}");
}

internal sealed class PublicationSlots
{
    public Dictionary<Guid, Share> Shares { get; } = [];
    public Guid? Home { get; set; }
    public int? NextRoot => new[] { 8443, 10000, 443 }.Cast<int?>().FirstOrDefault(port => Shares.Values.All(s => s.RootPort != port));
    public Share Reserve(Project project, bool root)
    {
        if (Shares.Count >= 3) throw new IOException("三个公网名额已用完，请先取消一项。");
        var port = root ? NextRoot ?? throw new IOException("没有可用独立入口。") : (int?)null;
        var share = new Share(project.Id, port);
        Shares.Add(project.Id, share);
        return share;
    }
    public void Release(Guid id) { Shares.Remove(id); if (Home == id) Home = Shares.Keys.Cast<Guid?>().FirstOrDefault(); }
    public void Clear() { Shares.Clear(); Home = null; }
}
