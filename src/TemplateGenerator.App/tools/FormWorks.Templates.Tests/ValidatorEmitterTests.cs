using System.Text.Json;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// A rule's condition can parse cleanly and still be something the generator must not
/// honour. These cover that case rather than the more common "the structure did not come
/// apart at all" one.
/// </summary>
public class ValidatorEmitterTests
{
    /// <summary>
    /// Two pages: Interview.Other is required only while BuildingInformationPage is shown
    /// - a page reading another *page's* own visible state, which is the real shape found
    /// converting Buy To Let Audio V14. Page visibility is deliberately not tracked by the
    /// generated Visibility tables (it is recovered as routing instead), so honouring this
    /// guard literally would make IsShown answer "shown" for a name it has never heard of
    /// and the field would end up required on every path, including ones that never show
    /// BuildingInformationPage at all.
    /// </summary>
    private static FormWorks.Templates.Model.TemplateDocument Document()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Form"] = new Dictionary<string, object>
            {
                ["fieldType"] = "Form",
                ["children"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["Page"] = new Dictionary<string, object>
                        {
                            ["fieldType"] = "Page", ["elementName"] = "Interview", ["name"] = "Interview",
                            ["title"] = "Interview",
                            ["children"] = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["Text"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Text", ["elementName"] = "Other",
                                        ["name"] = "Interview.Other", ["title"] = "Other",
                                        ["scripts"] = new Dictionary<string, string>
                                        {
                                            ["OnValidate"] =
                                                "if this.value == \"\" and BuildingInformationPage.visible == true then\n"
                                                + "  this.valid = false;\n  this.message = \"Required\";\nend"
                                        }
                                    }
                                }
                            }
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["Page"] = new Dictionary<string, object>
                        {
                            ["fieldType"] = "Page", ["elementName"] = "BuildingInformationPage",
                            ["name"] = "BuildingInformationPage", ["title"] = "Building Information",
                            ["children"] = Array.Empty<object>()
                        }
                    }
                }
            }
        });

        return TemplateLoader.LoadJson(json, "Fixture V1");
    }

    [Fact]
    public void A_rule_reading_a_pages_own_visible_state_is_refused_rather_than_always_true()
    {
        var doc = Document();
        var analysis = TemplateConverter.Analyze(doc, HouseStyle.Default("App"));

        Assert.Contains("Interview.Other", analysis.Validator.RefusedFields);
        Assert.DoesNotContain("answers.OtherError =", analysis.Validator.Code);
        Assert.DoesNotContain("IsShown(report, \"BuildingInformationPage\")", analysis.Validator.Code);
    }

    [Fact]
    public void Remaining_md_lists_it_even_though_its_condition_parsed_cleanly()
    {
        var doc = Document();
        var analysis = TemplateConverter.Analyze(doc, HouseStyle.Default("App"));

        var remaining = RemainingWorkEmitter.Emit(
            doc, analysis.Validation, analysis.Validator, analysis.State, analysis.Computed, analysis.Models);

        Assert.Contains("Interview.Other", remaining);
    }
}
