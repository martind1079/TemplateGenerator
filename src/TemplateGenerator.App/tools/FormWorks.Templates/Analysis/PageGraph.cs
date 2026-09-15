using FormWorks.Templates.Model;

namespace FormWorks.Templates.Analysis;

/// <summary>One routing edge, and where in the template it came from.</summary>
public sealed record RouteEdge(
    string? FromPage,
    string SourceField,
    string SourceFieldType,
    string Event,
    string ToPage,
    bool MixedWithState);

public sealed record PageGraphReport(
    string Template,
    string? EntryPage,
    IReadOnlyList<string> Pages,
    IReadOnlyList<RouteEdge> Edges,
    IReadOnlyList<string> UnreachablePages,
    IReadOnlyList<string> MissingTargets,
    IReadOnlyList<(string Shape, IReadOnlyList<string> Handlers)> HandlerShapes)
{
    public int HandlerCount => Edges.Select(e => (e.SourceField, e.Event)).Distinct().Count();
    public int MixedHandlerCount => Edges.Where(e => e.MixedWithState)
        .Select(e => (e.SourceField, e.Event)).Distinct().Count();
}

/// <summary>
/// Turns the scattered changePage calls into a graph.
///
/// In the template this knowledge exists only inside handlers, so nothing states the
/// shape of the form. Recovering it here is what lets a generator emit routing as a
/// table, and what makes unreachable pages visible.
/// </summary>
public static class PageGraph
{
    public static PageGraphReport Build(TemplateDocument doc)
    {
        var pages = doc.Pages.Select(p => p.ElementName ?? p.Label).ToList();
        var edges = new List<RouteEdge>();
        var shapes = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var node in doc.AllNodes)
        {
            foreach (var (evt, script) in node.Scripts)
            {
                var targets = LuaScript.ChangePage().Matches(script);
                if (targets.Count == 0) continue;

                var mixed = LuaScript.MixesRoutingWithState(script);
                var from = node.IsPage ? node.ElementName ?? node.Label : node.Page?.ElementName ?? node.Page?.Label;

                foreach (System.Text.RegularExpressions.Match m in targets)
                {
                    edges.Add(new RouteEdge(
                        from,
                        node.Label,
                        node.FieldType,
                        evt,
                        m.Groups["page"].Value,
                        mixed));
                }

                // Grouping handlers by normalised body shows how much of the routing is
                // one rule copied. It is the measure that decides whether routing is
                // worth generating rather than hand writing.
                var shape = LuaScript.Normalise(script);
                if (!shapes.TryGetValue(shape, out var list))
                    shapes[shape] = list = [];
                list.Add($"{node.Label} {evt}");
            }
        }

        var entry = pages.FirstOrDefault();
        var reachable = Reachable(entry, edges);

        var missing = edges.Select(e => e.ToPage)
            .Distinct(StringComparer.Ordinal)
            .Where(t => !pages.Contains(t, StringComparer.Ordinal))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        return new PageGraphReport(
            doc.FolderName,
            entry,
            pages,
            edges,
            pages.Where(p => !reachable.Contains(p)).ToList(),
            missing,
            shapes.Select(kv => (kv.Key, (IReadOnlyList<string>)kv.Value))
                  .OrderByDescending(x => x.Item2.Count)
                  .ToList());
    }

    private static HashSet<string> Reachable(string? entry, IReadOnlyList<RouteEdge> edges)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (entry is null) return seen;

        var queue = new Queue<string>();
        queue.Enqueue(entry);
        seen.Add(entry);

        while (queue.Count > 0)
        {
            var page = queue.Dequeue();
            foreach (var next in edges.Where(e => e.FromPage == page).Select(e => e.ToPage))
                if (seen.Add(next)) queue.Enqueue(next);
        }
        return seen;
    }
}
