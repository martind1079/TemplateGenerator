using System.Text;
using FormWorks.Templates.Analysis;

namespace FormWorks.Templates.Emit;

/// <summary>What kind of work an item is, matching Remaining.md's own sections.</summary>
public enum WorklistCategory
{
    Validation,
    ComputedValue,
    State,
    Unattributed
}

/// <summary>
/// One thing left to a person, numbered so a tracking spreadsheet and Remaining.md can
/// point at the same row without either one owning the other.
/// </summary>
public sealed record WorklistItem(
    int Id,
    WorklistCategory Category,
    string Field,
    string? Property,
    string? Page,
    string Summary);

/// <summary>
/// Numbers every item Remaining.md lists as left to a person, so a developer's own
/// tracking spreadsheet can reference "item 14" and mean the same thing Remaining.md does.
///
/// The numbering has to come from one place. Remaining.md and a CSV export are two
/// different renderings of the same list, and if each counted independently a template
/// with two runs of `formworks generate` and one of `formworks worklist` would end up with
/// two conflicting sets of numbers for the same fields. <see cref="Build"/> is that one
/// place; both renderings call it.
///
/// Stable across runs of the same template.json, because the order is alphabetical by
/// field name within each category rather than the order fields happened to be discovered
/// in - discovery order can shift with unrelated changes elsewhere in the template, and a
/// spreadsheet row whose number silently pointed at a different field next time would be
/// worse than not numbering at all.
/// </summary>
public static class WorklistEmitter
{
    public static IReadOnlyList<WorklistItem> Build(TemplateAnalysis analysis)
        => Build(analysis.Validation, analysis.Validator, analysis.State, analysis.Computed);

    public static IReadOnlyList<WorklistItem> Build(
        ValidationTableReport validation, ValidatorResult validator, VisibilityResult state, ComputedResult computed)
    {
        var items = new List<WorklistItem>();
        var id = 1;

        // validator.RefusedFields, not `!r.FullyParsed`: a rule can parse fine and still
        // be refused once resolving its condition shows it reads a page's own shown state
        // rather than a field's, which validator is the one place that already knows.
        foreach (var group in validation.Rules
                     .Where(r => validator.RefusedFields.Contains(r.Field))
                     .GroupBy(r => r.Field, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            items.Add(new WorklistItem(
                id++, WorklistCategory.Validation, group.Key, null, group.First().Page,
                $"Validation rule the generator could not express ({group.Count()} rule(s))."));
        }

        foreach (var group in computed.LeftToAPerson
                     .GroupBy(w => w.Target.Label, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            items.Add(new WorklistItem(
                id++, WorklistCategory.ComputedValue, group.Key, null,
                group.First().Target.Page?.Title,
                "Answer the form fills in that the generator could not express."));
        }

        foreach (var group in state.LeftToAPerson.GroupBy(RemainingWorkEmitter.Why))
        {
            foreach (var field in group.OrderBy(f => f.Target.Label, StringComparer.Ordinal))
            {
                items.Add(new WorklistItem(
                    id++, WorklistCategory.State, field.Target.Label, field.Property,
                    field.Target.Page?.Title, group.Key));
            }
        }

        foreach (var rule in state.Unattributed.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            items.Add(new WorklistItem(
                id++, WorklistCategory.Unattributed, rule.Name, rule.Property, null,
                "Rule names more than one element; which one it is about is not recorded."));
        }

        return items;
    }

    /// <summary>
    /// Every item as CSV, with blank Status and Notes columns for a developer to fill in
    /// and Excel to keep. Deliberately CSV rather than a real spreadsheet file: it opens
    /// and edits in Excel identically for this, and needs no library in a tools project
    /// that otherwise has no package dependencies at all.
    /// </summary>
    public static string EmitCsv(IReadOnlyList<WorklistItem> items, string templateName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,Template,Category,Page,Field,Property,Summary,Status,Notes");

        foreach (var item in items)
        {
            sb.AppendLine(string.Join(",",
                item.Id,
                Cell(templateName),
                Cell(Describe(item.Category)),
                Cell(item.Page ?? ""),
                Cell(item.Field),
                Cell(item.Property ?? ""),
                Cell(item.Summary),
                Cell(""),
                Cell("")));
        }

        return sb.ToString();
    }

    private static string Describe(WorklistCategory category) => category switch
    {
        WorklistCategory.Validation => "Validation rule",
        WorklistCategory.ComputedValue => "Answer the form fills in",
        WorklistCategory.State => "Shown/usable state",
        WorklistCategory.Unattributed => "Rule naming more than one element",
        _ => category.ToString()
    };

    /// <summary>Quoted and escaped per RFC 4180, so a comma or quote in a title or caption
    /// cannot shift a row's columns.</summary>
    private static string Cell(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
