using System.Text.RegularExpressions;

namespace FormWorks.Templates.Analysis;

/// <summary>
/// The handful of Lua shapes the converter needs to recognise.
///
/// This is pattern matching over source text, not a parser, and it is deliberately
/// that. The handlers are small and highly repetitive, so recognising the shapes that
/// actually occur is cheap and honest about its limits. Anything these patterns do
/// not match stays visible as an unmatched script rather than being quietly dropped.
/// </summary>
public static partial class LuaScript
{
    /// <summary>form.changePage("SomePage")</summary>
    [GeneratedRegex(@"form\.changePage\(\s*""(?<page>[^""]*)""\s*\)", RegexOptions.CultureInvariant)]
    public static partial Regex ChangePage();

    /// <summary>string.sub(Field.value, 1, 6) == "ASBHM1", and the not-equals form.</summary>
    [GeneratedRegex(
        @"string\.sub\(\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)(?:\.value)?\s*,\s*(?<from>\d+)\s*,\s*(?<to>\d+)\s*\)\s*(?<op>==|~=)\s*""(?<literal>[^""]*)""",
        RegexOptions.CultureInvariant)]
    public static partial Regex PrefixTest();

    /// <summary>Field.value == "Some Option", and the not-equals form.</summary>
    [GeneratedRegex(
        @"(?<field>[A-Za-z_][A-Za-z0-9_]*)\.value\s*(?<op>==|~=)\s*""(?<literal>[^""]*)""",
        RegexOptions.CultureInvariant)]
    public static partial Regex ValueTest();

    /// <summary>Field.value == true, which is how a checkbox is tested.</summary>
    [GeneratedRegex(
        @"(?<field>[A-Za-z_][A-Za-z0-9_]*)\.value\s*(?<op>==|~=)\s*(?<literal>true|false)\b",
        RegexOptions.CultureInvariant)]
    public static partial Regex BooleanTest();

    /// <summary>
    /// Field.visible == true. A read, not a write.
    ///
    /// This is what makes a validation rule conditional: a field is only required while it
    /// is shown. Mistaking it for an assignment makes validation look entangled with
    /// visibility when the two barely overlap.
    /// </summary>
    [GeneratedRegex(
        @"(?<field>[A-Za-z_][A-Za-z0-9_]*)\.(?<property>visible|enabled)\s*(?<op>==|~=)\s*(?<literal>true|false)\b",
        RegexOptions.CultureInvariant)]
    public static partial Regex StateTest();

    /// <summary>
    /// tonumber(Field.value), alone or compared against a number.
    ///
    /// Lua's tonumber returns nil for text that is not a number, and nil is false, so
    /// "if tonumber(x)" is a test that the answer reads as a number at all. The estate
    /// uses that as the outer guard and the comparison inside it, which is why the two
    /// shapes are one pattern.
    /// </summary>
    [GeneratedRegex(
        @"tonumber\(\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)(?:\.value)?\s*\)"
        + @"(?:\s*(?<op>[<>]=?|==|~=)\s*(?:"
        + @"tonumber\(\s*(?<other>[A-Za-z_][A-Za-z0-9_]*)(?:\.value)?\s*\)"
        + @"|""?(?<literal>-?\d+(?:\.\d+)?)""?))?",
        RegexOptions.CultureInvariant)]
    public static partial Regex NumberTest();

    /// <summary>
    /// Field.value &lt; something, where the something is a number or another field.
    ///
    /// Written without tonumber, so Lua compares as text: "10" &lt; "9" is true. The
    /// templates plainly mean a number, and the quoted form gives it away, so this is
    /// recovered as a numeric comparison and the difference is reported rather than
    /// reproduced.
    /// </summary>
    [GeneratedRegex(
        @"(?<field>[A-Za-z_][A-Za-z0-9_]*)\.value\s*(?<op><=?|>=?)\s*"
        + @"(?:(?<other>[A-Za-z_][A-Za-z0-9_]*)\.value|""?(?<literal>-?\d+(?:\.\d+)?)""?)",
        RegexOptions.CultureInvariant)]
    public static partial Regex RelationalTest();

    /// <summary>
    /// A write to some other field's validity, which this estate never does: all 4,426
    /// writes are to the handler's own field. Detected so that a template which starts
    /// doing it is reported rather than quietly half-converted.
    /// </summary>
    [GeneratedRegex(
        @"\b(?<field>(?!this\b)[A-Za-z_][A-Za-z0-9_.]*)\.(?<property>valid|message)\s*=(?!=)",
        RegexOptions.CultureInvariant)]
    public static partial Regex ForeignValidityAssignment();

    /// <summary>this.valid = false, and the message that goes with it.</summary>
    [GeneratedRegex(@"this\.valid\s*=\s*(?<value>true|false)", RegexOptions.CultureInvariant)]
    public static partial Regex ValidityAssignment();

    [GeneratedRegex(@"this\.message\s*=\s*(?:""(?<literal>[^""]*)""|(?<expression>[^;]+))", RegexOptions.CultureInvariant)]
    public static partial Regex MessageAssignment();

    /// <summary>
    /// Field.value = something: a form filling in an answer for the agent.
    ///
    /// Deliberately not matching == , and not matching a read. The capital V form appears
    /// in the estate too, because FormWorks does not care about the case of a property.
    /// </summary>
    [GeneratedRegex(
        @"(?<![.\w])(?<field>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*[Vv]alue\s*=(?!=)\s*(?<value>[^;\n]+)",
        RegexOptions.CultureInvariant)]
    public static partial Regex ValueAssignment();

    /// <summary>Field.visible = true, and assignments to enabled.</summary>
    [GeneratedRegex(
        @"(?<field>[A-Za-z_][A-Za-z0-9_.]*)\.(?<property>visible|enabled)\s*=\s*(?<value>true|false)",
        RegexOptions.CultureInvariant)]
    public static partial Regex StateAssignment();

    [GeneratedRegex(@"""[^""]*""", RegexOptions.CultureInvariant)]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"\b\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex NumberLiteral();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_.]*(?=\.value\b)", RegexOptions.CultureInvariant)]
    private static partial Regex FieldReference();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    /// <summary>
    /// Strips the parts that vary between copies of the same handler so that identical
    /// logic collapses to one shape. Nine of the routing handlers in one example template
    /// differ only in indentation.
    /// </summary>
    public static string Normalise(string script)
    {
        var s = StringLiteral().Replace(script, "STR");
        s = FieldReference().Replace(s, "FIELD");
        s = NumberLiteral().Replace(s, "NUM");
        s = Whitespace().Replace(s, " ");
        return s.Trim();
    }

    /// <summary>Whether a handler both routes and changes visibility or enablement.</summary>
    public static bool MixesRoutingWithState(string script)
        => ChangePage().IsMatch(script) && StateAssignment().IsMatch(script);
}
