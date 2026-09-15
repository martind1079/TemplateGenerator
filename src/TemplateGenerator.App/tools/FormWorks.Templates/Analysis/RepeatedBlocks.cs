using System.Text.RegularExpressions;
using FormWorks.Templates.Model;

namespace FormWorks.Templates.Analysis;

/// <summary>
/// Sibling containers that differ only by an index and hold the same fields, such as
/// two borrower groups each holding a name and a date of birth.
/// </summary>
public sealed record StructuralRepeat(
    string Stem,
    string FieldType,
    IReadOnlyList<int> Indices,
    string Signature,
    string? Page);

/// <summary>
/// A repeat the template has already flattened, where the index sits inside the field
/// name: six tenants as Tenant1Name, Tenant1Age, Tenant2Name and so on.
/// </summary>
public sealed record FlattenedRepeat(
    string Stem,
    IReadOnlyList<int> Indices,
    IReadOnlyList<string> Fields,
    string? Container,
    string? Page)
{
    public int FieldCount => Indices.Count * Fields.Count;
}

/// <summary>
/// Siblings numbered like a repeat that are nothing of the sort: the editor numbers
/// sections as they are added, so one page carries eight sections named alike holding
/// entirely different questions.
/// </summary>
public sealed record IncidentalNumbering(
    string Stem,
    string FieldType,
    int Count,
    string? Page);

public sealed record RepeatedBlockReport(
    string Template,
    IReadOnlyList<StructuralRepeat> Structural,
    IReadOnlyList<FlattenedRepeat> Flattened,
    IReadOnlyList<IncidentalNumbering> Incidental);

/// <summary>
/// Finds genuine repeats, and separates them from names that merely end in a number.
///
/// Whether a repeated block becomes a collection or stays a flat set of numbered
/// properties changes the emitted model, so it has to be answered from what the estate
/// actually contains. Keying on the name alone gets this wrong in both directions: it
/// reports numbered sections that share nothing, and it misses flattened repeats whose
/// index sits in the middle of the name.
/// </summary>
public static partial class RepeatedBlocks
{
    /// <summary>Splits Tenant1Name into stem, index and field, or Borrower1 into stem and index.</summary>
    [GeneratedRegex(@"^(?<stem>[A-Za-z_][A-Za-z0-9_]*?)(?<index>\d{1,2})(?<field>[A-Za-z][A-Za-z0-9_]*)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IndexedName();

    public static RepeatedBlockReport Build(TemplateDocument doc)
    {
        var structural = new List<StructuralRepeat>();
        var incidental = new List<IncidentalNumbering>();
        var flattened = new List<FlattenedRepeat>();

        foreach (var container in doc.AllNodes)
        {
            var parsed = container.Children
                .Select(c => (Node: c, Match: IndexedName().Match(Leaf(c) ?? "")))
                .Where(x => x.Match.Success)
                .ToList();

            // Siblings numbered with nothing after the index. A repeat only if they hold
            // the same thing; otherwise it is the editor counting.
            foreach (var group in parsed
                         .Where(x => !x.Match.Groups["field"].Success)
                         .GroupBy(x => (Stem: x.Match.Groups["stem"].Value, x.Node.FieldType)))
            {
                if (group.Count() < 2) continue;

                var signatures = group.Select(x => Signature(x.Node)).Distinct(StringComparer.Ordinal).ToList();
                var indices = group.Select(x => int.Parse(x.Match.Groups["index"].Value)).Order().ToList();

                if (signatures.Count == 1 && signatures[0].Length > 0)
                {
                    structural.Add(new StructuralRepeat(
                        group.Key.Stem, group.Key.FieldType, indices, signatures[0], container.Page?.ElementName ?? PageOf(container)));
                }
                else
                {
                    incidental.Add(new IncidentalNumbering(
                        group.Key.Stem, group.Key.FieldType, indices.Count, container.Page?.ElementName ?? PageOf(container)));
                }
            }

            // Fields carrying the index inside the name. A repeat when two or more indices
            // offer the same set of fields.
            foreach (var group in parsed
                         .Where(x => x.Match.Groups["field"].Success)
                         .GroupBy(x => x.Match.Groups["stem"].Value, StringComparer.Ordinal))
            {
                var byIndex = group
                    .GroupBy(x => int.Parse(x.Match.Groups["index"].Value))
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Match.Groups["field"].Value)
                        .OrderBy(f => f, StringComparer.Ordinal).ToList());

                if (byIndex.Count < 2) continue;

                var shapes = byIndex.Values
                    .Select(f => string.Join("|", f))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (shapes.Count != 1) continue;

                flattened.Add(new FlattenedRepeat(
                    group.Key,
                    byIndex.Keys.Order().ToList(),
                    byIndex.Values.First(),
                    Leaf(container),
                    container.Page?.ElementName ?? PageOf(container)));
            }
        }

        return new RepeatedBlockReport(
            doc.FolderName,
            structural.OrderByDescending(r => r.Indices.Count).ToList(),
            flattened.OrderByDescending(r => r.FieldCount).ToList(),
            incidental.OrderByDescending(r => r.Count).ToList());
    }

    /// <summary>What a container holds, one level down, as a comparable string.</summary>
    private static string Signature(TemplateNode node)
        => string.Join(",", node.Children.Select(c => $"{c.FieldType}:{Leaf(c)}"));

    private static string? Leaf(TemplateNode node)
        => node.ElementName ?? node.Name?.Split('.').Last();

    private static string? PageOf(TemplateNode node)
        => node.IsPage ? node.ElementName : node.Page?.ElementName;
}
