using System.Text.Json;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// A field's width is the space it takes in the row; its content width is how wide the
/// control may draw inside that. 4,369 fields across the estate set the two differently.
/// Margins are the template's own spacing between components.
/// </summary>
public class SpacingTests
{
    private static PageEmitModel Page(
        int width, int? contentWidth = null,
        int left = 0, int top = 0, int right = 0, int bottom = 0, string type = "Text")
    {
        var field = new Dictionary<string, object>
        {
            ["fieldType"] = type, ["elementName"] = "F", ["name"] = "F", ["title"] = "F",
            ["width"] = width,
            ["marginLeft"] = left, ["marginTop"] = top,
            ["marginRight"] = right, ["marginBottom"] = bottom
        };
        if (contentWidth is { } cw) field["contentWidth"] = cw;

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
                                        ["title"] = "S", ["width"] = 980,
                                        ["children"] = new object[] { new Dictionary<string, object> { [type] = field } }
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
    public void A_narrower_content_width_limits_the_control_not_the_cell()
    {
        var model = Page(width: 230, contentWidth: 140);

        Assert.Equal(140, model.AllFields.Single().ContentWidth);

        var xaml = XamlEmitter.EmitPage(model, "App");
        Assert.Contains("WidthRequest=\"140\"", xaml);

        // Without this the control stretches back to fill the cell it was narrowed inside.
        Assert.Contains("HorizontalOptions=\"Start\"", xaml);
    }

    [Fact]
    public void A_content_width_equal_to_the_width_is_not_emitted()
    {
        var model = Page(width: 230, contentWidth: 230);

        Assert.Null(model.AllFields.Single().ContentWidth);
        Assert.DoesNotContain("WidthRequest", XamlEmitter.EmitPage(model, "App"));
    }

    [Fact]
    public void A_missing_content_width_leaves_the_control_alone()
    {
        var model = Page(width: 230);

        Assert.Null(model.AllFields.Single().ContentWidth);
    }

    [Fact]
    public void The_row_still_reserves_the_full_width_not_the_content_width()
    {
        // Two 600-wide fields do not share a row even though their content is 140 each.
        var doc = TemplateLoader.LoadJson(JsonSerializer.Serialize(new Dictionary<string, object>
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
                            ["children"] = new[] { "A", "B" }.Select(n => (object)new Dictionary<string, object>
                            {
                                ["Text"] = new Dictionary<string, object>
                                {
                                    ["fieldType"] = "Text", ["elementName"] = n, ["name"] = n,
                                    ["title"] = n, ["width"] = 600, ["contentWidth"] = 140
                                }
                            }).ToArray()
                        }
                    }
                }
            }
        }), "F V1");

        var model = PageEmitModelBuilder.Build(doc, doc.Pages.Single(), ControlVocabulary.Default, "");

        Assert.Equal(2, model.Rows.Count);
    }

    [Fact]
    public void Template_margins_are_emitted_in_maui_order()
    {
        var xaml = XamlEmitter.EmitPage(Page(width: 200, left: 20, top: 10, right: 20, bottom: 5), "App");

        Assert.Contains("Margin=\"20,10,20,5\"", xaml);
    }

    [Fact]
    public void A_field_with_no_margins_gets_no_margin_attribute()
    {
        var xaml = XamlEmitter.EmitPage(Page(width: 200), "App");

        Assert.DoesNotContain("Margin=", xaml);
    }

    [Fact]
    public void The_emitter_adds_no_spacing_of_its_own_on_top_of_the_margins()
    {
        // A hardcoded gap plus a declared margin is two gaps, and the template already
        // says what the spacing should be.
        var xaml = XamlEmitter.EmitPage(Page(width: 200, left: 20, top: 10, right: 20, bottom: 10), "App");

        Assert.DoesNotContain("ColumnSpacing", xaml);
        Assert.DoesNotContain("Spacing=\"14\"", xaml);
    }
}
