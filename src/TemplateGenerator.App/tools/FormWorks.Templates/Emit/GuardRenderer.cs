using FormWorks.Templates.Analysis;

namespace FormWorks.Templates.Emit;

/// <summary>
/// Turns a recovered condition into a C# expression.
///
/// Shared by validation and visibility deliberately. Both read the same small vocabulary
/// of comparisons out of the same handlers, and the vocabulary carries Lua semantics that
/// are not obvious: a slice is one-based and inclusive at both ends, an empty comparison
/// is asking whether the field was answered at all, and a state test reads another field's
/// visibility rather than its value. Two copies of that would agree until one was fixed.
/// </summary>
public static class GuardRenderer
{
    /// <param name="paths">Field name, as scripts spell it, to its path from the report.</param>
    /// <param name="self">The label of the field whose handler this is, for <c>this</c>.</param>
    /// <param name="stateCall">The helper that answers whether a field is shown.</param>
    /// <param name="resolveState">
    /// Turns the name a state test reads into the key the state tables answer to. Names are
    /// not identities in these templates, so the same spelling means different elements in
    /// different handlers.
    /// </param>
    public static string Render(
        IReadOnlyList<Condition> when,
        IReadOnlyDictionary<string, string> paths,
        string? self,
        string stateCall,
        Func<string, string>? resolveState = null,
        IReadOnlyDictionary<string, string>? types = null)
    {
        var parts = new List<string>();

        foreach (var condition in when)
        {
            var alternatives = condition.AnyOf
                .Select(and => string.Join(" && ", and.Select(a => Atom(a, paths, self, stateCall, resolveState, types))))
                .Select(t => t.Contains("&&", StringComparison.Ordinal) ? $"({t})" : t)
                .ToList();

            var text = string.Join(" || ", alternatives);
            var rendered = alternatives.Count > 1 ? $"({text})" : text;

            // An else branch is its sibling's condition, negated. Dropping that flag reads
            // an else as though it were the if, which shows a field exactly when the
            // template hides it.
            parts.Add(condition.Negated ? $"!({rendered})" : rendered);
        }

        return parts.Count == 0 ? "true" : string.Join(" && ", parts);
    }

    public static string Atom(
        Guard guard,
        IReadOnlyDictionary<string, string> paths,
        string? self,
        string stateCall,
        Func<string, string>? resolveState = null,
        IReadOnlyDictionary<string, string>? types = null)
    {
        // A guard reading whether a field is shown, rather than what it holds.
        if (guard.IsStateTest)
        {
            var target = guard.Field == "this" ? self ?? guard.Field : guard.Field;
            if (resolveState is not null) target = resolveState(target);

            var test = $"{stateCall}(report, \"{Escape(target)}\")";
            return guard.Literal == "true" == !guard.IsNegated ? test : $"!{test}";
        }

        if (guard.IsNumeric)
        {
            var left = Path(guard.Field, paths, self);
            if (left is null) return "false /* unresolved */";

            // Lua's tonumber gives nil for text that is not a number, and every comparison
            // against nil is false. A nullable double behaves the same way: null < 18 and
            // null > 18 are both false, so the "is it a number" guard comes for free.
            if (guard.IsNumberTest) return $"Num(report.{left}) is not null";

            var right = guard.Other is null
                ? guard.Literal
                : Path(guard.Other, paths, self) is { } p ? $"Num(report.{p})" : null;

            if (right is null) return "false /* unresolved */";

            // Equality is the exception: two nulls are equal, which would make a rule fire
            // on two unanswered fields, so it says outright that the left is a number.
            return guard.Operator is "==" or "~="
                ? $"(Num(report.{left}) is not null && Num(report.{left}) {(guard.IsNegated ? "!=" : "==")} {right})"
                : $"Num(report.{left}) {guard.Operator} {right}";
        }

        var path = guard.Field == "this"
            ? (self is null ? null : paths.GetValueOrDefault(self))
            : paths.GetValueOrDefault(guard.Field);

        if (path is null) return "false /* unresolved */";

        var access = $"report.{path}";

        if (guard.IsPrefixTest)
        {
            var slice = $"Slice({access}, {guard.From}, {guard.To}, \"{Escape(guard.Literal)}\")";
            return guard.IsNegated ? $"!{slice}" : slice;
        }

        if (guard.IsLiteralBoolean)
            return guard.Literal == "true" == !guard.IsNegated ? access : $"!{access}";

        // An empty comparison is asking whether the field was answered at all. FormWorks
        // holds every answer as text, so the template compares a date to "". Here a date is
        // a date, and asking whether it is an empty string does not compile.
        if (guard.Literal.Length == 0)
        {
            var type = types?.GetValueOrDefault(path);
            var isText = type is null or "string" or "string?";

            // A checkbox cannot be unanswered: it is ticked or it is not, and the generated
            // property is a bool rather than a nullable one, so a null test does not compile.
            // Lua agrees — comparing a boolean to a string is false there too — so the guard
            // is a constant rather than a test.
            if (!isText && !type!.EndsWith('?'))
                return guard.IsNegated ? "true" : "false";

            var unanswered = isText
                ? $"string.IsNullOrEmpty({access})"
                : $"{access} is null";

            return guard.IsNegated ? $"!({unanswered})" : unanswered;
        }

        var equals = $"string.Equals({access}, \"{Escape(guard.Literal)}\", StringComparison.Ordinal)";
        return guard.IsNegated ? $"!{equals}" : equals;
    }

