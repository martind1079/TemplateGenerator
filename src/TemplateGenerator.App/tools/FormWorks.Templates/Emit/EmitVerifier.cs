using System.Text.RegularExpressions;

namespace FormWorks.Templates.Emit;

public sealed record EmitProblem(string Kind, string Detail);

/// <summary>
/// Checks the emitted files against each other and against the app before they are
/// written.
///
/// Both failures found on the last day of the POC phase were invisible to the compiler
/// and only showed at runtime: a wrong resource key crashes the page when it is
/// displayed, and a get-only section of complex type saves correctly and resumes empty.
/// At the scale of the estate, silent runtime failure is the thing that hurts, so the
/// generator checks its own output rather than trusting it.
/// </summary>
public static partial class EmitVerifier
{
    public const string UnresolvedBinding = "unresolved-binding";
    public const string MissingResourceKey = "missing-resource-key";
    public const string ReadOnlyAnswer = "read-only-answer";
    public const string DuplicateProperty = "duplicate-property";

    [GeneratedRegex(@"\{Binding\s+(?<path>[A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.CultureInvariant)]
    private static partial Regex BindingPath();

    [GeneratedRegex(@"\{StaticResource\s+(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceKey();

    [GeneratedRegex(@"public\s+[^\s]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get;\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex GetOnlyProperty();

    public static IReadOnlyList<EmitProblem> Verify(
        PageEmitModel page, string xaml, string answers, IReadOnlySet<string>? availableResourceKeys)
    {
        var problems = new List<EmitProblem>();

        var properties = page.AllFields.Select(f => f.PropertyName).ToHashSet(StringComparer.Ordinal);
        foreach (var field in page.AllFields.Where(f => f.Options.Count > 0))
            properties.Add(field.PropertyName + "Options");

        // A validated field also carries why it is not valid.
        foreach (var validated in page.Validated)
            properties.Add(validated + "Error");

        foreach (var duplicate in page.AllFields
                     .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            problems.Add(new EmitProblem(DuplicateProperty,
                $"'{duplicate.Key}' is emitted {duplicate.Count()} times; answers would overwrite each other."));
        }

        // Every binding the page uses must exist on the model it declares as its data type.
        foreach (Match m in BindingPath().Matches(xaml))
        {
            var path = m.Groups["path"].Value;
            if (!path.StartsWith("Answers.", StringComparison.Ordinal)) continue;

            var property = path["Answers.".Length..];
            if (!properties.Contains(property))
                problems.Add(new EmitProblem(UnresolvedBinding,
                    $"'{path}' does not resolve against {page.AnswersClassName}."));
        }

        // The house palette spells it Grey. A Gray ramp sits inside a commented-out block
        // of MAUI defaults in Colors.xaml and shadows the real names, so a plausible key
        // can be entirely absent.
        if (availableResourceKeys is not null)
        {
            foreach (Match m in ResourceKey().Matches(xaml))
            {
                var key = m.Groups["key"].Value;
                if (!availableResourceKeys.Contains(key))
                    problems.Add(new EmitProblem(MissingResourceKey,
                        $"'{key}' is not in the app's resource dictionaries; the page crashes when shown."));
            }
        }

        // A get-only property of complex type is skipped on deserialise, silently.
        foreach (Match m in GetOnlyProperty().Matches(answers))
        {
            var name = m.Groups["name"].Value;
            if (!name.EndsWith("Options", StringComparison.Ordinal))
                problems.Add(new EmitProblem(ReadOnlyAnswer,
                    $"'{name}' has no setter; it will save correctly and resume empty."));
        }

        return problems;
    }

    /// <summary>Collects x:Key values from the app's resource dictionaries.</summary>
    public static IReadOnlySet<string> ReadResourceKeys(string appProjectDirectory)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var styles = System.IO.Path.Combine(appProjectDirectory, "Resources", "Styles");
        if (!Directory.Exists(styles)) return keys;

        foreach (var file in Directory.EnumerateFiles(styles, "*.xaml"))
        {
            var text = File.ReadAllText(file);

            // Commented-out blocks are not live keys, and Colors.xaml has a large one.
            text = Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);

            foreach (Match m in Regex.Matches(text, @"x:Key=""(?<key>[^""]+)"""))
                keys.Add(m.Groups["key"].Value);
        }
        return keys;
    }
}
