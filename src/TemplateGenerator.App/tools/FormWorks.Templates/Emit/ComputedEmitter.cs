using System.Text;
using FormWorks.Templates.Analysis;
using FormWorks.Templates.Model;

namespace FormWorks.Templates.Emit;

public sealed record ComputedResult(
    string Code,
    int Applied,
    IReadOnlyList<ValueWrite> LeftToAPerson);

/// <summary>
/// Writes the answers a form fills in for the agent.
///
/// A third kind of behaviour beside rules and state, and the one that fails most quietly.
/// A rule that is missing shows the wrong thing; a value that is missing shows nothing at
/// all, and a blank field looks exactly like one nobody has reached yet.
///
/// The distinction that shapes the code is that this changes an answer rather than
/// deciding something about it, so most of it is edge-triggered. A clear that ran on every
/// change would erase the agent's typing keystroke by keystroke, where the template clears
/// once, when the field it watches changes. Only values a handler derives on validation are
/// recomputed continuously, and those are idempotent by construction: across the estate not
/// one of them is a clear.
/// </summary>
public static class ComputedEmitter
{
    /// <summary>Events whose writes are a reaction to one field being edited.</summary>
    private static readonly string[] EdgeTriggered = ["OnValueChange"];

    /// <summary>
    /// Events whose writes run when a field is finished with rather than as it is typed.
    ///
    /// The difference is not cosmetic. A rule reading an age fires on the "3" of "30" and
    /// names the tenant a child, then unnames them a keystroke later.
    /// </summary>
    private static readonly string[] OnLeaving = ["OnBlur"];

    /// <summary>Events whose writes are a derived value, recomputed with the rules.</summary>
    private static readonly string[] Continuous = ["OnValidate"];

