namespace FormWorks.Templates.Analysis;

/// <summary>One comparison, e.g. VisitOutcome.value == "Refused".</summary>
/// <param name="Property">Which part of the field is read: its value, or its visible state.</param>
/// <param name="IsLiteralBoolean">True when the comparison is against true or false rather than text.</param>
/// <param name="IsNumeric">
/// True where the comparison is arithmetic rather than textual. An empty
/// <paramref name="Operator"/> with this set is the bare tonumber test: does the answer
/// read as a number at all.
/// </param>
/// <param name="Other">The field on the right of the comparison, where it is a field.</param>
public sealed record Guard(
    string Field, string Operator, string Literal, int? From, int? To,
    string Property = "value", bool IsLiteralBoolean = false,
    bool IsNumeric = false, string? Other = null)
{
    public bool IsPrefixTest => From is not null;

    /// <summary>Whether the answer reads as a number, with nothing compared to it.</summary>
    public bool IsNumberTest => IsNumeric && Operator.Length == 0;

    /// <summary>Reading another field's visible or enabled state, rather than its answer.</summary>
    public bool IsStateTest => Property is "visible" or "enabled";

    /// <summary>Lua's not-equals. Inverting one of these by eye is silent.</summary>
    public bool IsNegated => Operator == "~=";

    public override string ToString() => (IsPrefixTest, IsStateTest || IsLiteralBoolean) switch
    {
        _ when IsNumberTest => $"{Field} is a number",
        _ when IsNumeric => $"{Field} {Operator} {Other ?? Literal} (as numbers)",
        (true, _) => $"{Field}[{From}..{To}] {Operator} \"{Literal}\"",
        (_, true) => $"{Field}.{Property} {Operator} {Literal}",
        _ => $"{Field} {Operator} \"{Literal}\""
    };
}

/// <summary>
/// A branch condition, split into a disjunction of conjunctions.
///
/// Getting the connective right is the whole point. The outcome branches are chains of
/// "or", and a table that renders them as "and" routes nobody anywhere while looking
/// perfectly reasonable. <see cref="Raw"/> is kept as the authority so a reader can
/// always check the split against the source.
/// </summary>
public sealed record Condition(string Raw, IReadOnlyList<IReadOnlyList<Guard>> AnyOf, bool Negated = false)
{
    /// <summary>True when every part of the text was accounted for as a comparison.</summary>
    public bool FullyParsed => AnyOf.Count > 0 && AnyOf.All(g => g.Count > 0);

    public IEnumerable<Guard> Atoms => AnyOf.SelectMany(g => g);

    public Condition Negate() => this with { Negated = !Negated };

    public override string ToString()
    {
        if (!FullyParsed) return (Negated ? "NOT " : "") + Raw.Trim();

        var terms = AnyOf.Select(and => and.Count == 1
            ? and[0].ToString()
            : "(" + string.Join(" AND ", and) + ")");

        var body = string.Join(" OR ", terms);
        if (AnyOf.Count > 1) body = "(" + body + ")";
        return Negated ? "NOT " + body : body;
    }
}

/// <summary>Splits Lua boolean expressions into <see cref="Condition"/> form.</summary>
public static class ConditionParser
{
    public static Condition Parse(string raw)
    {
        var text = StripOuterParens(raw.Trim());

        var anyOf = SplitTopLevel(text, "or")
            .Select(term => (IReadOnlyList<Guard>)SplitTopLevel(StripOuterParens(term.Trim()), "and")
                .SelectMany(AtomsOf)
                .ToList())
            .ToList();

        return new Condition(raw, anyOf);
    }

    private static IEnumerable<Guard> AtomsOf(string text)
    {
        var atoms = new List<Guard>();

        foreach (System.Text.RegularExpressions.Match m in LuaScript.PrefixTest().Matches(text))
        {
            atoms.Add(new Guard(
                m.Groups["field"].Value, m.Groups["op"].Value, m.Groups["literal"].Value,
                int.Parse(m.Groups["from"].Value), int.Parse(m.Groups["to"].Value)));
        }

        foreach (System.Text.RegularExpressions.Match m in LuaScript.ValueTest().Matches(text))
        {
            // A prefix test carries a .value reference of its own; do not count it twice.
            if (text.Contains($"string.sub({m.Groups["field"].Value}", StringComparison.Ordinal)) continue;
            atoms.Add(new Guard(m.Groups["field"].Value, m.Groups["op"].Value, m.Groups["literal"].Value, null, null));
        }

        foreach (System.Text.RegularExpressions.Match m in LuaScript.BooleanTest().Matches(text))
        {
            atoms.Add(new Guard(
                m.Groups["field"].Value, m.Groups["op"].Value, m.Groups["literal"].Value, null, null,
                IsLiteralBoolean: true));
        }

        foreach (System.Text.RegularExpressions.Match m in LuaScript.StateTest().Matches(text))
        {
            atoms.Add(new Guard(
                m.Groups["field"].Value, m.Groups["op"].Value, m.Groups["literal"].Value, null, null,
                Property: m.Groups["property"].Value, IsLiteralBoolean: true));
        }

        foreach (System.Text.RegularExpressions.Match m in LuaScript.NumberTest().Matches(text))
        {
            // Both sides converted, "tonumber(a) < tonumber(b)", is one comparison. Read as
            // two bare conversions it becomes "a is a number and b is a number", which is
            // true whenever the comparison is even askable and drops the question entirely.
            atoms.Add(new Guard(
                m.Groups["field"].Value, m.Groups["op"].Value, m.Groups["literal"].Value, null, null,
                IsNumeric: true,
                Other: m.Groups["other"].Success ? m.Groups["other"].Value : null));
        }

        foreach (System.Text.RegularExpressions.Match m in LuaScript.RelationalTest().Matches(text))
        {
            // tonumber already claimed this comparison; counting it twice would AND a test
            // with itself.
            if (text.Contains($"tonumber({m.Groups["field"].Value}", StringComparison.Ordinal)) continue;

            atoms.Add(new Guard(
                m.Groups["field"].Value, m.Groups["op"].Value, m.Groups["literal"].Value, null, null,
                IsNumeric: true,
                Other: m.Groups["other"].Success ? m.Groups["other"].Value : null));
        }

        return atoms;
    }

    /// <summary>Splits on a keyword only where parentheses are balanced.</summary>
    private static List<string> SplitTopLevel(string text, string keyword)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        var inString = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && IsKeywordAt(text, i, keyword))
            {
                parts.Add(text[start..i]);
                i += keyword.Length - 1;
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    private static bool IsKeywordAt(string text, int i, string keyword)
    {
        if (i + keyword.Length > text.Length) return false;
        if (string.CompareOrdinal(text, i, keyword, 0, keyword.Length) != 0) return false;
        if (i > 0 && char.IsLetterOrDigit(text[i - 1])) return false;
        var after = i + keyword.Length;
        return after >= text.Length || !char.IsLetterOrDigit(text[after]);
    }

    private static string StripOuterParens(string text)
    {
        while (text.Length > 1 && text[0] == '(' && text[^1] == ')')
        {
            var depth = 0;
            var wraps = true;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')') depth--;
                if (depth == 0 && i < text.Length - 1) { wraps = false; break; }
            }
            if (!wraps) break;
            text = text[1..^1].Trim();
        }
        return text;
    }
}
