using FormWorks.Templates.Model;

namespace FormWorks.Templates.Analysis;

/// <summary>One place a field's shown or enabled state is written, and what has to hold.</summary>
/// <param name="When">
/// Every condition guarding the write, negated siblings included, so it is correct
/// independently of the order the writes are emitted in.
///
/// Visibility needs this where validation could use the branch-local conditions. A
/// validation handler is an if/elseif chain where one branch runs, so emitting in source
/// order gets the negations for free. Visibility writes run in sequence and the last one
/// wins, so reconstructing it means checking the writes in reverse, and reversing is only
/// sound when each condition stands on its own.
/// </param>
/// <param name="Own">The branch-local conditions, which read better in a report.</param>
public sealed record StateWrite(
    TemplateNode Target,
    string Property,
    bool Value,
    string SourceField,
    TemplateNode Source,
    string SourceEvent,
    IReadOnlyList<Condition> When,
    IReadOnlyList<Condition> Own,
    string Name)
{
    public bool FullyParsed => When.Count > 0 && When.All(c => c.FullyParsed);

    /// <summary>An unguarded write always runs, so it states a default rather than a rule.</summary>
    public bool IsUnconditional => When.Count == 0;
}

/// <summary>How a single field's state can be reconstructed, if it can.</summary>
public enum StateShape
{
    /// <summary>Nothing writes it. The template's own hidden flag is the whole answer.</summary>
    Static,

    /// <summary>Written from one handler, so the writes are ordered and complete.</summary>
    SingleSource,

    /// <summary>Written from several handlers, so the answer depends on which ran last.</summary>
    MultiSource,

    /// <summary>At least one guard is not expressible, so the field is left to a person.</summary>
    Refused
}

/// <summary>
/// How the imperative writes turn into a predicate, if they do.
/// </summary>
public enum Inversion
{
    /// <summary>
    /// The estate's dominant idiom: hide unconditionally at the top of the handler, then
    /// show under a guard. Inverts exactly, to the disjunction of the guards.
    /// </summary>
    ResetThenShow,

    /// <summary>
    /// Guarded both ways and never reset, so the guards have to cover every case between
    /// them or the field keeps whatever state it last had. Sound only if they are
    /// complementary, which is checked per field rather than assumed.
    /// </summary>
    GuardedBothWays,

    /// <summary>
    /// Only ever shown, never hidden. Once true it stays true, so a predicate that goes
    /// back to false where the guard stops holding is not the same form. Needs a person.
    /// </summary>
    ShowOnly,

    /// <summary>Only ever hidden under a guard, the mirror of ShowOnly.</summary>
    HideOnly,

    /// <summary>A guard somewhere is not expressible, so nothing can be said.</summary>
    None
}

/// <param name="Target">The element the rule is about, resolved from the name.</param>
/// <param name="Name">The name the scripts write it under, which several elements may share.</param>
public sealed record FieldState(
    TemplateNode Target,
    string Name,
    string Property,
    StateShape Shape,
    bool StartsHidden,
    IReadOnlyList<StateWrite> Writes)
{
    public IEnumerable<string> Sources =>
        Writes.Select(w => $"{w.SourceField}[{w.SourceEvent}]").Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Whether the writes state a predicate, and which shape of one.
    ///
    /// This is the question the emitter turns on, and it is not the same as whether the
    /// conditions parse. A field whose guards all parse can still be unreconstructable,
    /// because FormWorks state persists and a predicate does not.
    /// </summary>
    public Inversion Inversion
    {
        get
        {
            if (Shape == StateShape.Refused) return Inversion.None;

            var guarded = Writes.Where(w => !w.IsUnconditional).ToList();
            var resets = Writes.Any(w => w.IsUnconditional && !w.Value);

            var shows = guarded.Any(w => w.Value);
            var hides = guarded.Any(w => !w.Value);

            if (resets && shows && !hides) return Inversion.ResetThenShow;
            if (shows && hides) return Inversion.GuardedBothWays;
            if (shows) return Inversion.ShowOnly;
            if (hides) return Inversion.HideOnly;

            return Inversion.None;
        }
    }
}

