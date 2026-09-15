using FormWorks.Templates.Model;

namespace FormWorks.Templates.Parsing;

/// <summary>
/// Finds template folders on disk.
///
/// Templates live in a folder the caller names, or in a <c>templates</c> folder found by
/// walking up from the working directory, so the tool behaves the same whichever folder it
/// is run from. Nothing outside that is searched and no path is baked in: a template estate
/// carries client names and business rules, and where one is kept is the caller's business.
/// </summary>
public static class EstateLocator
{
    /// <summary>The folder templates are kept in, beside the project rather than anywhere else.</summary>
    public const string FolderName = "templates";

    public static string Resolve(string? explicitRoot)
    {
        if (explicitRoot is not null)
        {
            return Directory.Exists(explicitRoot)
                ? explicitRoot
                : throw new DirectoryNotFoundException($"No template folder at '{explicitRoot}'.");
        }

        return FindNearby(Directory.GetCurrentDirectory())
               ?? throw new DirectoryNotFoundException(
                   $"No '{FolderName}' folder here or in any folder above. Pass --estate <path>.");
    }

    /// <summary>
    /// The nearest templates folder at or above a starting point.
    ///
    /// Walked rather than assumed, because the command line is run from the tool's own
    /// folder as often as from the root of the repository.
    /// </summary>
    private static string? FindNearby(string start)
    {
        var directory = new DirectoryInfo(start);

        while (directory is not null)
        {
            var candidate = System.IO.Path.Combine(directory.FullName, FolderName);
            if (Directory.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>Every folder under the root that holds a template.json, in name order.</summary>
    public static IReadOnlyList<string> FindTemplateFolders(string root)
        => Directory.EnumerateDirectories(root)
            .Where(d => File.Exists(System.IO.Path.Combine(d, "template.json")))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Keeps only the highest version of each family.
    ///
    /// A family with no version number in its folder name is kept as it is, since
    /// there is nothing to compare it against.
    /// </summary>
    public static IReadOnlyList<TemplateDocument> LatestPerFamily(IEnumerable<TemplateDocument> documents)
        => documents
            .GroupBy(d => d.Family, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(d => d.Version ?? -1).First())
            .OrderBy(d => d.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Loads every template under the root. Failures are collected rather than thrown,
    /// because a single unreadable template should not stop an estate-wide report.
    /// </summary>
    public static (List<TemplateDocument> Loaded, List<(string Folder, Exception Error)> Failed)
        LoadAll(string root)
    {
        var loaded = new List<TemplateDocument>();
        var failed = new List<(string, Exception)>();

        foreach (var folder in FindTemplateFolders(root))
        {
            try { loaded.Add(TemplateLoader.LoadFolder(folder)); }
            catch (Exception ex) { failed.Add((System.IO.Path.GetFileName(folder)!, ex)); }
        }

        return (loaded, failed);
    }
}
