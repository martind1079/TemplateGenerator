using System.Text.Json;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// The fully qualified FormWorks name is the contract with the portal, because the CMS
/// exports against it. It is not derivable from the generated property, so the generator
/// writes the pairing down and both directions read the same list.
/// </summary>
public class FieldExchangeTests
{
    private static string Report(params (string Type, string Name)[] fields)
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
                            ["fieldType"] = "Page", ["elementName"] = "JobSheet", ["name"] = "JobSheet",
                            ["title"] = "Job Sheet", ["width"] = 980,
                            ["children"] = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["Section"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Section", ["elementName"] = "JobSheet",
                                        ["name"] = "JobSheet.JobSheet", ["title"] = "Job Sheet", ["width"] = 980,
                                        ["children"] = fields.Select(f => (object)new Dictionary<string, object>
                                        {
                                            [f.Type] = new Dictionary<string, object>
                                            {
                                                ["fieldType"] = f.Type, ["elementName"] = f.Name,
                                                ["name"] = $"JobSheet.JobSheet.{f.Name}",
                                                ["title"] = f.Name, ["width"] = 940
                                            }
                                        }).ToArray()
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        var doc = TemplateLoader.LoadJson(json, "F V1");
        var pages = doc.Pages
            .Select(p => PageEmitModelBuilder.Build(doc, p, ControlVocabulary.Default, ""))
            .ToList();

        return ReportEmitter.Emit(doc, pages, "App", "Report", ControlVocabulary.Default);
    }

    [Fact]
    public void A_field_is_keyed_by_its_fully_qualified_name_not_its_property_name()
    {
        var code = Report(("Text", "Reference"));

        Assert.Contains("case \"JobSheet.JobSheet.Reference\":", code);
        Assert.Contains("JobSheet.Reference = value ?? string.Empty;", code);
    }

    [Fact]
    public void Both_directions_come_from_the_same_list()
    {
        var code = Report(("Text", "Reference"));

        Assert.Contains("case \"JobSheet.JobSheet.Reference\":", code);
        Assert.Contains("[\"JobSheet.JobSheet.Reference\"] = JobSheet.Reference,", code);
    }

    [Fact]
    public void A_date_is_parsed_with_explicit_formats_and_an_invariant_culture()
    {
        // A locale-sensitive parse turns the third of April into the fourth of March
        // without complaining, which is worse than refusing.
        var code = Report(("Date", "DateInstructed"));

        Assert.Contains("CultureInfo.InvariantCulture", code);
        Assert.Contains("DateTime.TryParseExact", code);
        Assert.DoesNotContain("DateTime.Parse(", code);
    }

    [Fact]
    public void A_value_that_will_not_convert_is_reported_rather_than_dropped()
    {
        var code = Report(("Date", "DateInstructed"), ("Checkbox", "Passport"));

        Assert.Contains("problems.Add(Unreadable(field, value, \"a date\"))", code);
        Assert.Contains("problems.Add(Unreadable(field, value, \"a yes or no value\"))", code);
    }

    [Fact]
    public void A_key_naming_no_field_is_reported_rather_than_ignored()
    {
        var code = Report(("Text", "Reference"));

        Assert.Contains("FieldProblemKind.UnknownField", code);
        Assert.Contains("no field of this name on this template", code);
    }

    [Fact]
    public void Switch_locals_are_unique_so_the_block_compiles()
    {
        // C# scopes a switch's locals to the whole block, so two date fields sharing a
        // variable name would not compile.
        var code = Report(("Date", "First"), ("Date", "Second"));

        Assert.Contains("out var dJobSheetFirst", code);
        Assert.Contains("out var dJobSheetSecond", code);
    }

    [Fact]
    public void An_unanswered_date_leaves_as_nothing_rather_than_today()
    {
        // MAUI's DatePicker has no empty state, so the model carries the nullability and a
        // house control supplies the empty state. Without it an untouched field reaches the
        // portal as today's date, as though the agent had entered it.
        var code = Report(("Date", "DateInstructed"), ("Time", "Time1"));

        Assert.Contains("public DateTime? DateInstructed", ModelAnswers(("Date", "DateInstructed")));
        Assert.Contains("JobSheet.DateInstructed?.ToString(", code);
        Assert.Contains("JobSheet.Time1?.ToString(", code);
    }

    [Fact]
    public void An_empty_value_means_unanswered_rather_than_unreadable()
    {
        var code = Report(("Date", "DateInstructed"));

        Assert.Contains("if (string.IsNullOrWhiteSpace(value)) JobSheet.DateInstructed = null;", code);
    }

    private static string ModelAnswers(params (string Type, string Name)[] fields)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
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
                            ["fieldType"] = "Page", ["elementName"] = "JobSheet", ["name"] = "JobSheet",
                            ["title"] = "Job Sheet", ["width"] = 980,
                            ["children"] = fields.Select(f => (object)new Dictionary<string, object>
                            {
                                [f.Type] = new Dictionary<string, object>
                                {
                                    ["fieldType"] = f.Type, ["elementName"] = f.Name,
                                    ["name"] = $"JobSheet.{f.Name}", ["title"] = f.Name, ["width"] = 940
                                }
                            }).ToArray()
                        }
                    }
                }
            }
        });

        var doc = TemplateLoader.LoadJson(json, "F V1");
        return ModelEmitter.EmitAnswers(
            PageEmitModelBuilder.Build(doc, doc.Pages.Single(), ControlVocabulary.Default, ""), "App");
    }

    [Fact]
    public void The_accepted_formats_are_configuration()
    {
        var doc = TemplateLoader.LoadJson(
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["Form"] = new Dictionary<string, object>
                {
                    ["fieldType"] = "Form", ["width"] = 980, ["children"] = Array.Empty<object>()
                }
            }), "F V1");

        var code = ReportEmitter.Emit(
            doc, [], "App", "Report",
            new ControlVocabulary { DateInputFormats = ["dd-MMM-yyyy"] });

        Assert.Contains("\"dd-MMM-yyyy\"", code);
    }
}
