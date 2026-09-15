using System.Text.Json;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// Paragraph fields declare how many lines they are sized for. An editor left to size
/// itself expands to fill whatever it is given, which turned a two-field panel into a
/// full screen of empty box.
/// </summary>
public class TextLinesTests
{
    private static PageEmitModel Page(string type, int? textLines)
    {
        var field = new Dictionary<string, object>
        {
            ["fieldType"] = type, ["elementName"] = "Notes", ["name"] = "Notes",
            ["title"] = "Notes", ["width"] = 600
        };
        if (textLines is { } n) field["textLines"] = n;

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

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(13)]
    public void A_paragraph_field_is_sized_from_its_declared_line_count(int lines)
    {
        var model = Page("ParagraphText", lines);

        var expected = ControlVocabulary.Default.HeightForLines(lines);
        Assert.Equal(expected, model.AllFields.Single().HeightRequest);
        Assert.Contains($"HeightRequest=\"{expected}\"", XamlEmitter.EmitPage(model, "App"));
    }

    [Fact]
    public void More_lines_means_a_taller_box()
    {
        Assert.True(ControlVocabulary.Default.HeightForLines(13)
                    > ControlVocabulary.Default.HeightForLines(3));
    }

    [Fact]
    public void A_single_line_field_is_not_given_a_height()
    {
        var model = Page("Text", null);

        Assert.Null(model.AllFields.Single().HeightRequest);
        Assert.DoesNotContain("HeightRequest", XamlEmitter.EmitPage(model, "App"));
    }

    [Fact]
    public void A_paragraph_field_with_no_line_count_is_left_to_size_itself()
    {
        var model = Page("ParagraphText", null);

        Assert.Null(model.AllFields.Single().HeightRequest);
    }

    [Fact]
    public void The_line_height_is_configuration_rather_than_a_constant()
    {
        var tighter = new ControlVocabulary { TextLineHeight = 18, TextBoxPadding = 0 };

        Assert.Equal(54, tighter.HeightForLines(3));
        Assert.NotEqual(ControlVocabulary.Default.HeightForLines(3), tighter.HeightForLines(3));
    }
}
