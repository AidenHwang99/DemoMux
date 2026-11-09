using System.IO;
using DemoMux;

internal static class ProjectSlugRegression
{
    internal static void Run(Action<bool, string> check)
    {
        var catalog = new Catalog(); var id = Guid.NewGuid();
        check(catalog.SuggestSlug("teleConnect_AI主动模式原型_155", id) == "teleconnect-ai-155", "slug matches Mac extraction of English and numbers from mixed-language names");
        check(catalog.SuggestSlug(" MeshInfo ", id) == "meshinfo" && catalog.SuggestSlug("A__B..C", id) == "a-b-c", "slug lowercases names and collapses separators");
        check(catalog.SuggestSlug(new string('a', 39) + "_bc", id) == new string('a', 39), "automatic slug truncates at 40 characters and trims a trailing separator");
        check(catalog.SuggestSlug("中文项目", id) == "project-" + id.ToString("D") && Catalog.ValidSlug(catalog.SuggestSlug("中文项目", id)), "names without English or numbers use a valid stable UUID fallback");
        var project = new Project { Name = "Local Service 8888", Kind = "port", Port = 8888, Source = "untouched-source" };
        catalog.Add(project);
        check(project.Slug == "local-service-8888", "new projects immediately receive a name-derived slug");
        check(catalog.SuggestSlug(project.Name, project.Id) == project.Slug && catalog.SuggestSlug(project.Name, id) == "project-" + id.ToString("D"), "slug generation excludes the current project while avoiding another project's path");
        catalog.Rename(project, "MeshInfo");
        check(project.Name == "MeshInfo" && project.Slug == "meshinfo" && project.Kind == "port" && project.Port == 8888 && project.Source == "untouched-source", "renaming an unpublished automatic slug preserves its project identity and source settings");
        project.Slug = "chosen-by-user"; catalog.Rename(project, "Different Name");
        check(project.Slug == "chosen-by-user", "renaming preserves a custom unpublished path");
        project.Slug = catalog.SuggestSlug(project.Name, project.Id); project.MarkPublished(false); catalog.Rename(project, "Published Rename");
        check(project.Slug == "different-name" && project.LockedSlug, "published path remains stable across display-name changes");
        project.MarkPublished(true);
        check(project.LockedSlug, "later independent publication does not unlock a previously published path");
        var independent = new Project { Name = "Independent Original", Kind = "port", Port = 8889 }; catalog.Add(independent); independent.MarkPublished(true);
        catalog.Rename(independent, "Independent Renamed");
        check(independent.Slug == "independent-renamed" && !independent.LockedSlug, "new independent-entry publication leaves the unused path eligible to track renames");
        var collision = new Project { Name = "collision", Slug = "next-name" }; catalog.Projects.Add(collision);
        catalog.Rename(independent, "Next Name");
        check(independent.Slug == "project-" + independent.Id.ToString("D"), "rename collision falls back to the project's stable identifier");
        catalog.Rename(independent, "Unique Again");
        check(independent.Slug == "unique-again", "automatic fallback continues to track later unique names");
        var persisted = Catalog.Load().Projects.Single(p => p.Id == project.Id);
        check(persisted.Name == project.Name && persisted.Slug == "different-name" && persisted.LockedSlug, "saved catalog preserves published path protection without a new data field");
        try { catalog.Rename(project, " "); throw new Exception("empty rename accepted"); }
        catch (IOException) { check(project.Name == "Published Rename", "empty rename is rejected without changing the project"); }
    }
}
