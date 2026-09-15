using FormWorks.Templates.Model;

namespace FormWorks.Templates.Analysis;

public sealed record CoverageFinding(
    string Template,
    string SourceField,
    string Event,
    string TestedField,
    string Kind,
    string Detail);

public sealed record SelectionCoverageReport(IReadOnlyList<CoverageFinding> Findings)
{
    public const string TestedButNotAnOption = "tested-but-not-an-option";
    public const string OptionNeverTested = "option-never-tested";
    public const string ShadowedBranch = "shadowed-branch";
    public const string UnresolvedField = "unresolved-field";

    public IEnumerable<CoverageFinding> OfKind(string kind)
        => Findings.Where(f => f.Kind == kind);
}

/// <summary>
/// Checks branch conditions against the options a control actually offers.
///
/// Handlers accumulate branches as forms are revised, and nothing in FormWorks
/// rechecks them against the option list. The result is branches on values that can
/// no longer be selected, and duplicate branches where the second can never fire. Both
/// are invisible to a person converting the form by eye, and both change what the
/// converted form does.
/// </summary>
public static class SelectionCoverage
{
    public static SelectionCoverageReport Build(TemplateDocument doc)
    {
        var findings = new List<CoverageFinding>();
        var byElementName = BuildIndex(doc);

        foreach (var node in doc.AllNodes)
        {
            foreach (var (evt, script) in node.Scripts)
            {
                var matches = LuaScript.ValueTest().Matches(script);
                if (matches.Count == 0) continue;

                foreach (var group in matches.GroupBy(m => m.Groups["field"].Value, StringComparer.Ordinal))
                {
                    var reference = group.Key;
                    var literals = group.Select(m => m.Groups["literal"].Value).ToList();

                    // A literal tested twice against the same field inside one if/elseif
                    // chain can only fire on the first branch. Reported as a warning
                    // rather than a certainty, because two separate chains in one handler
                    // would look the same from here.
                    foreach (var dup in literals.GroupBy(l => l, StringComparer.Ordinal).Where(g => g.Count() > 1))
                    {
                        findings.Add(new CoverageFinding(doc.FolderName, node.Label, evt, reference,
                            SelectionCoverageReport.ShadowedBranch,
                            $"'{dup.Key}' is tested {dup.Count()} times; only the first branch can fire."));
                    }

                    if (!byElementName.TryGetValue(reference, out var target))
                    {
                        findings.Add(new CoverageFinding(doc.FolderName, node.Label, evt, reference,
                            SelectionCoverageReport.UnresolvedField,
                            "No element with this name; the option list could not be checked."));
                        continue;
                    }

                    var options = OptionsOf(target);
                    if (options.Count == 0) continue;

                    var declared = options.Select(o => o.Value).ToHashSet(StringComparer.Ordinal);
                    foreach (var literal in literals.Distinct(StringComparer.Ordinal).Where(l => !declared.Contains(l)))
                    {
                        findings.Add(new CoverageFinding(doc.FolderName, node.Label, evt, reference,
                            SelectionCoverageReport.TestedButNotAnOption,
                            $"'{literal}' is not one of the {declared.Count} options; the branch is dead."));
                    }

                    // Only meaningful for a handler that routes: an option no branch
                    // mentions leaves the agent with nowhere to go.
                    if (LuaScript.ChangePage().IsMatch(script))
                    {
                        var tested = literals.ToHashSet(StringComparer.Ordinal);
                        foreach (var option in declared.Where(d => !tested.Contains(d)).OrderBy(d => d, StringComparer.Ordinal))
                        {
                            findings.Add(new CoverageFinding(doc.FolderName, node.Label, evt, reference,
                                SelectionCoverageReport.OptionNeverTested,
                                $"'{option}' is selectable but no branch handles it."));
                        }
                    }
                }
            }
        }

        return new SelectionCoverageReport(findings);
    }

    /// <summary>
    /// Scripts refer to elements by short name, and the short name usually lands on the
    /// Section wrapping a control rather than the control itself. Later entries do not
    /// overwrite earlier ones, so the outermost match wins, which is what a script sees.
    /// </summary>
    private static Dictionary<string, TemplateNode> BuildIndex(TemplateDocument doc)
    {
        var index = new Dictionary<string, TemplateNode>(StringComparer.Ordinal);
        foreach (var node in doc.AllNodes)
        {
            foreach (var key in new[] { node.ElementName, node.Alias })
                if (!string.IsNullOrEmpty(key))
                    index.TryAdd(key, node);
        }
        return index;
    }

    /// <summary>
    /// The options of a node, or of the single descendant that has them. A FormWorks
    /// "field" is often a Section holding a label and the control, so the options sit
    /// one or two levels below the name a script uses.
    /// </summary>
    private static IReadOnlyList<TemplateOption> OptionsOf(TemplateNode node)
    {
        if (node.Options.Count > 0) return node.Options;

        var withOptions = node.Descendants().Where(n => n.Options.Count > 0).ToList();
        return withOptions.Count == 1 ? withOptions[0].Options : [];
    }
}
