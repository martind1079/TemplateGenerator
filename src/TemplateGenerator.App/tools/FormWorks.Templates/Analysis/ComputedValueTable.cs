using System.Text.RegularExpressions;
using FormWorks.Templates.Model;

namespace FormWorks.Templates.Analysis;

/// <summary>What a handler puts into another field.</summary>
public enum ValueSource
{
    /// <summary>Blanked, which is what a form does when a question stops applying.</summary>
    Clear,

    /// <summary>A fixed string or number written in the template.</summary>
    Literal,

    /// <summary>Another field's answer, copied across.</summary>
    Copy,

    /// <summary>Read from the job the agent was sent, which the exchange pass already carries.</summary>
    Inbound,

    /// <summary>Worked out by the handler. Not recoverable.</summary>
    Computed
}

/// <summary>One field filled in by a handler on another field.</summary>
public sealed record ValueWrite(
    TemplateNode Target,
    string Name,
    ValueSource Source,
    string Expression,
    TemplateNode? From,
    TemplateNode Owner,
    string Event,
    IReadOnlyList<Condition> When)
{
    /// <summary>
    /// Whether this can be emitted: the value has to be recoverable and every guard on it
    /// expressible.
    ///
    /// Both halves matter and the second is easy to miss. ContactOK is assigned the plain
    /// string "No", but only inside a handler that counts weekend and evening visits with
    /// date arithmetic, so the value is simple and the rule is not.
    /// </summary>
    public bool Expressible =>
        Source is ValueSource.Clear or ValueSource.Literal or ValueSource.Copy
        && When.All(c => c.FullyParsed)
        && (Source != ValueSource.Copy || From is not null);
}

public sealed record ComputedValueReport(
    string Template,
    IReadOnlyList<ValueWrite> Writes,
    IReadOnlyList<string> Unresolved)
{
    public IEnumerable<ValueWrite> Expressible => Writes.Where(w => w.Expressible);
    public IEnumerable<ValueWrite> LeftToAPerson => Writes.Where(w => !w.Expressible);
}

/// <summary>
/// Recovers the answers a form fills in for the agent.
///
/// A third category beside validation and visibility, and the one that fails most quietly:
/// a field the template would have filled in is simply blank, which looks exactly like a
/// field nobody has reached yet. 2,030 handlers across the estate write into another
/// field, touching 3,119 of them.
///
/// Unlike a rule, this changes an answer, so it is edge-triggered rather than continuous.
/// A clear that ran on every change would erase the agent's typing keystroke by keystroke,
/// where the template clears once when the field it watches changes.
/// </summary>
public static class ComputedValueTable
{
    public static ComputedValueReport Build(TemplateDocument doc)
    {
        var writes = new List<ValueWrite>();
        var unresolved = new List<string>();
        var resolver = new NameResolver(doc);

        foreach (var node in doc.AllNodes)
        {
            foreach (var (evt, script) in node.Scripts)
            {
                if (!LuaScript.ValueAssignment().IsMatch(script)) continue;

                var statements = ScriptWalker.Walk(
                    script, line => LuaScript.ValueAssignment().IsMatch(line));

                foreach (var statement in statements)
                {
                    foreach (Match m in LuaScript.ValueAssignment().Matches(statement.Line))
                    {
                        var name = m.Groups["field"].Value;

                        // A handler setting its own answer is not filling in another field.
                        if (name == "this") continue;

                        var target = resolver.Resolve(name, node);

                        if (target is null)
                        {
                            unresolved.Add($"{node.Label} [{evt}]: writes {name}.value, which names no one element");
                            continue;
                        }

                        var raw = m.Groups["value"].Value.Trim().TrimEnd(';').Trim();
                        var (source, expression, from) = Classify(raw, node, resolver);

                        writes.Add(new ValueWrite(
                            target, name, source, expression, from, node, evt, statement.When));
                    }
                }
            }
        }

        return new ComputedValueReport(doc.FolderName, writes, unresolved);
    }

    private static (ValueSource, string, TemplateNode?) Classify(
        string raw, TemplateNode owner, NameResolver resolver)
    {
        if (Regex.IsMatch(raw, @"^(""""|''|nil)$"))
            return (ValueSource.Clear, "", null);

        if (Regex.IsMatch(raw, @"^""[^""]*""$"))
            return (ValueSource.Literal, raw[1..^1], null);

        if (Regex.IsMatch(raw, @"^-?\d+(\.\d+)?$"))
            return (ValueSource.Literal, raw, null);

        if (Regex.IsMatch(raw, @"^getUserData\("))
            return (ValueSource.Inbound, raw, null);

        var copy = Regex.Match(raw, @"^(?<field>[A-Za-z_]\w*)\.[Vv]alue$");

        if (copy.Success)
        {
            var field = copy.Groups["field"].Value;
            var from = field == "this" ? owner : resolver.Resolve(field, owner);
            return (ValueSource.Copy, field, from);
        }

        return (ValueSource.Computed, raw, null);
    }
}
