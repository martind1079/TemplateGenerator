using System.Text.Json;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// A FormWorks group is a layout container with its own width, and a section uses two of
/// them to put questions side by side. Emitting containers one per row stacks those
/// columns, which is a different form from the one the template describes.
/// </summary>
public class GroupLayoutTests
{
    private static object Field(string type, string name, int width) => new Dictionary<string, object>
    {
        [type] = new Dictionary<string, object>
        {
            ["fieldType"] = type, ["elementName"] = name, ["name"] = name,
            ["title"] = name, ["width"] = width
        }
    };

    private static object Group(string name, string title, int width, params object[] children)
        => new Dictionary<string, object>
        {
            ["Group"] = new Dictionary<string, object>
            {
                ["fieldType"] = "Group", ["elementName"] = name, ["name"] = name,
                ["title"] = title, ["width"] = width, ["children"] = children
            }
        };

    private static PageEmitModel Page(params object[] sectionChildren)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Form"] = new Dictionary<string, object>
            {
                ["fieldType"] = "Form", ["width"] = 980,
                ["children"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["Page"] = new Dictionary<string, object>
                        {
                            ["fieldType"] = "Page", ["elementName"] = "P", ["name"] = "P", ["width"] = 980,
                            ["children"] = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["Section"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Section", ["elementName"] = "S", ["name"] = "S",
                                        ["title"] = "Customer Details", ["width"] = 980,
                                        ["children"] = sectionChildren
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        var doc = TemplateLoader.LoadJson(json, "F V1");
        return PageEmitModelBuilder.Build(doc, doc.Pages.Single(), ControlVocabulary.Default, "");
    }

    [Fact]
    public void Two_groups_that_fit_the_section_sit_side_by_side()
    {
        // The real customer details section: two 490-point groups in a 980 section.
        var model = Page(
            Group("Borrower1", "Customer 1", 490, Field("Text", "Name", 450)),
            Group("Borrower2", "Customer 2", 490, Field("Text", "Name", 450)));

        var section = Assert.IsType<ContainerCell>(model.Rows.Single().Cells.Single()).Group;
        var row = Assert.Single(section.Rows);

        Assert.Equal(2, row.Cells.Count);
        Assert.All(row.Cells, c => Assert.IsType<ContainerCell>(c));
        Assert.Equal("490*,490*", row.ColumnDefinitions);
    }

    [Fact]
    public void Groups_of_different_widths_keep_their_proportions()
    {
        // Financials: a 330 beside a 650.
        var model = Page(
            Group("FinancialSituation", "Financial Situation", 330, Field("Text", "Balance", 190)),
            Group("AdditionalInformation", "Additional Information", 650, Field("ParagraphText", "Notes", 600)));

        var section = Assert.IsType<ContainerCell>(model.Rows.Single().Cells.Single()).Group;

        Assert.Equal("330*,650*", Assert.Single(section.Rows).ColumnDefinitions);
    }

    [Fact]
    public void Groups_that_do_not_fit_together_go_on_separate_rows()
    {
        var model = Page(
            Group("A", "A", 600, Field("Text", "X", 400)),
            Group("B", "B", 600, Field("Text", "Y", 400)));

        var section = Assert.IsType<ContainerCell>(model.Rows.Single().Cells.Single()).Group;

        Assert.Equal(2, section.Rows.Count);
    }

    [Fact]
    public void A_groups_children_pack_against_the_group_width_not_the_section_width()
    {
        // 450 and 160 fit easily in 980 but not together in 490.
        var model = Page(
            Group("Borrower1", "Customer 1", 490,
                Field("Text", "Name", 450), Field("Date", "DateofBirth", 160)));

        var section = Assert.IsType<ContainerCell>(model.Rows.Single().Cells.Single()).Group;
        var group = Assert.IsType<ContainerCell>(Assert.Single(section.Rows).Cells.Single()).Group;

        Assert.Equal(2, group.Rows.Count);
    }

    [Fact]
    public void A_titled_group_becomes_a_card_and_an_untitled_one_does_not()
    {
        var titled = XamlEmitter.EmitPage(
            Page(Group("G", "Customer 1", 490, Field("Text", "Name", 450))), "App");
        var untitled = XamlEmitter.EmitPage(
            Page(Group("G", "", 490, Field("Text", "Name", 450))), "App");

        Assert.Contains("SubSectionControl Title=\"Customer 1\"", titled);
        Assert.DoesNotContain("SubSectionControl Title=\"\"", untitled);
    }

    [Fact]
    public void Fields_and_groups_keep_their_template_order()
    {
        var model = Page(
            Field("Text", "First", 980),
            Group("G", "Middle", 980, Field("Text", "Inner", 450)),
            Field("Text", "Last", 980));

        var section = Assert.IsType<ContainerCell>(model.Rows.Single().Cells.Single()).Group;

        Assert.Equal(3, section.Rows.Count);
        Assert.IsType<ElementCell>(section.Rows[0].Cells.Single());
        Assert.IsType<ContainerCell>(section.Rows[1].Cells.Single());
        Assert.IsType<ElementCell>(section.Rows[2].Cells.Single());
    }
}
