using System.Text.Json;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// A checkbox reads as a tick against a statement, not as a box with a heading above it.
/// </summary>
public class CaptionPlacementTests
{
    private static PageEmitModel Page(string type, string title, ControlVocabulary? vocabulary = null)
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
                                        ["title"] = "S", ["width"] = 980,
                                        ["children"] = new object[]
                                        {
                                            new Dictionary<string, object>
                                            {
                                                [type] = new Dictionary<string, object>
                                                {
                                                    ["fieldType"] = type, ["elementName"] = "F",
                                                    ["name"] = "F", ["title"] = title, ["width"] = 940
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        var doc = TemplateLoader.LoadJson(json, "F V1");
        return PageEmitModelBuilder.Build(
            doc, doc.Pages.Single(), vocabulary ?? ControlVocabulary.Default, "");
    }

    [Fact]
    public void A_checkbox_puts_its_caption_beside_the_control()
    {
        var xaml = XamlEmitter.EmitPage(Page("Checkbox", "Passport"), "App");

        var checkbox = xaml.IndexOf("<CheckBox", StringComparison.Ordinal);
        var caption = xaml.IndexOf("Passport", StringComparison.Ordinal);

        Assert.True(checkbox > 0 && caption > checkbox, "the control should come before its caption");
        Assert.Contains("Grid.Column=\"1\"", xaml);
    }

    [Fact]
    public void The_caption_sits_in_a_star_column_so_long_text_wraps()
    {
        // A horizontal stack would let the caption take whatever width it asked for and
        // run off the row rather than wrapping.
        var xaml = XamlEmitter.EmitPage(Page("Checkbox", "A caption long enough to need two lines"), "App");

        Assert.Contains("ColumnDefinitions=\"Auto,*\"", xaml);
        Assert.DoesNotContain("<HorizontalStackLayout", xaml);
    }

    [Fact]
    public void A_text_field_still_puts_its_caption_above()
    {
        var xaml = XamlEmitter.EmitPage(Page("Text", "Our Reference"), "App");

        var caption = xaml.IndexOf("Our Reference", StringComparison.Ordinal);
        var entry = xaml.IndexOf("<Entry", StringComparison.Ordinal);

        Assert.True(caption > 0 && entry > caption, "the caption should come before the control");
    }

    [Fact]
    public void A_checkbox_with_no_caption_emits_no_label()
    {
        var xaml = XamlEmitter.EmitPage(Page("Checkbox", ""), "App");

        Assert.Contains("<CheckBox", xaml);
        Assert.DoesNotContain("Grid.Column=\"1\"", xaml);
    }

    [Fact]
    public void The_gap_beside_the_control_is_configuration()
    {
        var xaml = XamlEmitter.EmitPage(
            Page("Checkbox", "Passport", new ControlVocabulary { CaptionGap = 20 }), "App");

        Assert.Contains("ColumnSpacing=\"20\"", xaml);
    }
}
