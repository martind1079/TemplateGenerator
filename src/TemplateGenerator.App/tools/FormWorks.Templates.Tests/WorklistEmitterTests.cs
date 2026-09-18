using System.Text.Json;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// The whole point of numbering is that Remaining.md and a worklist CSV, two different
/// renderings, point at the same field when they print the same number. These cover that
/// the numbering is shared rather than each rendering counting on its own, and that it is
/// stable rather than depending on discovery order.
/// </summary>
public class WorklistEmitterTests
{
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
                            ["fieldType"] = "Page", ["elementName"] = "P", ["name"] = "P",
                            ["title"] = "Page",
                            ["children"] = new object[]
                            {
                                // A validation rule the generator refuses: the decision
                                // depends on a running total, not a comparison.
                                new Dictionary<string, object>
                                {
                                    ["Text"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Text", ["elementName"] = "Reference",
                                        ["name"] = "P.Reference", ["title"] = "Reference",
                                        ["scripts"] = new Dictionary<string, string>
                                        {
                                            ["OnValidate"] =
                                                "local n = 0;\n" +
                                                "if Other.value == \"Yes\" then n = n + 1; end\n" +
                                                "if n < 1 then this.valid = false; this.message = \"Not enough\"; end"
                                        }
                                    }
                                },
                                new Dictionary<string, object>
                                {
                                    ["Text"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Text", ["elementName"] = "Other",
                                        ["name"] = "P.Other", ["title"] = "Other"
                                    }
                                },

                                // A field two different handlers write visibility for, so
                                // the generator cannot say which one wins.
                                new Dictionary<string, object>
                                {
                                    ["Text"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Text", ["elementName"] = "DriverA",
                                        ["name"] = "P.DriverA", ["title"] = "Driver A",
                                        ["scripts"] = new Dictionary<string, string>
                                        {
                                            ["OnValueChange"] =
                                                "Target.visible = false;\nif this.value == \"Yes\" then Target.visible = true; end"
                                        }
                                    }
                                },
                                new Dictionary<string, object>
                                {
                                    ["Text"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Text", ["elementName"] = "DriverB",
                                        ["name"] = "P.DriverB", ["title"] = "Driver B",
                                        ["scripts"] = new Dictionary<string, string>
                                        {
                                            ["OnValueChange"] =
                                                "Target.visible = false;\nif this.value == \"No\" then Target.visible = true; end"
                                        }
                                    }
                                },
                                new Dictionary<string, object>
                                {
                                    ["Text"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Text", ["elementName"] = "Target",
                                        ["name"] = "P.Target", ["title"] = "Target", ["hidden"] = true
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        return TemplateLoader.LoadJson(json, "Fixture V1");
    }

    [Fact]
    public void The_worklist_finds_both_a_refused_rule_and_a_multi_source_field()
    {
        var doc = Document();
        var analysis = TemplateConverter.Analyze(doc, HouseStyle.Default("App"));
        var items = WorklistEmitter.Build(analysis);

        Assert.Contains(items, i => i.Category == WorklistCategory.Validation && i.Field == "P.Reference");
        Assert.Contains(items, i => i.Category == WorklistCategory.State && i.Field == "P.Target");
    }

    [Fact]
    public void Numbering_is_stable_across_repeated_builds_of_the_same_document()
    {
        var doc = Document();
        var analysis = TemplateConverter.Analyze(doc, HouseStyle.Default("App"));

        var first = WorklistEmitter.Build(analysis);
        var second = WorklistEmitter.Build(analysis);

        Assert.Equal(
            first.Select(i => (i.Id, i.Category, i.Field)),
            second.Select(i => (i.Id, i.Category, i.Field)));
    }

    [Fact]
    public void Remaining_md_and_the_csv_number_the_same_field_the_same_way()
    {
        var doc = Document();
        var analysis = TemplateConverter.Analyze(doc, HouseStyle.Default("App"));

        var remaining = RemainingWorkEmitter.Emit(
            doc, analysis.Validation, analysis.Validator, analysis.State, analysis.Computed, analysis.Models);

        var items = WorklistEmitter.Build(analysis);
        var validationItem = Assert.Single(items, i => i.Category == WorklistCategory.Validation);
        var stateItem = Assert.Single(items, i => i.Category == WorklistCategory.State);

        Assert.Contains($"### {validationItem.Id}. P.Reference", remaining);

        var csv = WorklistEmitter.EmitCsv(items, doc.FolderName);
        Assert.Contains($"{validationItem.Id},\"Fixture V1\"", csv);
        Assert.Contains($"{stateItem.Id},\"Fixture V1\"", csv);
    }

    [Fact]
    public void A_comma_in_a_field_does_not_shift_csv_columns()
    {
        var doc = Document();
        var analysis = TemplateConverter.Analyze(doc, HouseStyle.Default("App"));
        var items = WorklistEmitter.Build(analysis);

        var csv = WorklistEmitter.EmitCsv(items, "A, Template");

        // The comma stays inside its cell's quotes rather than being read as a column
        // break, which is what makes this valid CSV rather than a coincidentally similar
        // string. A raw comma count would see one column too many on this row and be none
        // the wiser about why.
        Assert.Contains("\"A, Template\"", csv);
        Assert.Contains("\"A, Template\",\"Validation rule\"", csv);
    }
}
