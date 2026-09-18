using System.Text.Json;
using FormWorks.Templates.Analysis;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// FormWorks writes validation as a handler per field setting this.valid and this.message
/// under some condition. The condition is what matters and it is stated nowhere else.
/// </summary>
public class ValidationTableTests
{
    private static ValidationTableReport Build(string lua)
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
                                    ["Text"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Text", ["elementName"] = "Reference",
                                        ["name"] = "JobSheet.Reference", ["title"] = "Our Reference",
                                        ["width"] = 940,
                                        ["scripts"] = new Dictionary<string, string> { ["OnValidate"] = lua }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        return ValidationTable.Build(TemplateLoader.LoadJson(json, "F V1"));
    }

    [Fact]
    public void A_rule_carries_its_condition_and_its_message()
    {
        var report = Build("""
            if this.value == "" then
                this.valid = false; this.message = "Please supply a reference";
            end
            """);

        var rule = Assert.Single(report.Rules);
        Assert.Equal("JobSheet.Reference", rule.Field);
        Assert.Equal("Please supply a reference", rule.Message);
        Assert.True(rule.FullyParsed);
    }

    [Fact]
    public void A_rule_written_onto_another_field_is_reported_not_ignored()
    {
        // Every one of the estate's 4,426 validity writes is to the handler's own field.
        // If a template starts doing otherwise, the rule belongs to a field this walker is
        // not looking at, so it must be visible rather than dropped.
        var report = Build("""
            if this.value == "" then
                Other.valid = false; Other.message = "Required";
            end
            """);

        Assert.NotEmpty(report.Unrecovered);
        Assert.Contains(report.Unrecovered, u => u.Contains("on another field"));
    }

    [Fact]
    public void A_handler_setting_only_its_own_validity_is_not_reported()
    {
        var report = Build("""
            if this.value == "" then this.valid = false; this.message = "Required"; end
            """);

        Assert.Empty(report.Unrecovered);
    }

    [Fact]
    public void Reading_whether_a_field_is_shown_is_part_of_the_condition()
    {
        // A field is only required while it is visible. Mistaking this read for a write
        // makes validation look entangled with visibility when the two barely overlap.
        var report = Build("""
            if this.value == "" and IDV.visible == true then
                this.valid = false; this.message = "Required";
            end
            """);

        var atoms = Assert.Single(report.Rules).When.SelectMany(c => c.Atoms).ToList();

        Assert.Contains(atoms, a => a.IsStateTest && a.Field == "IDV");
        Assert.True(report.Expressible.Any());
    }

    [Fact]
    public void A_checkbox_is_tested_against_a_boolean_not_a_string()
    {
        var report = Build("""
            if this.value == false then this.valid = false; this.message = "Tick it"; end
            """);

        var atom = Assert.Single(Assert.Single(report.Rules).When.SelectMany(c => c.Atoms));

        Assert.True(atom.IsLiteralBoolean);
        Assert.Equal("false", atom.Literal);
    }

    [Fact]
    public void Setting_a_field_valid_is_not_a_rule()
    {
        // Valid is the default state. Only what makes a field invalid is a rule.
        var report = Build("""
            if this.value == "" then this.valid = false; this.message = "Required";
            else this.valid = true; end
            """);

        Assert.Single(report.Rules);
    }

    [Fact]
    public void Each_branch_of_a_chain_is_its_own_rule_with_its_own_message()
    {
        var report = Build("""
            if this.value == "" then
                this.valid = false; this.message = "Required";
            elseif this.value == "x" then
                this.valid = false; this.message = "Not x";
            end
            """);

        Assert.Equal(2, report.Rules.Count);
        Assert.Equal(["Required", "Not x"], report.Rules.Select(r => r.Message));
    }

    [Fact]
    public void A_computed_message_keeps_its_expression_rather_than_inventing_text()
    {
        var report = Build("""
            if this.value == "" then this.valid = false; this.message = this.title; end
            """);

        var rule = Assert.Single(report.Rules);

        Assert.Null(rule.Message);
        Assert.Equal("this.title", rule.MessageExpression);
    }

    [Fact]
    public void Reading_a_field_that_does_not_resolve_is_refused_rather_than_treated_as_shown()
    {
        // IDV names nothing in this fixture. IsShown answers "shown" for any name it does
        // not recognise - right for a field nothing ever hides, wrong for a name that was
        // never a field to begin with - so emitting the call anyway would turn "only
        // required while IDV is shown" into "always required" and never say so.
        var result = ResultFor("""
            if this.value == "" and IDV.visible == true then
                this.valid = false; this.message = "Required";
            end
            """);

        Assert.Contains("JobSheet.Reference", result.RefusedFields);
        Assert.DoesNotContain("IsShown(report, \"IDV\")", result.Code);
    }

