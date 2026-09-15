using System.Text.Json;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Model;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

public class RowPackerTests
{
    private static IReadOnlyList<TemplateNode> Fields(params (string Name, int Width, int Margin)[] spec)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Form"] = new Dictionary<string, object>
            {
                ["fieldType"] = "Form",
                ["width"] = 980,
                ["children"] = spec.Select(f => (object)new Dictionary<string, object>
                {
                    ["Text"] = new Dictionary<string, object>
                    {
                        ["fieldType"] = "Text",
                        ["elementName"] = f.Name,
                        ["width"] = f.Width,
                        ["marginLeft"] = f.Margin,
                        ["marginRight"] = f.Margin
                    }
                }).ToArray()
            }
        });

        return TemplateLoader.LoadJson(json, "F V1").Root.Children;
    }

    private static bool Leaf(TemplateNode n) => true;

    [Fact]
    public void Fields_that_fit_share_a_row()
    {
        // The real contact summary row: 160+84+400+70+70 plus margins comes to 954.
        var rows = RowPacker.Pack(
            Fields(("Date", 160, 20), ("Time", 84, 20), ("Type", 400, 20), ("Sec", 70, 10), ("Corr", 70, 10)),
            980, Leaf);

        var row = Assert.Single(rows);
        Assert.Equal(5, row.Count);
    }

    [Fact]
    public void A_field_that_would_overflow_starts_the_next_row()
    {
        var rows = RowPacker.Pack(Fields(("A", 600, 0), ("B", 600, 0)), 980, Leaf);

        Assert.Equal(2, rows.Count);
        Assert.Single(rows[0]);
        Assert.Single(rows[1]);
    }

    [Fact]
    public void Margins_count_towards_the_row()
    {
        // 450 each fits twice on width alone, but not once margins are included.
        var rows = RowPacker.Pack(Fields(("A", 450, 50), ("B", 450, 50)), 980, Leaf);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Five_contacts_become_five_rows_not_twenty_five()
    {
        var spec = new List<(string, int, int)>();
        for (var i = 1; i <= 5; i++)
        {
            spec.Add(($"Date{i}", 160, 20));
            spec.Add(($"Time{i}", 84, 20));
            spec.Add(($"Type{i}", 400, 20));
            spec.Add(($"Sec{i}", 70, 10));
            spec.Add(($"Corr{i}", 70, 10));
        }

        var rows = RowPacker.Pack(Fields(spec.ToArray()), 980, Leaf);

        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.Equal(5, r.Count));
    }

    [Fact]
    public void A_field_with_no_width_takes_its_own_row()
    {
        var rows = RowPacker.Pack(Fields(("A", 100, 0), ("B", 0, 0), ("C", 100, 0)), 980, Leaf);

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void A_container_ends_the_row_so_order_is_preserved()
    {
        var nodes = Fields(("A", 100, 0), ("Group", 100, 0), ("B", 100, 0));
        var rows = RowPacker.Pack(nodes, 980, n => n.ElementName != "Group");

        Assert.Equal(2, rows.Count);
        Assert.Equal("A", rows[0][0].ElementName);
        Assert.Equal("B", rows[1][0].ElementName);
    }

    [Fact]
    public void A_row_that_does_not_fill_its_container_gets_a_filler_column()
    {
        // A 160-wide date alone on a row inside a 490 group is a date field, not a
        // 490-point one. Without the filler it stretches to fill the card.
        var row = Fields(("DateofBirth", 160, 0)).ToList();

        Assert.Equal("160*,330*", RowPacker.ColumnDefinitions(row, 490));
    }

    [Fact]
    public void A_row_that_exactly_fills_its_container_gets_no_filler()
    {
        var row = Fields(("Wide", 980, 0)).ToList();

        Assert.Equal("980*", RowPacker.ColumnDefinitions(row, 980));
    }

    [Fact]
    public void Rows_of_different_totals_still_line_up_column_for_column()
    {
        // The contact summary: four rows totalling 954 and a fifth totalling 964, because
        // one checkbox in the last row is ten points wider. Star columns divide by their
        // own total, so without a filler on both the first column of each row gets a
        // different fraction of the width and the rows drift apart.
        var wide = RowPacker.ColumnDefinitions(Fields(("A", 200, 0), ("B", 754, 0)).ToList(), 980);
        var narrow = RowPacker.ColumnDefinitions(Fields(("A", 200, 0), ("B", 764, 0)).ToList(), 980);

        static int Total(string definitions)
            => definitions.Split(',').Sum(c => int.Parse(c.TrimEnd('*')));

        Assert.Equal(980, Total(wide));
        Assert.Equal(980, Total(narrow));
        Assert.StartsWith("200*", wide);
        Assert.StartsWith("200*", narrow);
    }

    [Fact]
    public void Column_widths_keep_the_ratio_rather_than_the_points()
    {
        var row = Fields(("Date", 160, 20), ("Time", 84, 20)).ToList();

        Assert.StartsWith("200*,124*", RowPacker.ColumnDefinitions(row, 980));
    }
}