    public static ComputedResult Emit(
        TemplateDocument doc,
        IReadOnlyList<PageEmitModel> pages,
        ComputedValueReport computed,
        IReadOnlySet<string> clearedOnHide,
        string rootNamespace,
        string className,
        string reportClassName)
    {
        var paths = GuardRenderer.PathIndex(pages);
        var types = GuardRenderer.TypeIndex(pages);
        var fields = pages
            .SelectMany(p => p.AllFields.Select(f => (Page: p, Field: f)))
            .Where(x => x.Field.PropertyName is not null && x.Field.Mapping is not null)
            .GroupBy(x => x.Field.Source.Label, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var resolver = new NameResolver(doc);

        // Fields the form fills in but this cannot: their answers stay empty. Anything
        // derived from one of them would be computed from a blank and come out confidently
        // wrong, which is worse than coming out missing. ContactOK is the case in point:
        // the rule deciding it is plain, but it reads visit counts produced by date
        // arithmetic nothing here can reproduce, so it would answer "Yes" on an empty form
        // and quietly satisfy a rule that should have stopped the agent.
        var starved = computed.Writes
            .Where(w => !Rendered(w, fields, out _))
            .Select(w => w.Target.Label)
            .ToHashSet(StringComparer.Ordinal);

        bool Usable(ValueWrite write)
        {
            if (!Rendered(write, fields, out _)) return false;

            var reads = write.When
                .SelectMany(c => c.Atoms)
                .SelectMany(g => new[] { g.Field, g.Other })
                .Where(f => f is not null && f != "this")
                .Select(f => resolver.Resolve(f!, write.Owner)?.Label)
                .Where(l => l is not null);

            if (write.Source == ValueSource.Copy && write.From is not null)
                reads = reads.Concat([write.From.Label]);

            return !reads.Any(l => starved.Contains(l!));
        }

        // All the writes one handler makes to one field are an if/elseif chain and stand or
        // fall together. Emitting the branches that happen to be expressible leaves a
        // half-chain: One example template would have said the contact rules were unmet and
        // never that they were met, which is worse than saying nothing.
        var applied = new List<ValueWrite>();
        var refused = new List<ValueWrite>();

        foreach (var chain in computed.Writes.GroupBy(w => (w.Target.Label, w.Owner.Label, w.Event)))
        {
            // Clearing a field as its section hides is already done, from the visibility
            // rules rather than from each handler that mentions it. Listing it as work
            // would send someone to write what is written.
            if (chain.Key.Event == "OnHide"
                && chain.All(w => w.Source == ValueSource.Clear)
                && chain.All(w => clearedOnHide.Contains(w.Target.Label)))
                continue;

            if (chain.All(Usable))
                applied.AddRange(chain);
            else
                refused.AddRange(chain);
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"//     Generated from {doc.FolderName}.");
        sb.AppendLine("//     Do not edit. Regenerate instead; edits here are lost.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"using {rootNamespace}.Models.Generated;");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace}.Views.Generated;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Answers a {GuardRenderer.Escape(doc.FolderName)} report fills in for the agent.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Fills in what the template would have filled in.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <param name=\"changed\">");
        sb.AppendLine("    /// The field the agent just edited, by its FormWorks name, or null where nothing");
        sb.AppendLine("    /// was edited. Most of what a form fills in is a reaction to one field changing,");
        sb.AppendLine("    /// and running those continuously would overwrite the agent as they typed.");
        sb.AppendLine("    /// </param>");
        sb.AppendLine($"    public static void Apply({reportClassName} report, string? changed)");
        sb.AppendLine("    {");

        var wrote = EmitContinuous(sb, applied, fields, paths, types, resolver);
        wrote |= EmitEdgeTriggered(sb, applied, fields, paths, types, resolver, EdgeTriggered,
            "        // A reaction to one field being edited, so it runs only then.");

        if (!wrote)
            sb.AppendLine("        // This template fills in nothing the generator can express.");

        sb.AppendLine("    }");

        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Fills in what the template fills in once a field is finished with.");
        sb.AppendLine("    ///");
        sb.AppendLine("    /// Separate from Apply because the template waits for the agent to leave the");
        sb.AppendLine("    /// field. A rule reading an age would otherwise fire on the \"3\" of \"30\" and");
        sb.AppendLine("    /// name the tenant a child, then unname them a keystroke later.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public static void ApplyOnLeaving({reportClassName} report, string? left)");
        sb.AppendLine("    {");

        if (!EmitEdgeTriggered(sb, applied, fields, paths, types, resolver, OnLeaving,
                "        // Run when the agent leaves the field, not as they type in it."))
            sb.AppendLine("        // This template fills nothing in when a field is left.");

        sb.AppendLine("    }");
        sb.AppendLine();
        GuardRenderer.EmitSliceHelper(sb);
        sb.AppendLine("}");

        return new ComputedResult(
            sb.ToString(),
            applied.Count(w => Applies(w)),
            refused.Concat(applied.Where(w => !Applies(w))).ToList());
    }

    /// <summary>
    /// Whether a write is one this emitter actually runs.
    ///
    /// OnOpen and OnHide are left out on purpose. A form's opening values come from the job
    /// the agent was sent, which the exchange pass carries, and clearing on hide is already
    /// done from the visibility rules rather than from each handler that mentions it.
    /// </summary>
    private static bool Applies(ValueWrite write)
        => EdgeTriggered.Contains(write.Event)
           || Continuous.Contains(write.Event)
           || OnLeaving.Contains(write.Event);

    private static bool EmitContinuous(
        StringBuilder sb, IReadOnlyList<ValueWrite> applied,
        IReadOnlyDictionary<string, (PageEmitModel Page, EmittedElement Field)> fields,
        IReadOnlyDictionary<string, string> paths, IReadOnlyDictionary<string, string> types,
        NameResolver resolver)
    {
        var writes = applied.Where(w => Continuous.Contains(w.Event)).ToList();
        if (writes.Count == 0) return false;

        sb.AppendLine("        // Derived values, recomputed whenever anything changes. Safe to run every");
        sb.AppendLine("        // time because none of them blanks a field: they state what an answer is,");
        sb.AppendLine("        // rather than reacting to an edit.");

        foreach (var write in writes)
            EmitOne(sb, write, fields, paths, types, resolver, indent: 2);

        sb.AppendLine();
        return true;
    }

    private static bool EmitEdgeTriggered(
        StringBuilder sb, IReadOnlyList<ValueWrite> applied,
        IReadOnlyDictionary<string, (PageEmitModel Page, EmittedElement Field)> fields,
        IReadOnlyDictionary<string, string> paths, IReadOnlyDictionary<string, string> types,
        NameResolver resolver, IReadOnlyList<string> events, string heading)
    {
        var byOwner = applied
            .Where(w => events.Contains(w.Event))
            .GroupBy(w => w.Owner.Label, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        if (byOwner.Count == 0) return false;

        sb.AppendLine(heading);
        sb.AppendLine($"        switch ({(events == OnLeaving ? "left" : "changed")})");
        sb.AppendLine("        {");

        foreach (var group in byOwner)
        {
            sb.AppendLine($"            case \"{GuardRenderer.Escape(group.Key)}\":");

            foreach (var write in group)
                EmitOne(sb, write, fields, paths, types, resolver, indent: 4);

            sb.AppendLine("                break;");
            sb.AppendLine();
        }

        sb.AppendLine("        }");
        return true;
    }

    private static void EmitOne(
        StringBuilder sb, ValueWrite write,
        IReadOnlyDictionary<string, (PageEmitModel Page, EmittedElement Field)> fields,
        IReadOnlyDictionary<string, string> paths, IReadOnlyDictionary<string, string> types,
        NameResolver resolver, int indent)
    {
        if (!Rendered(write, fields, out var assignment)) return;

        var pad = new string(' ', indent * 4);

        var condition = GuardRenderer.Render(
            write.When, paths, write.Owner.Label, "IsShown",
            name => resolver.Resolve(name, write.Owner)?.Label ?? name,
            types);

        if (condition == "true")
        {
            sb.AppendLine($"{pad}{assignment}");
            return;
        }

        sb.AppendLine($"{pad}if ({condition})");
        sb.AppendLine($"{pad}    {assignment}");
    }

    /// <summary>
    /// The assignment this write becomes, or nothing where it cannot be written at all.
    ///
    /// A copy between fields of different types is refused rather than converted. The
    /// estate copies text into text; anything else is a template doing something this has
    /// not seen, and guessing at a conversion is how a wrong answer reaches the CMS.
    /// </summary>
    private static bool Rendered(
        ValueWrite write,
        IReadOnlyDictionary<string, (PageEmitModel Page, EmittedElement Field)> fields,
        out string assignment)
    {
        assignment = "";

        if (!write.Expressible) return false;
        if (!fields.TryGetValue(write.Target.Label, out var target)) return false;

        var path = $"report.{target.Page.PageName}.{target.Field.PropertyName}";
        var type = target.Field.Mapping!.ClrType;

        switch (write.Source)
        {
            case ValueSource.Clear:
                assignment = $"{path} = {Blank(type)};";
                return true;

            case ValueSource.Literal when type is "string" or "string?":
                assignment = $"{path} = \"{GuardRenderer.Escape(write.Expression)}\";";
                return true;

            case ValueSource.Literal when type == "bool" && write.Expression is "true" or "false":
                assignment = $"{path} = {write.Expression};";
                return true;

            case ValueSource.Copy when write.From is not null
                                       && fields.TryGetValue(write.From.Label, out var from)
                                       && from.Field.Mapping!.ClrType == type:
                assignment = $"{path} = report.{from.Page.PageName}.{from.Field.PropertyName};";
                return true;

            default:
                return false;
        }
    }

    private static string Blank(string clrType) => clrType switch
    {
        "bool" => "false",
        "int" or "double" => "0",
        _ when clrType.EndsWith('?') => "null",
        "string" => "\"\"",
        _ => "default"
    };
}
