using FormWorks.Templates.Model;

namespace FormWorks.Templates.Analysis;

/// <summary>
/// One reason a field can be invalid: the conditions that make it so, and what the agent
/// is told.
/// </summary>
/// <param name="Message">
/// Null where the template computes the message rather than stating it, which is usually
/// <c>this.title</c>.
/// </param>
public sealed record ValidationRule(
    string Field,
    string? Page,
    string Event,
    IReadOnlyList<Condition> When,
    string? Message,
    string MessageExpression)
{
    public bool FullyParsed => When.Count > 0 && When.All(c => c.FullyParsed);
}

public sealed record ValidationTableReport(
    string Template,
    IReadOnlyList<ValidationRule> Rules,
    int HandlerCount,
    IReadOnlyList<string> Unrecovered)
{
    public IEnumerable<ValidationRule> Expressible => Rules.Where(r => r.FullyParsed);
}

/// <summary>
/// Recovers what makes a field invalid.
///
/// FormWorks writes validation as a handler per field that sets <c>this.valid</c> and
/// <c>this.message</c> under some condition. The condition is what matters and it is not
/// stated anywhere else, so it is recovered the same way routing was: by walking the
/// branch structure rather than pattern matching for the assignment.
///
/// The rules read other fields freely, and very often read whether a field is currently
/// shown, because a field is only required while it is visible.
/// </summary>
public static class ValidationTable
{
    public static ValidationTableReport Build(TemplateDocument doc)
    {
        var rules = new List<ValidationRule>();
        var unrecovered = new List<string>();
        var handlers = 0;

        foreach (var node in doc.AllNodes)
        {
            foreach (var (evt, script) in node.Scripts)
            {
                // A field's validity is only ever set by its own handler in this estate.
                // If that stops being true the rule belongs to a field this walker is not
                // looking at, so say so rather than dropping it.
                foreach (System.Text.RegularExpressions.Match foreign
                         in LuaScript.ForeignValidityAssignment().Matches(script))
                {
                    unrecovered.Add(
                        $"{node.Label} [{evt}]: sets {foreign.Groups["field"].Value}"
                        + $".{foreign.Groups["property"].Value} on another field");
                }

                if (!LuaScript.ValidityAssignment().IsMatch(script)) continue;
                handlers++;

                var statements = ScriptWalker.Walk(
                    script, line => LuaScript.ValidityAssignment().IsMatch(line));

                if (statements.Count == 0)
                {
                    unrecovered.Add($"{node.Label} [{evt}]: no branch structure found");
                    continue;
                }

                foreach (var statement in statements)
                {
                    // Only a rule that can make a field invalid. Setting it valid is the
                    // default state, not a rule.
                    var assignment = LuaScript.ValidityAssignment().Match(statement.Line);
                    if (assignment.Groups["value"].Value != "false") continue;

                    var (message, expression) = MessageFor(script, statement.Line);

                    rules.Add(new ValidationRule(
                        node.Label,
                        node.IsPage ? node.ElementName : node.Page?.ElementName,
                        evt,
                        statement.Own,
                        message,
                        expression));
                }
            }
        }

        return new ValidationTableReport(doc.FolderName, rules, handlers, unrecovered);
    }

    /// <summary>
    /// The message that goes with a validity assignment.
    ///
    /// Usually the next statement, so the search starts there and falls back to the only
    /// message in the handler. A template that computes the message keeps the expression
    /// rather than a literal, so a person can see what it was.
    /// </summary>
    private static (string? Literal, string Expression) MessageFor(string script, string line)
    {
        var lines = script.ReplaceLineEndings("\n").Split('\n');
        var index = Array.FindIndex(lines, l => l.Contains(line, StringComparison.Ordinal));

        for (var i = Math.Max(index, 0); i < lines.Length && i <= index + 3; i++)
        {
            var match = LuaScript.MessageAssignment().Match(lines[i]);
            if (!match.Success) continue;

            return match.Groups["literal"].Success
                ? (match.Groups["literal"].Value, match.Groups["literal"].Value)
                : (null, match.Groups["expression"].Value.Trim());
        }

        var anywhere = LuaScript.MessageAssignment().Match(script);
        if (!anywhere.Success) return (null, "");

        return anywhere.Groups["literal"].Success
            ? (anywhere.Groups["literal"].Value, anywhere.Groups["literal"].Value)
            : (null, anywhere.Groups["expression"].Value.Trim());
    }
}
