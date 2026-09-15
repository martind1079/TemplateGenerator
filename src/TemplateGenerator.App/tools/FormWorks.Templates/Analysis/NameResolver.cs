using FormWorks.Templates.Model;

namespace FormWorks.Templates.Analysis;

/// <summary>
/// Works out which element a script means when it names one.
///
/// Names in these templates are not identities. 483 across the estate are claimed by more
/// than one element, and the duplication is not accidental: a form with the same question
/// on nine outcome pages has nine elements called Tenant1Name, each written by the handler
/// beside it. Treating the name as the identity collapses nine independent rules into one
/// that appears to have nine authors and cannot be reconstructed.
///
/// Resolution is therefore always relative to where the reference was written.
/// </summary>
public sealed class NameResolver
{
    private readonly Dictionary<string, List<TemplateNode>> _claims;

    public NameResolver(TemplateDocument doc)
    {
        _claims = new Dictionary<string, List<TemplateNode>>(StringComparer.Ordinal);

        foreach (var node in doc.AllNodes)
        {
            foreach (var name in node.Names)
            {
                if (!_claims.TryGetValue(name, out var list)) _claims[name] = list = [];
                if (!list.Contains(node)) list.Add(node);
            }
        }
    }

    /// <summary>
    /// The element <paramref name="name"/> refers to in a handler written on
    /// <paramref name="from"/>, or null where nothing decides it.
    ///
    /// Three steps, in order. One claimant settles it. Otherwise the page the reference was
    /// written on does, because a script reaching a field by bare name means the one beside
    /// it. Otherwise a container wins over the question inside it, because a rule that
    /// hides the field whose value decides it can never be satisfied.
    /// </summary>
    public TemplateNode? Resolve(string name, TemplateNode? from)
    {
        if (!_claims.TryGetValue(name, out var claimants)) return null;
        if (claimants.Count == 1) return claimants[0];

        var page = from?.IsPage == true ? from : from?.Page;

        if (page is not null)
        {
            var local = claimants
                .Where(c => (c.IsPage ? c : c.Page)?.Label == page.Label)
                .ToList();

            if (local.Count == 1) return local[0];

            if (local.Count > 1)
            {
                var inner = local.Where(IsContainer).ToList();
                if (inner.Count == 1) return inner[0];
                return null;
            }
        }

        var containers = claimants.Where(IsContainer).ToList();
        return containers.Count == 1 ? containers[0] : null;
    }

    /// <summary>Every element a name could mean, for reporting where nothing decides it.</summary>
    public IReadOnlyList<TemplateNode> Claimants(string name)
        => _claims.GetValueOrDefault(name) ?? [];

    private static bool IsContainer(TemplateNode node) => node.Children.Count > 0 || node.IsPage;
}
