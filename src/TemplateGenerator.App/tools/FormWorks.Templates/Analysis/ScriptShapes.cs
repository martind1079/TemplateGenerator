using FormWorks.Templates.Model;

namespace FormWorks.Templates.Analysis;

public sealed record ScriptShape(
    string Event,
    string Shape,
    int Count,
    IReadOnlyList<string> Examples,
    string Sample);

public sealed record ScriptShapeReport(
    int TotalHandlers,
    IReadOnlyList<ScriptShape> Shapes,
    IReadOnlyList<(string Event, int Handlers, int DistinctShapes)> ByEvent)
{
    /// <summary>
    /// How many handlers the most common shapes account for.
    ///
    /// This is the number that decides what conversion costs. If a small number of
    /// shapes cover most handlers, then most of pass three is one rule applied many
    /// times and a person only has to read the tail.
    /// </summary>
    public IEnumerable<(int Shapes, int Handlers, double Share)> CoverageCurve(params int[] cutoffs)
    {
        var ordered = Shapes.OrderByDescending(s => s.Count).ToList();
        foreach (var n in cutoffs)
        {
            var taken = ordered.Take(n).Sum(s => s.Count);
            yield return (Math.Min(n, ordered.Count), taken, TotalHandlers == 0 ? 0 : (double)taken / TotalHandlers);
        }
    }

    /// <summary>Shapes that occur exactly once: the handlers nobody can avoid reading.</summary>
    public int SingletonShapes => Shapes.Count(s => s.Count == 1);
}

/// <summary>
/// Groups handlers by what they look like once names and literals are stripped out.
///
/// This is the measurement that decides how much of pass three is genuine judgement.
/// A category with many handlers but few shapes is a template applied repeatedly, and
/// a generator can emit it. A category where almost every handler is its own shape has
/// to be read by a person.
/// </summary>
public static class ScriptShapes
{
    public static ScriptShapeReport Build(IEnumerable<TemplateDocument> docs, string? onlyEvent = null)
    {
        var buckets = new Dictionary<(string Event, string Shape), List<(string Where, string Body)>>();
        var total = 0;

        foreach (var doc in docs)
        {
            foreach (var node in doc.AllNodes)
            {
                foreach (var (evt, script) in node.Scripts)
                {
                    if (onlyEvent is not null && !evt.Equals(onlyEvent, StringComparison.OrdinalIgnoreCase))
                        continue;

                    total++;
                    var key = (evt, LuaScript.Normalise(script));
                    if (!buckets.TryGetValue(key, out var list))
                        buckets[key] = list = [];
                    list.Add(($"{doc.FolderName}: {node.Label}", script));
                }
            }
        }

        var shapes = buckets
            .Select(kv => new ScriptShape(
                kv.Key.Event,
                kv.Key.Shape,
                kv.Value.Count,
                kv.Value.Take(3).Select(v => v.Where).ToList(),
                kv.Value[0].Body))
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.Event, StringComparer.Ordinal)
            .ToList();

        var byEvent = shapes
            .GroupBy(s => s.Event, StringComparer.Ordinal)
            .Select(g => (Event: g.Key, Handlers: g.Sum(s => s.Count), DistinctShapes: g.Count()))
            .OrderByDescending(x => x.Handlers)
            .ToList();

        return new ScriptShapeReport(total, shapes, byEvent);
    }
}
