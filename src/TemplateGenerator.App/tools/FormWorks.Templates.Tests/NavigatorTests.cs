using System.Text.Json;
using FormWorks.Templates.Analysis;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// The navigator is the routing table as typed C#. Order is load bearing, the prefix rules
/// are the easiest thing in the conversion to lose, and a guard the emitter cannot express
/// must be visible rather than quietly wrong.
/// </summary>
public class NavigatorTests
{
    private static (string Code, IReadOnlyList<UnroutableGuard> Unroutable) Emit(
        string lua, string? owner = null)
    {
        // The handler can belong to a field rather than to the button, which is how a
        // template routes from an answer changing.
        object Handler(string type, string name, int width) => new Dictionary<string, object>
        {
            [type] = new Dictionary<string, object>
            {
                ["fieldType"] = type,
                ["elementName"] = name,
                ["name"] = name,
                ["title"] = name,
                ["width"] = width,
                ["scripts"] = new Dictionary<string, string> { ["OnValueChange"] = lua }
            }
        };

        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Form"] = new Dictionary<string, object>
            {
                ["fieldType"] = "Form",
                ["width"] = 980,
                ["children"] = new object[]
                {
                    Page("JobSheet", new object[]
                    {
                        Field("Text", "Reference", 940),
                        owner == "Outcome" ? Handler("Text", "Outcome", 940) : Field("Text", "Outcome", 940),
                        new Dictionary<string, object>
                        {
                            ["Button"] = new Dictionary<string, object>
                            {
                                ["fieldType"] = "Button", ["elementName"] = "NextButton",
                                ["name"] = "JobSheet.NextButton", ["title"] = "Next", ["width"] = 90,
                                ["scripts"] = new Dictionary<string, string>
                                {
                                    ["OnTap"] = owner is null ? lua : "form.changePage(\"Completion\");"
                                }
                            }
                        }
                    }),
                    Page("Refused", [Field("Text", "Why", 940)]),
                    Page("Completion", [Field("Text", "Done", 940)])
                }
            }
        });

        var doc = TemplateLoader.LoadJson(json, "Fixture V1");
        var pages = doc.Pages
            .Select(p => PageEmitModelBuilder.Build(doc, p, ControlVocabulary.Default, ""))
            .ToList();

        var result = NavigatorEmitter.Emit(
            doc, pages, RouteTable.Build(doc), "App", "Nav", "Report");

        return (result.Code, result.Unroutable);
    }

    private static object Page(string name, object[] children) => new Dictionary<string, object>
    {
        ["Page"] = new Dictionary<string, object>
        {
            ["fieldType"] = "Page",
            ["elementName"] = name,
            ["name"] = name,
            ["title"] = name,
            ["width"] = 980,
            ["children"] = children
        }
    };

    private static object Field(string type, string name, int width) => new Dictionary<string, object>
    {
        [type] = new Dictionary<string, object>
        {
            ["fieldType"] = type,
            ["elementName"] = name,
            ["name"] = name,
            ["title"] = name,
            ["width"] = width
        }
    };

    [Fact]
    public void A_value_comparison_becomes_a_typed_path_on_the_report()
    {
        var (code, _) = Emit("""
            if Outcome.value == "Refused" then form.changePage("Refused"); end
            """);

        Assert.Contains("string.Equals(report.JobSheet.Outcome, \"Refused\", StringComparison.Ordinal)", code);
        Assert.Contains("return \"//refusedPage\";", code);
    }

    [Fact]
    public void A_prefix_rule_keeps_its_slice_bounds()
    {
        // Lua's string.sub is one-based and inclusive, so 1 to 6 is six characters. An
        // out-by-one here routes a whole client's cases to the wrong page.
        var (code, _) = Emit("""
            if string.sub(Reference.value, 1, 6) == "ASBHM1" then form.changePage("Refused"); end
            """);

        Assert.Contains("Slice(report.JobSheet.Reference, 1, 6, \"ASBHM1\")", code);
    }

    [Fact]
    public void A_negated_prefix_rule_is_inverted_rather_than_dropped()
    {
        var (code, _) = Emit("""
            if string.sub(Reference.value, 1, 4) ~= "MSUK" then form.changePage("Refused"); end
            """);

        Assert.Contains("!Slice(report.JobSheet.Reference, 1, 4, \"MSUK\")", code);
    }

    [Fact]
    public void An_or_chain_stays_a_disjunction()
    {
        var (code, _) = Emit("""
            if Outcome.value == "A" or Outcome.value == "B" then form.changePage("Refused"); end
            """);

        // The condition line only: the Slice helper further down legitimately uses &&.
        var condition = code.Split('\n').Single(l => l.TrimStart().StartsWith("if (", StringComparison.Ordinal));

        Assert.Contains(" || ", condition);
        Assert.DoesNotContain(" && ", condition);
    }

    [Fact]
    public void Branches_keep_their_order_and_return_early()
    {
        var (code, _) = Emit("""
            if Outcome.value == "A" then
                form.changePage("Refused");
            elseif Outcome.value == "B" then
                form.changePage("Completion");
            end
            """);

        var first = code.IndexOf("//refusedPage", StringComparison.Ordinal);
        var second = code.IndexOf("//completionPage", StringComparison.Ordinal);

        Assert.True(first > 0 && second > first, "routes must be emitted in template order");
    }

    [Fact]
    public void Routes_are_absolute_so_a_page_is_replaced_rather_than_stacked()
    {
        // A form moves between pages of one report. A relative route pushes, which would
        // grow a back stack sixteen deep and put a back chevron on a form.
        var (code, _) = Emit("""form.changePage("Completion");""");

        Assert.Contains("return \"//completionPage\";", code);
    }

    [Fact]
    public void The_step_path_follows_the_routing_rather_than_listing_every_page()
    {
        var (code, _) = Emit("""
            if Outcome.value == "A" then form.changePage("Refused"); end
            """);

        Assert.Contains("public const string EntryRoute", code);
        Assert.Contains("IReadOnlyList<(string Title, string Route)> Path(", code);
        Assert.Contains("route = Forward(report, route);", code);
    }

    [Fact]
    public void An_unconditional_route_ends_the_method()
    {
        var (code, _) = Emit("""form.changePage("Completion");""");

        Assert.Contains("return \"//completionPage\";", code);
    }

    [Fact]
    public void A_conditional_chain_falls_through_to_nowhere()
    {
        // Going nowhere is what the template does when the question routing turns on has
        // not been answered. Inventing a destination would not be.
        var (code, _) = Emit("""
            if Outcome.value == "A" then form.changePage("Refused"); end
            """);

        Assert.Contains("return null;", code);
    }

    [Fact]
    public void Lua_this_resolves_to_the_field_whose_handler_it_is()
    {
        // "this" is the self-reference, not a name to look up. The handler below belongs
        // to the Outcome field, so this.value is that field.
        var (code, unroutable) = Emit("""
            if this.value == "Refused" then form.changePage("Refused"); end
            """, owner: "Outcome");

        Assert.Empty(unroutable);
        Assert.Contains("string.Equals(report.JobSheet.Outcome, \"Refused\", StringComparison.Ordinal)", code);
    }

    [Fact]
    public void This_on_a_control_that_holds_no_answer_is_reported()
    {
        // A button has no value, so there is nothing for "this" to mean.
        var (_, unroutable) = Emit("""
            if this.value == "x" then form.changePage("Refused"); end
            """);

        Assert.Contains(unroutable, u => u.Detail.Contains("holds no answer"));
    }

    [Fact]
    public void A_guard_naming_a_field_that_does_not_exist_is_reported_not_guessed()
    {
        var (code, unroutable) = Emit("""
            if Nonexistent.value == "A" then form.changePage("Refused"); end
            """);

        Assert.Contains(unroutable, u => u.Detail.Contains("Nonexistent"));
        Assert.Contains("false /* unroutable */", code);
    }

    [Fact]
    public void Methods_are_qualified_by_page_because_leaf_names_collide()
    {
        // Nine of One example template's sixteen pages call their button "NextButton".
        Assert.Equal("JobSheetNextNextButton", NavigatorEmitter.MethodName("JobSheet.Next.NextButton"));
        Assert.NotEqual(
            NavigatorEmitter.MethodName("JobSheet.Next.NextButton"),
            NavigatorEmitter.MethodName("Refused.Next.NextButton"));
    }

    [Fact]
    public void Methods_are_also_qualified_within_a_page_because_a_page_can_have_several_next_buttons()
    {
        // One page, a Next per branch: page name alone is not enough to tell them apart.
        Assert.NotEqual(
            NavigatorEmitter.MethodName("InterviewPart1.UnemploymentSection.Next"),
            NavigatorEmitter.MethodName("InterviewPart1.RedundancySection.Next"));
    }
}