    private static string? Path(
        string field, IReadOnlyDictionary<string, string> paths, string? self)
        => field == "this"
            ? self is null ? null : paths.GetValueOrDefault(self)
            : paths.GetValueOrDefault(field);

    /// <summary>The helpers every rendered guard can need, so both emitters write them.</summary>
    public static void EmitSliceHelper(System.Text.StringBuilder sb)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Lua's string.sub is one-based and inclusive at both ends, so a slice of 1 to 6");
        sb.AppendLine("    /// is six characters starting at the first.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private static bool Slice(string? value, int from, int to, string literal)");
        sb.AppendLine("        => value is not null");
        sb.AppendLine("           && value.Length >= to");
        sb.AppendLine("           && string.Equals(value[(from - 1)..to], literal, StringComparison.Ordinal);");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Lua's tonumber: the answer read as a number, or null where it is not one.");
        sb.AppendLine("    ///");
        sb.AppendLine("    /// Nullable on purpose. Every comparison against null is false, which is how Lua");
        sb.AppendLine("    /// treats a comparison against nil, so an unanswered or non-numeric field fails");
        sb.AppendLine("    /// the test in both directions rather than counting as zero.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private static double? Num(string? value)");
        sb.AppendLine("        => double.TryParse(");
        sb.AppendLine("               value, System.Globalization.NumberStyles.Any,");
        sb.AppendLine("               System.Globalization.CultureInfo.InvariantCulture, out var number)");
        sb.AppendLine("           ? number");
        sb.AppendLine("           : null;");
    }

    /// <summary>
    /// Every name a field can be referred to by, mapped to its path from the report.
    ///
    /// Scripts reach fields by element name or alias, and a node's own label is usually the
    /// fully qualified name, so all three have to resolve to the same property.
    /// </summary>
    /// <summary>
    /// The C# type behind each path, so a guard can ask the right question of it. FormWorks
    /// holds every answer as text and the templates compare dates and times to "", which is
    /// a string comparison here only if the answer really is a string.
    /// </summary>
    public static Dictionary<string, string> TypeIndex(IReadOnlyList<PageEmitModel> pages)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var page in pages)
            foreach (var field in page.AllFields)
                if (field.Mapping is { } mapping && field.PropertyName is { } property)
                    index.TryAdd($"{page.PageName}.{property}", mapping.ClrType);

        return index;
    }

    public static Dictionary<string, string> PathIndex(IReadOnlyList<PageEmitModel> pages)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            foreach (var field in page.AllFields)
            {
                var path = $"{page.PageName}.{field.PropertyName}";

                index.TryAdd(field.Source.Label, path);

                for (var node = field.Source; node is not null && !node.IsPage; node = node.Parent)
                    foreach (var key in new[] { node.ElementName, node.Alias })
                        if (!string.IsNullOrEmpty(key))
                            index.TryAdd(key, path);
            }
        }

        return index;
    }

    internal static string Escape(string value) => value.Replace("\"", "\\\"");
}
