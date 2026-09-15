using FormWorks.Templates.Model;

namespace FormWorks.Templates.Emit;

/// <summary>
/// Recovers the rows a template lays its fields out in.
///
/// FormWorks stores no row marker. Fields carry a width in points against the form's
/// design surface and flow left to right, wrapping when the next one would not fit. So
/// a row is an accumulation, and the wrap points fall out of the arithmetic rather than
/// being declared anywhere.
///
/// This matters more than it sounds. Emitting every field as its own full-width row is
/// structurally correct and unusable: the contact summary becomes 25 stacked rows where
/// the template shows five contacts as five columns.
/// </summary>
public static class RowPacker
{
    /// <summary>
    /// Groups consecutive siblings into rows. A node that is itself a container ends the
    /// current row, because its children are laid out against their own width.
    /// </summary>
    public static List<List<TemplateNode>> Pack(
        IReadOnlyList<TemplateNode> siblings, int containerWidth, Func<TemplateNode, bool> isLeaf)
    {
        var rows = new List<List<TemplateNode>>();
        var current = new List<TemplateNode>();
        var used = 0;

        foreach (var node in siblings)
        {
            if (!isLeaf(node))
            {
                Flush(rows, ref current, ref used);
                continue;
            }

            var width = node.OuterWidth;

            // A field with no declared width, or one wider than the container, takes a
            // row to itself rather than being packed against a number that means nothing.
            if (width <= 0 || width > containerWidth)
            {
                Flush(rows, ref current, ref used);
                rows.Add([node]);
                continue;
            }

            if (current.Count > 0 && used + width > containerWidth)
                Flush(rows, ref current, ref used);

            current.Add(node);
            used += width;
        }

        Flush(rows, ref current, ref used);
        return rows;
    }

    private static void Flush(List<List<TemplateNode>> rows, ref List<TemplateNode> current, ref int used)
    {
        if (current.Count > 0) rows.Add(current);
        current = [];
        used = 0;
    }

    /// <summary>
    /// Column widths for a row, as MAUI star values, with a filler column for whatever
    /// the row does not use.
    ///
    /// The template's widths are points against a fixed 980 surface. Reproducing them
    /// literally would put a desktop form on a tablet and cut it off on anything smaller,
    /// so the ratios are kept and the absolute sizes are not.
    ///
    /// The filler is what stops a narrow field stretching. A date of birth declared 160
    /// wide, alone on its row inside a 490 group, is a date field and not a 490-point
    /// one; without a column to absorb the remaining 330 it fills the card.
    /// </summary>
    public static string ColumnDefinitions(IReadOnlyList<TemplateNode> row, int containerWidth)
    {
        var columns = row.Select(n => Math.Max(n.OuterWidth, 1)).ToList();
        var remainder = containerWidth - columns.Sum();

        // Every remainder, however small. Star columns divide the width by their total, so
        // a row that adds up to 964 and a row that adds up to 980 give their first column
        // different fractions and the two rows stop lining up. The contact summary has
        // exactly this: four rows totalling 954 and a fifth totalling 964, because one
        // checkbox in the last row is ten points wider than its neighbours above.
        if (remainder > 0)
            columns.Add(remainder);

        return string.Join(",", columns.Select(w => $"{w}*"));
    }
}