/// <param name="Unresolved">
/// References whose element could not be decided, with the names they were written under.
/// </param>
public sealed record VisibilityTableReport(
    string Template,
    IReadOnlyList<FieldState> Fields,
    IReadOnlyList<string> Unrecovered,
    IReadOnlyList<(string Name, string Property, TemplateNode From)> Unresolved)
{
    public IEnumerable<FieldState> Visibility => Fields.Where(f => f.Property == "visible");
    public IEnumerable<FieldState> Enablement => Fields.Where(f => f.Property == "enabled");
}

/// <summary>
/// Recovers when a field is shown and when it is usable.
///
/// This is a harder problem than validation and the difference is worth stating. A
/// validation handler only ever writes <c>this.valid</c>, so each rule belongs to the
/// field it is written on and the handler is the whole story. Visibility is the opposite:
/// a handler on one field writes the state of several others, so a field's visibility is
/// scattered across the handlers of every field that can affect it, and nothing in the
/// template gathers it.
///
/// So the table is built the other way round, keyed by the field written rather than the
/// handler doing the writing, which is the form a generated property needs.
///
/// The remaining difficulty is that FormWorks visibility is imperative. A field is shown
/// because something set it so, and the answer is whatever ran most recently. A generated
/// property has to be a predicate over the answers instead, true whenever the conditions
/// hold. Those agree only when every write to a field is guarded and the guards cover the
/// cases; where they do not, this reports the shape rather than guessing.
/// </summary>
public static class VisibilityTable
{
    public static VisibilityTableReport Build(TemplateDocument doc)
    {
        var writes = new List<StateWrite>();
        var unrecovered = new List<string>();
        var unresolved = new List<(string, string, TemplateNode)>();
        var resolver = new NameResolver(doc);

        foreach (var node in doc.AllNodes)
        {
            foreach (var (evt, script) in node.Scripts)
            {
                if (!LuaScript.StateAssignment().IsMatch(script)) continue;

                var statements = ScriptWalker.Walk(
                    script, line => LuaScript.StateAssignment().IsMatch(line));

                if (statements.Count == 0)
                {
                    unrecovered.Add($"{node.Label} [{evt}]: writes state but no structure found");
                    continue;
                }

                foreach (var statement in statements)
                {
                    foreach (System.Text.RegularExpressions.Match assignment
                             in LuaScript.StateAssignment().Matches(statement.Line))
                    {
                        var name = assignment.Groups["field"].Value;
                        var property = assignment.Groups["property"].Value;

                        // "this" is the field the handler is written on. Any other name is
                        // resolved from here, because the same name on nine outcome pages
                        // is nine elements, each written by the handler beside it.
                        var target = name == "this" ? node : resolver.Resolve(name, node);

                        if (target is null)
                        {
                            unresolved.Add((name, property, node));
                            continue;
                        }

                        writes.Add(new StateWrite(
                            target,
                            property,
                            assignment.Groups["value"].Value == "true",
                            node.Label,
                            node,
                            evt,
                            statement.When,
                            statement.Own,
                            name));
                    }
                }
            }
        }

        var fields = writes
            .GroupBy(w => (w.Target.Label, w.Property))
            .Select(g => new FieldState(
                g.First().Target,
                g.First().Name,
                g.Key.Property,
                Shape(g.ToList()),
                g.First().Target.Hidden,
                g.ToList()))
            .OrderBy(f => f.Target.Label, StringComparer.Ordinal)
            .ToList();

        return new VisibilityTableReport(doc.FolderName, fields, unrecovered, unresolved);
    }

    private static StateShape Shape(IReadOnlyList<StateWrite> writes)
    {
        // An unconditional write is a default being set, not a rule, so it does not make
        // the field unreadable on its own. Every guarded write has to be expressible.
        if (writes.Where(w => !w.IsUnconditional).Any(w => !w.FullyParsed))
            return StateShape.Refused;

        if (writes.All(w => w.IsUnconditional))
            return StateShape.Static;

        var sources = writes
            .Select(w => $"{w.SourceField}[{w.SourceEvent}]")
            .Distinct(StringComparer.Ordinal)
            .Count();

        return sources == 1 ? StateShape.SingleSource : StateShape.MultiSource;
    }
}
