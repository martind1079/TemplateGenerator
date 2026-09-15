using FormWorks.Templates.Model;

namespace FormWorks.Templates.Analysis;

/// <summary>
/// One reference-prefix test, e.g. string.sub(Reference.value, 1, 6) == "ASBHM1".
/// </summary>
/// <param name="Operator">"==" or "~=". The negated form inverts the branch and is easy to lose by eye.</param>
public sealed record PrefixRule(
    string Template,
    string Field,
    int From,
    int To,
    string Operator,
    string Literal,
    string? Page,
    string SourceField,
    string Event)
{
    public int Length => To - From + 1;
    public bool IsNegated => Operator == "~=";
    public bool RoutesSomewhere { get; init; }
}

public sealed record PrefixRuleReport(
    IReadOnlyList<PrefixRule> Rules,
    IReadOnlyList<(string Field, int Count)> SlicedFields)
{
    public IReadOnlyList<(string Literal, int Length, int Count, bool AnyNegated)> Distinct =>
        Rules.GroupBy(r => (r.Literal, r.Length))
             .Select(g => (g.Key.Literal, g.Key.Length, g.Count(), g.Any(r => r.IsNegated)))
             .OrderByDescending(x => x.Item3)
             .ThenBy(x => x.Literal, StringComparer.Ordinal)
             .ToList();
}

/// <summary>
/// Catalogues the prefix rules.
///
/// These are client-specific rules encoded as string comparisons with nothing in the
/// form explaining them, and the session notes are right that they are the single
/// easiest thing to lose. Generating the catalogue rather than writing it by hand is
/// what stops it drifting from the estate.
/// </summary>
public static class PrefixRules
{
    /// <summary>The field whose prefix is conventionally load bearing.</summary>
    public const string ReferenceField = "Reference";

    public static PrefixRuleReport Build(IEnumerable<TemplateDocument> docs, string? onlyField = null)
    {
        var rules = new List<PrefixRule>();
        var sliced = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var doc in docs)
        {
            foreach (var node in doc.AllNodes)
            {
                foreach (var (evt, script) in node.Scripts)
                {
                    foreach (System.Text.RegularExpressions.Match m in LuaScript.PrefixTest().Matches(script))
                    {
                        var field = m.Groups["field"].Value;
                        sliced[field] = sliced.GetValueOrDefault(field) + 1;

                        if (onlyField is not null && !field.Equals(onlyField, StringComparison.Ordinal))
                            continue;

                        rules.Add(new PrefixRule(
                            doc.FolderName,
                            field,
                            int.Parse(m.Groups["from"].Value),
                            int.Parse(m.Groups["to"].Value),
                            m.Groups["op"].Value,
                            m.Groups["literal"].Value,
                            node.IsPage ? node.ElementName : node.Page?.ElementName,
                            node.Label,
                            evt)
                        {
                            RoutesSomewhere = LuaScript.ChangePage().IsMatch(script)
                        });
                    }
                }
            }
        }

        return new PrefixRuleReport(
            rules,
            sliced.Select(kv => (kv.Key, kv.Value))
                  .OrderByDescending(x => x.Value)
                  .ToList());
    }
}
