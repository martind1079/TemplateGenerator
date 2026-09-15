using FormWorks.Templates.Model;
using FormWorks.Templates.Parsing;

namespace FormWorks.Templates.Analysis;

public sealed record InventoryReport(
    string Template,
    int NodeCount,
    int PageCount,
    int MaxDepth,
    IReadOnlyList<(string FieldType, int Count)> FieldTypes,
    IReadOnlyList<(string Event, int Count)> ScriptEvents,
    IReadOnlyList<string> UnknownFieldTypes,
    IReadOnlyList<string> FieldTypeMismatches)
{
    public int ScriptCount => ScriptEvents.Sum(e => e.Count);
}

/// <summary>Counts what a template is made of, and flags anything the reader did not expect.</summary>
public static class Inventory
{
    public static InventoryReport Build(TemplateDocument doc)
    {
        var nodes = doc.AllNodes.ToList();

        var fieldTypes = nodes
            .GroupBy(n => n.FieldType, StringComparer.Ordinal)
            .Select(g => (FieldType: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.FieldType, StringComparer.Ordinal)
            .ToList();

        var events = nodes
            .SelectMany(n => n.Scripts.Keys)
            .GroupBy(k => k, StringComparer.Ordinal)
            .Select(g => (Event: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Event, StringComparer.Ordinal)
            .ToList();

        var unknown = fieldTypes
            .Select(f => f.FieldType)
            .Where(t => !TemplateLoader.KnownFieldTypes.Contains(t))
            .ToList();

        // The wrapper key and the node's own fieldType agree everywhere seen so far.
        // If they ever stop agreeing, the reader is keying off the wrong one.
        var mismatches = nodes
            .Where(n => n.DeclaredFieldType is not null && n.DeclaredFieldType != n.FieldType)
            .Select(n => $"{n.Label}: wrapper '{n.FieldType}' vs declared '{n.DeclaredFieldType}'")
            .ToList();

        return new InventoryReport(
            doc.FolderName,
            nodes.Count,
            doc.Pages.Count(),
            nodes.Max(n => n.Depth),
            fieldTypes,
            events,
            unknown,
            mismatches);
    }
}
