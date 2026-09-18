using System.Text;
using FormWorks.Templates.Analysis;
using FormWorks.Templates.Model;

namespace FormWorks.Templates.Emit;

/// <summary>
/// Writes the list of what the generator could not express, for the person who has to.
///
/// The alternative is printing it at generation time and losing it, which leaves a
/// developer to find the gaps by reading 8,000 lines of generated code and noticing what
/// is absent. Absence is the hardest thing to notice.
///
/// Each entry carries the original handler, because the rule is a translation problem and
/// the source is the specification.
/// </summary>
public static class RemainingWorkEmitter
{
    public static string Emit(
        TemplateDocument doc,
        ValidationTableReport validation,
        ValidatorResult validator,
        VisibilityResult state,
        ComputedResult computed,
        IReadOnlyList<PageEmitModel> pages)
    {
        var refused = validation.Rules.Where(r => !r.FullyParsed).ToList();
        var scripts = doc.AllNodes.ToDictionary(n => n.Label, n => n, StringComparer.Ordinal);

        // One numbering, computed once, for both this document and a `formworks worklist`
        // CSV export - so "item 14" means the same field whichever of the two a developer
        // is looking at. See WorklistEmitter for why the order is stable across runs.
        var numbers = WorklistEmitter.Build(validation, state, computed)
            .ToDictionary(i => (i.Category, i.Field, i.Property), i => i.Id);

        int NumberOf(WorklistCategory category, string field, string? property = null)
            => numbers[(category, field, property)];

        var sb = new StringBuilder();
        sb.AppendLine($"# {doc.FolderName} — what is left to a person");
        sb.AppendLine();
        sb.AppendLine("Generated. Do not edit; it is rewritten on every run.");
        sb.AppendLine();
        sb.AppendLine("Everything not listed here is generated and needs no attention. What is listed");
        sb.AppendLine("is logic the generator refused to guess at, with the original handler alongside.");
        sb.AppendLine();
        sb.AppendLine("Write these in the hand-written half of the page's view model, in a file outside");
        sb.AppendLine("`Generated/`. See `docs/finishing-a-converted-template.md`.");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Pages | {pages.Count} |");
        sb.AppendLine($"| Fields bound | {pages.Sum(p => p.AllFields.Count)} |");
        sb.AppendLine($"| Validation rules generated | {validator.RulesEmitted} |");
        sb.AppendLine($"| Validation rules left to a person | {refused.Count} |");
        sb.AppendLine($"| Shown/usable rules generated | {state.FieldsEmitted} |");
        sb.AppendLine($"| Shown/usable rules left to a person | {state.LeftToAPerson.Count} |");
        sb.AppendLine($"| Answers the form fills in | {computed.Applied} |");
        sb.AppendLine($"| Answers left to a person | {computed.LeftToAPerson.Count} |");

        if (state.Unattributed.Count > 0)
            sb.AppendLine($"| Rules naming more than one element | {state.Unattributed.Count} |");

        // Distinct, because one field with two branches needs one piece of wording, not
        // two, and a count that disagrees with the list below it is just noise.
        if (validator.Wording.Count > 0)
            sb.AppendLine($"| Messages needing wording | {validator.Wording.Distinct().Count()} |");

        sb.AppendLine();

        if (refused.Count == 0 && state.LeftToAPerson.Count == 0
            && state.Unattributed.Count == 0 && computed.LeftToAPerson.Count == 0)
        {
            sb.AppendLine("## Nothing outstanding");
            sb.AppendLine();
            sb.AppendLine("Every rule in this template was expressible.");
            return sb.ToString();
        }

        if (refused.Count == 0)
        {
            sb.AppendLine("Every validation rule in this template was expressible.");
            sb.AppendLine();
        }

        if (refused.Count > 0)
        {
            sb.AppendLine("## Validation rules the generator could not express");
            sb.AppendLine();
            sb.AppendLine("Each of these decides validity from something worked out earlier in the handler:");
            sb.AppendLine("a counter, a running flag, or arithmetic. The generator emits comparisons, not");
            sb.AppendLine("computation, so it stops rather than approximating.");
            sb.AppendLine();
        }

        foreach (var group in refused.GroupBy(r => r.Field, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var node = scripts.GetValueOrDefault(group.Key);
            var page = group.First().Page;

            sb.AppendLine($"### {NumberOf(WorklistCategory.Validation, group.Key)}. {group.Key}");
            sb.AppendLine();
            sb.AppendLine($"On page **{page}**. {group.Count()} rule(s).");
            sb.AppendLine();

            // Deliberately no condition summary. These rules were refused because the
            // condition could not be expressed, and the guards recovered from separate
            // branches read as one conjunction, which can be self-contradictory. The
            // handler below is the truth; a plausible-looking paraphrase above it would
            // only invite someone to trust it.
            foreach (var rule in group)
            {
                var message = rule.Message is null
                    ? $"a message computed at runtime from `{rule.MessageExpression}`"
                    : $"\"{rule.Message}\"";

                sb.AppendLine($"- Sets the field invalid, saying {message}. Read the handler below for when.");
            }

            if (node is not null)
            {
                foreach (var (evt, script) in node.Scripts.Where(s => s.Value.Contains("this.valid")))
                {
                    sb.AppendLine();
                    sb.AppendLine($"The original `{evt}` handler:");
                    sb.AppendLine();
                    sb.AppendLine("```lua");
                    sb.AppendLine(script.Trim());
                    sb.AppendLine("```");
                }
            }

            sb.AppendLine();
        }

        EmitUnread(sb, doc);
        EmitMessages(sb, doc, validator);
        EmitComputed(sb, computed, numbers);
        EmitState(sb, doc, state, numbers);
        EmitUnattributed(sb, doc, state, numbers);
        return sb.ToString();
    }

    /// <summary>
    /// Handlers on events nothing in the generator reads.
    ///
    /// FormWorks has twelve events and six of them carry 99% of the code, so the passes
    /// were built for those. The rest are not rare enough to ignore silently: a handler
    /// nobody reads is a behaviour nobody knows is missing, which is the failure this file
    /// exists to prevent.
    ///
    /// Reported per template rather than counted, because the ones that matter are
    /// specific: a form that reopens the previous report on submission, or a page that
    /// seeds shared variables before anything runs.
    /// </summary>
    private static void EmitUnread(StringBuilder sb, TemplateDocument doc)
    {
        // What the passes actually look at. Anything else is unread by definition.
        string[] read = ["OnValidate", "OnValueChange", "OnBlur", "OnTap", "OnHide", "OnOpen"];

        var unread = doc.AllNodes
            .SelectMany(n => n.Scripts.Select(s => (Node: n, Event: s.Key, Script: s.Value)))
            .Where(h => !read.Contains(h.Event, StringComparer.Ordinal))
            .Where(h => HasCode(h.Script))
            .OrderBy(h => h.Event, StringComparer.Ordinal)
            .ToList();

        if (unread.Count == 0) return;

        sb.AppendLine("## Handlers the generator does not read");
        sb.AppendLine();
        sb.AppendLine("Nothing in the generator looks at these events, so whatever they do is not being");
        sb.AppendLine("done. They are listed rather than counted because what they do is specific, and a");
        sb.AppendLine("handler nobody reads is a behaviour nobody knows is missing.");
        sb.AppendLine();

        foreach (var group in unread.GroupBy(h => h.Event, StringComparer.Ordinal))
        {
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();

            foreach (var handler in group)
            {
                sb.AppendLine($"On `{handler.Node.Label}`:");
                sb.AppendLine();
                sb.AppendLine("```lua");
                sb.AppendLine(handler.Script.Trim());
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }
    }

    /// <summary>Whether a handler is anything more than a comment.</summary>
    private static bool HasCode(string script)
        => script
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.IndexOf("--", StringComparison.Ordinal) is var i && i >= 0 ? line[..i] : line)
            .Any(line => line.Trim().Length > 0);

    /// <summary>
    /// Answers the form fills in for the agent that the generator cannot.
    ///
    /// The quietest failure of the three. A rule that is missing shows the wrong thing; a
    /// value that is missing shows nothing, and a blank field looks exactly like one nobody
    /// has reached yet. So these are listed even though there is nothing wrong with the
    /// generated code: the gap is invisible on screen.
    /// </summary>
    private static void EmitComputed(
        StringBuilder sb, ComputedResult computed,
        IReadOnlyDictionary<(WorklistCategory, string, string?), int> numbers)
    {
        if (computed.LeftToAPerson.Count == 0) return;

        sb.AppendLine("## Answers the form fills in");
        sb.AppendLine();
        sb.AppendLine("The template writes these answers for the agent. Until they are written by hand the");
        sb.AppendLine("fields stay blank, and a blank field looks like one nobody has reached yet, so");
        sb.AppendLine("nothing on screen will tell you they are missing.");
        sb.AppendLine();
        sb.AppendLine("Write them in the page's hand-written view model, in the same place as a validation");
        sb.AppendLine("rule. Most read a value the handler works out first.");
        sb.AppendLine();
        sb.AppendLine("`OnOpen` writes are the exception: the report opens once for the whole job, not");
        sb.AppendLine("once per page, so those belong in the report's own hand-written `OnOpened()`");
        sb.AppendLine("instead of a page's view model.");
        sb.AppendLine();

        foreach (var group in computed.LeftToAPerson
                     .GroupBy(w => w.Target.Label, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var first = group.First();
            var number = numbers[(WorklistCategory.ComputedValue, group.Key, null)];

            sb.AppendLine($"### {number}. {group.Key}");
            sb.AppendLine();

            if (first.Target.Page?.Title is { Length: > 0 } page)
                sb.AppendLine($"On page **{page}**.");
            else
                sb.AppendLine("Not on any page of this template.");

            sb.AppendLine();

            foreach (var write in group)
            {
                sb.AppendLine(
                    $"- Set from `{write.Owner.Label}` [{write.Event}] to {Describe(write)}");
            }

            sb.AppendLine();

            // "Which the handler works out first" can be a date sum, a lookup in an
            // in-memory table, or a database query the handler makes itself: the field's
            // caption does not say which, so the source is the only way to tell them apart.
            var shown = new HashSet<(string, string)>();

            foreach (var write in group.Where(w => w.Source == ValueSource.Computed))
            {
                if (!shown.Add((write.Owner.Label, write.Event))) continue;
                if (!write.Owner.Scripts.TryGetValue(write.Event, out var script)) continue;

                sb.AppendLine($"The original `{write.Event}` handler on `{write.Owner.Label}`:");
                sb.AppendLine();
                sb.AppendLine("```lua");
                sb.AppendLine(script.Trim());
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }
    }

    private static string Describe(ValueWrite write) => write.Source switch
    {
        ValueSource.Clear => "nothing, clearing it",
        ValueSource.Literal => $"\"{write.Expression}\"",
        ValueSource.Copy => $"whatever `{write.Expression}` holds",
        ValueSource.Inbound => $"`{write.Expression}`, which comes with the job",
        _ => $"`{write.Expression}`, which the handler works out first"
    };

    /// <summary>
    /// Messages the template worked out at runtime.
    ///
    /// Counted before but never listed, so the count told a developer there was something
    /// to do and nothing told them what. The rule works either way: the field's caption
    /// stands in, so the agent is told which field is wrong but not why.
    /// </summary>
    private static void EmitMessages(StringBuilder sb, TemplateDocument doc, ValidatorResult validator)
    {
        if (validator.Wording.Count == 0) return;

        sb.AppendLine("## Messages needing wording");
        sb.AppendLine();
        sb.AppendLine("The template built each of these at runtime, usually by appending to a local, so");
        sb.AppendLine("the generator cannot recover the text. The field's caption stands in, which names");
        sb.AppendLine("the field but does not say what is wrong with it.");
        sb.AppendLine();
        sb.AppendLine("Set these in the page's `OnValidated`, after checking the field's error is not null.");
        sb.AppendLine();
        sb.AppendLine("| Field | Currently says | Built from |");
        sb.AppendLine("|---|---|---|");

        foreach (var (field, expression, caption) in validator.Wording
                     .Distinct()
                     .OrderBy(w => w.Field, StringComparer.Ordinal))
        {
            sb.AppendLine($"| `{field}` | \"{caption}\" | `{expression}` |");
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Fields whose shown or usable state could not be stated as a predicate.
    ///
    /// Three different failures, kept apart because they need different work. A guard that
    /// did not parse needs the condition writing out. Several handlers writing one field
    /// needs a decision about which of them wins, which the template does not record. A
    /// field only ever shown and never hidden needs someone to say whether it should hide
    /// again, because FormWorks state persists and a predicate does not.
    /// </summary>
    private static void EmitState(
        StringBuilder sb, TemplateDocument doc, VisibilityResult state,
        IReadOnlyDictionary<(WorklistCategory, string, string?), int> numbers)
    {
        if (state.LeftToAPerson.Count == 0) return;

        var nodes = new Dictionary<string, TemplateNode>(StringComparer.Ordinal);

        foreach (var node in doc.AllNodes)
            foreach (var name in node.Names)
                nodes.TryAdd(name, node);

        sb.AppendLine("## Fields whose shown state is left to a person");
        sb.AppendLine();
        sb.AppendLine("Until these are written each field keeps the state the template gave it, so one");
        sb.AppendLine("the template starts hidden stays hidden and one it starts shown stays shown. That");
        sb.AppendLine("is the conservative reading in both directions: no question is silently skipped,");
        sb.AppendLine("and nothing the form was never going to show appears.");
        sb.AppendLine();

        foreach (var group in state.LeftToAPerson
                     .GroupBy(Why)
                     .OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();

            foreach (var field in group.OrderBy(f => f.Target.Label, StringComparer.Ordinal))
            {
                var where = field.Target.Page?.Title;
                var number = numbers[(WorklistCategory.State, field.Target.Label, field.Property)];

                sb.AppendLine(
                    $"- **{number}.** **{field.Target.Label}**`.{field.Property}`"
                    + (where is null ? "" : $" on {where}")
                    + $" — {field.Writes.Count} write(s) from "
                    + string.Join(", ", field.Sources.Select(s => $"`{s}`")));
            }

            sb.AppendLine();
        }
    }

    /// <summary>
    /// Rules that name more than one element.
    ///
    /// The rule is real and the template means something by it, but the name it is written
    /// under is claimed by several elements and none of them owns it, so which one it is
    /// about cannot be read off the template. Every claimant gets somewhere to decide it,
    /// defaulting to the state the template gave that element.
    ///
    /// Reported because the alternative is the rule going quiet: it was expressible, so it
    /// is not refused, and it reached no element, so it does nothing. That is the one
    /// failure a worklist exists to prevent.
    /// </summary>
    private static void EmitUnattributed(
        StringBuilder sb, TemplateDocument doc, VisibilityResult state,
        IReadOnlyDictionary<(WorklistCategory, string, string?), int> numbers)
    {
        if (state.Unattributed.Count == 0) return;

        sb.AppendLine("## Rules that name more than one element");
        sb.AppendLine();
        sb.AppendLine("Each of these is understood, but the name it is written under belongs to several");
        sb.AppendLine("elements and nothing in the template says which. So nothing is bound: every one of");
        sb.AppendLine("them keeps the state the template gave it, and the rule does nothing at all until");
        sb.AppendLine("someone says which element it is about.");
        sb.AppendLine();
        sb.AppendLine("Work out which from the handlers listed, then add the rule by hand to that page's");
        sb.AppendLine("view model. If the answer is obvious from the template, it is worth telling the");
        sb.AppendLine("generator how to see it rather than writing it once per template.");
        sb.AppendLine();

        foreach (var rule in state.Unattributed.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            var claimants = doc.AllNodes
                .Where(n => n.Names.Contains(rule.Name, StringComparer.Ordinal))
                .ToList();

            var number = numbers[(WorklistCategory.Unattributed, rule.Name, rule.Property)];

            sb.AppendLine($"### {number}. {rule.Name}`.{rule.Property}`");
            sb.AppendLine();
            sb.AppendLine($"Written by {string.Join(", ", rule.Sources.Select(w => $"`{w}`"))}.");
            sb.AppendLine();
            sb.AppendLine($"Claimed by {claimants.Count} elements:");
            sb.AppendLine();

            foreach (var node in claimants.OrderBy(n => n.Label, StringComparer.Ordinal))
            {
                sb.AppendLine(
                    $"- `{node.Label}` ({node.FieldType})"
                    + (node.Hidden ? ", starts hidden" : ""));
            }

            sb.AppendLine();
        }
    }

    /// <summary>
    /// Why a field's shown/usable state is left to a person. Internal rather than private:
    /// WorklistEmitter groups by the same reason, so the two have to agree on the text.
    /// </summary>
    internal static string Why(FieldState field) => field.Shape switch
    {
        StateShape.MultiSource =>
            "Several handlers decide it, and the template does not record which wins",
        StateShape.Refused =>
            "A condition the generator could not express",
        _ when field.Inversion is Inversion.ShowOnly =>
            "Shown but never hidden again, so it is not a predicate",
        _ when field.Inversion is Inversion.HideOnly =>
            "Hidden but never shown again, so it is not a predicate",
        _ => "Left out for another reason"
    };
}
