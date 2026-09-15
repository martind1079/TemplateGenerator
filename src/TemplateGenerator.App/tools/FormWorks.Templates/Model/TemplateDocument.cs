using System.Text.RegularExpressions;

namespace FormWorks.Templates.Model;

/// <summary>
/// One template file, with the family and version recovered from its folder name.
///
/// The estate keeps superseded versions alongside current ones, so roughly a quarter
/// of the folders are history. Counting the estate without splitting family from
/// version double counts it, which is why the split lives here rather than in each
/// caller.
/// </summary>
public sealed partial class TemplateDocument
{
    public required string FolderName { get; init; }
    public required string Path { get; init; }
    public required TemplateNode Root { get; init; }

    /// <summary>Folder name with the trailing version stripped, e.g. "My example template".</summary>
    public required string Family { get; init; }

    /// <summary>The trailing version number, or null when the folder does not carry one.</summary>
    public required int? Version { get; init; }

    public string Title => Root.Title ?? FolderName;

    public IEnumerable<TemplateNode> Pages => Root.Children.Where(c => c.IsPage);

    public IEnumerable<TemplateNode> AllNodes => Root.DescendantsAndSelf();

    [GeneratedRegex(@"^(?<family>.*?)\s+V(?<version>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionSuffix();

    public static (string Family, int? Version) SplitVersion(string folderName)
    {
        var m = VersionSuffix().Match(folderName);
        return m.Success
            ? (m.Groups["family"].Value, int.Parse(m.Groups["version"].Value))
            : (folderName, null);
    }

    public override string ToString() => FolderName;
}