    [Fact]
    public void Reading_a_field_that_does_resolve_becomes_a_named_seam()
    {
        // Unlike IDV above, Other is a real field in this fixture, so the guard is honoured.
        var result = ResultFor("""
            if this.value == "" and Other.visible == true then
                this.valid = false; this.message = "Required";
            end
            """, ("Other", "JobSheet.Other"));

        Assert.DoesNotContain("JobSheet.Reference", result.RefusedFields);
        Assert.Contains("IsShown(report, \"JobSheet.Other\")", result.Code);
        Assert.Contains("private static bool IsShown(", result.Code);
    }

    [Fact]
    public void Rules_on_one_field_keep_their_order_so_the_first_match_wins()
    {
        var code = EmitFor("""
            if this.value == "" then this.valid = false; this.message = "Required";
            elseif this.value == "x" then this.valid = false; this.message = "Not x";
            end
            """);

        var first = code.IndexOf("Required", StringComparison.Ordinal);
        var second = code.IndexOf("Not x", StringComparison.Ordinal);

        Assert.True(first > 0 && second > first, "rules must be emitted in template order");
    }

    [Fact]
    public void An_empty_comparison_asks_whether_the_field_was_answered()
    {
        var code = EmitFor("""
            if this.value == "" then this.valid = false; this.message = "Required"; end
            """);

        // Rooted at the report rather than the page's answers, because the same renderer
        // serves visibility, which has no page to be local to.
        Assert.Contains("string.IsNullOrEmpty(report.JobSheet.Reference)", code);
    }

    [Fact]
    public void A_message_of_this_title_becomes_the_fields_caption()
    {
        // 355 messages across the estate are this.title, meaning the field's own caption.
        // That is known at generation time, so emitting the words "this.title" at an agent
        // would be a straight bug.
        var code = EmitFor("""
            if this.value == "" then this.valid = false; this.message = this.title; end
            """);

        Assert.Contains("\"Our Reference\"", code);
        Assert.DoesNotContain("this.title", code);
    }

    [Fact]
    public void A_message_built_from_a_local_falls_back_to_the_caption_and_is_reported()
    {
        var result = ResultFor("""
            local strMessage = "x"
            if this.value == "" then this.valid = false; this.message = strMessage; end
            """);

        Assert.Contains("\"Our Reference\"", result.Code);
        Assert.Contains(result.Wording, w => w.Expression.Contains("strMessage"));
    }

    private static string EmitFor(string lua) => ResultFor(lua).Code;

    private static Emit.ValidatorResult ResultFor(string lua, params (string ElementName, string Name)[] extraFields)
    {
        var children = new List<object>
        {
            new Dictionary<string, object>
            {
                ["Text"] = new Dictionary<string, object>
                {
                    ["fieldType"] = "Text", ["elementName"] = "Reference",
                    ["name"] = "JobSheet.Reference", ["title"] = "Our Reference",
                    ["width"] = 940,
                    ["scripts"] = new Dictionary<string, string> { ["OnValidate"] = lua }
                }
            }
        };

        foreach (var (elementName, name) in extraFields)
        {
            children.Add(new Dictionary<string, object>
            {
                ["Text"] = new Dictionary<string, object>
                {
                    ["fieldType"] = "Text", ["elementName"] = elementName,
                    ["name"] = name, ["title"] = elementName, ["width"] = 940
                }
            });
        }

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
                            ["children"] = children.ToArray()
                        }
                    }
                }
            }
        });

        var doc = TemplateLoader.LoadJson(json, "F V1");
        var pages = doc.Pages
            .Select(p => Emit.PageEmitModelBuilder.Build(doc, p, Emit.ControlVocabulary.Default, ""))
            .ToList();

        return Emit.ValidatorEmitter.Emit(
            doc, pages, ValidationTable.Build(doc), "App", "Validator", "Report");
    }

    [Fact]
    public void A_condition_built_from_locals_is_not_claimed_as_expressible()
    {
        var report = Build("""
            local total = tonumber(this.value)
            if total <= 0 then this.valid = false; this.message = "Too small"; end
            """);

        Assert.Single(report.Rules);
        Assert.Empty(report.Expressible);
    }
}
