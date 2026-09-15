using System.Text.Json;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// A row stays aligned when its fields disagree about captions only if the ones without
/// a caption above still reserve the space. A checkbox wears its caption on the right,
/// so without it the tick floats to the top of a row of headed fields.
/// </summary>
public class TitleSpaceTests
{
    private static string Xaml(string type, string title, bool assignSpace)
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
                                                    ["fieldType"] = type, ["elementName"] = "F", ["name"] = "F",
                                                    ["title"] = title, ["width"] = 940,
                                                    ["assignSpaceForTitle"] = assignSpace
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
        return XamlEmitter.EmitPage(
            PageEmitModelBuilder.Build(doc, doc.Pages.Single(), ControlVocabulary.Default, ""), "App");
    }

    private static int Spacers(string xaml)
        => xaml.Split("<Label Text=\" \"").Length - 1;

    [Fact]
    public void A_checkbox_that_asks_for_title_space_gets_it()
    {
        // Sec and Corr in the contact summary, which sit in a row beside dated fields.
        Assert.Equal(1, Spacers(Xaml("Checkbox", "Sec", assignSpace: true)));
    }

    [Fact]
    public void A_checkbox_that_does_not_ask_for_it_stays_at_the_top()
    {
        // The identity evidence block, where every field is a tick against a statement.
        Assert.Equal(0, Spacers(Xaml("Checkbox", "Passport", assignSpace: false)));
    }

    [Fact]
    public void A_captionless_field_that_asks_for_the_space_gets_it()
    {
        // Rows two to five of the contact summary, whose headings are on row one.
        Assert.Equal(1, Spacers(Xaml("Date", "", assignSpace: true)));
    }

    [Fact]
    public void A_captionless_field_that_does_not_ask_gets_nothing()
    {
        Assert.Equal(0, Spacers(Xaml("Date", "", assignSpace: false)));
    }

    [Fact]
    public void A_field_with_a_caption_above_needs_no_spacer()
    {
        var xaml = Xaml("Date", "Date", assignSpace: true);

        Assert.Equal(0, Spacers(xaml));
        Assert.Contains("Text=\"Date\"", xaml);
    }

    [Fact]
    public void The_spacer_uses_the_caption_style_so_it_cannot_drift_out_of_step()
    {
        var xaml = Xaml("Checkbox", "Sec", assignSpace: true);

        Assert.Contains("<Label Text=\" \" Style=\"{StaticResource FormFieldCaption}\" />", xaml);
    }
}
