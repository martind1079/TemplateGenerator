using System.Text.Json;
using FormWorks.Templates.Analysis;
using FormWorks.Templates.Model;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// Pass two emits routing from this walker, so a guard it gets wrong becomes a form
/// that sends agents to the wrong page. These fixtures mirror the shapes the estate
/// actually uses.
/// </summary>
public class RouteTableTests
{
    private static TemplateDocument WithScript(string lua, string page = "JobSheet")
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Form"] = new Dictionary<string, object>
            {
                ["fieldType"] = "Form",
                ["name"] = "Fixture",
                ["children"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["Page"] = new Dictionary<string, object>
                        {
                            ["fieldType"] = "Page",
                            ["elementName"] = page,
                            ["name"] = page,
                            ["children"] = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["Button"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Button",
                                        ["elementName"] = "Next",
                                        ["name"] = $"{page}.Next",
                                        ["scripts"] = new Dictionary<string, string> { ["OnTap"] = lua }
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
    public void An_unconditional_call_is_recorded_as_always()
    {
        var r = RouteTable.Build(WithScript("""form.changePage("Completion");"""));

        var route = Assert.Single(r.Routes);
        Assert.Equal("Completion", route.ToPage);
        Assert.Equal("(always)", route.Describe());
        Assert.Empty(route.When);
    }

    [Fact]
    public void A_two_way_split_gives_the_else_branch_the_negated_condition()
    {
        var r = RouteTable.Build(WithScript("""
            if string.sub(Reference.value, 1, 6) == "ASBHM1" then
                form.changePage("VehicleInformationPage");
            else
                form.changePage("BuildingInformationPage");
            end
            """));

        Assert.Equal(2, r.Routes.Count);

        var vehicle = r.Routes[0];
        Assert.Equal("VehicleInformationPage", vehicle.ToPage);
        Assert.Contains("ASBHM1", vehicle.Describe());

        // The else branch states nothing of its own, and its full guard is the negation.
        var building = r.Routes[1];
        Assert.Equal("(otherwise)", building.Describe());
        Assert.Contains("NOT", building.DescribeFull());
    }

    [Fact]
    public void An_elseif_chain_negates_every_earlier_branch()
    {
        var r = RouteTable.Build(WithScript("""
            if Outcome.value == "Refused" then
                form.changePage("Refused");
            elseif Outcome.value == "Deceased" then
                form.changePage("Deceased");
            elseif Outcome.value == "Gone Away" then
                form.changePage("GoneAway");
            end
            """));

        Assert.Equal(3, r.Routes.Count);

        // The compact view is the branch's own condition only, which is what pass two
        // emits, and it is correct only because order is preserved.
        Assert.Equal("""Outcome == "Gone Away" """.Trim(), r.Routes[2].Describe());

        // The full view carries the negations, so it holds in any order.
        var full = r.Routes[2].DescribeFull();
        Assert.Contains("""NOT Outcome == "Refused" """.Trim(), full);
        Assert.Contains("""NOT Outcome == "Deceased" """.Trim(), full);
    }

    [Fact]
    public void A_nested_branch_carries_both_conditions()
    {
        var r = RouteTable.Build(WithScript("""
            if Outcome.value == "Instruction Cancelled" then
                if string.sub(Reference.value, 1, 6) == "ASBHM1" then
                    form.changePage("VehicleInformationPage");
                else
                    form.changePage("BuildingInformationPage");
                end
            end
            """));

        Assert.Equal(2, r.Routes.Count);

        var vehicle = r.Routes[0];
        Assert.Equal(2, vehicle.Own.Count);
        Assert.Contains("Instruction Cancelled", vehicle.Describe());
        Assert.Contains("ASBHM1", vehicle.Describe());
    }

    [Fact]
    public void An_or_chain_of_outcomes_stays_a_disjunction()
    {
        var r = RouteTable.Build(WithScript("""
            if Outcome.value == "Property Abandoned" or Outcome.value == "Property Empty" then
                form.changePage("AbandonedEmpty");
            end
            """));

        var route = Assert.Single(r.Routes);
        Assert.Contains(" OR ", route.Describe());
        Assert.DoesNotContain(" AND ", route.Describe());
    }

    [Fact]
    public void Every_call_is_recovered_so_none_can_be_dropped_silently()
    {
        var r = RouteTable.Build(WithScript("""
            if A.value == "1" then
                form.changePage("One");
            elseif A.value == "2" then
                if B.value == "x" then
                    form.changePage("TwoX");
                else
                    form.changePage("Two");
                end
            else
                form.changePage("Other");
            end
            """));

        Assert.Equal(4, r.ChangePageCalls);
        Assert.Equal(4, r.Routes.Count);
        Assert.Empty(r.UnparsedHandlers);
    }

    [Fact]
    public void A_commented_out_call_is_ignored()
    {
        var r = RouteTable.Build(WithScript("""
            -- form.changePage("Ghost");
            form.changePage("Real");
            """));

        var route = Assert.Single(r.Routes);
        Assert.Equal("Real", route.ToPage);
    }
}
