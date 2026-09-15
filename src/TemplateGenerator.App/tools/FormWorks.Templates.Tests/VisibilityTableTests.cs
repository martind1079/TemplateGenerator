using System.Text.Json;
using FormWorks.Templates.Analysis;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Model;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// Visibility is imperative in the source and a predicate in the output, and those are not
/// the same thing. These cover where they agree and where they do not, because a field
/// wrongly reconstructed is invisible in exactly the way a missing field is: the page
/// looks fine and a question is never asked.
/// </summary>
public class VisibilityTableTests
{
    private static VisibilityTableReport Build(params (string Field, string Event, string Script)[] handlers)
    {
        var children = handlers.Select(h => (object)new Dictionary<string, object>
        {
            ["Text"] = new Dictionary<string, object>
            {
                ["fieldType"] = "Text",
                ["elementName"] = h.Field,
                ["name"] = h.Field,
                ["title"] = h.Field,
                ["scripts"] = new Dictionary<string, string> { [h.Event] = h.Script }
            }
        }).ToList();

        // The fields written about, so they exist to be looked up.
        foreach (var target in new[] { "Target", "Other" })
        {
            children.Add(new Dictionary<string, object>
            {
                ["Text"] = new Dictionary<string, object>
                {
                    ["fieldType"] = "Text", ["elementName"] = target, ["name"] = target,
                    ["title"] = target, ["hidden"] = true
                }
            });
        }

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
                            ["title"] = "Page", ["children"] = children.ToArray()
                        }
                    }
                }
            }
        });

        return VisibilityTable.Build(TemplateLoader.LoadJson(json, "Fixture V1"));
    }

    [Fact]
    public void Hide_then_show_under_a_guard_inverts_to_the_guard()
    {
        // The estate's dominant idiom, and the one that inverts exactly.
        var report = Build(("Driver", "OnValueChange", """
            Target.visible = false;
            if this.value == "Yes" then
                Target.visible = true;
            end
            """));

        var target = Assert.Single(report.Visibility);
        Assert.Equal("Target", target.Target.Label);
        Assert.Equal(Inversion.ResetThenShow, target.Inversion);
        Assert.Equal(StateShape.SingleSource, target.Shape);
    }

    [Fact]
    public void A_field_only_ever_shown_is_not_a_predicate()
    {
        // Nothing hides it again, so once shown it stays shown. A predicate that goes back
        // to false where the guard stops holding is a different form, so this is a person's
        // call rather than the generator's.
        var report = Build(("Driver", "OnValueChange", """
            if this.value == "Yes" then
                Target.visible = true;
            end
            """));

        Assert.Equal(Inversion.ShowOnly, Assert.Single(report.Visibility).Inversion);
    }

    [Fact]
    public void Two_handlers_writing_one_field_are_flagged_as_ordered()
    {
        // Which one ran last depends on which field the agent touched last, and that is
        // not recoverable from the template.
        var report = Build(
            ("DriverA", "OnValueChange", "Target.visible = false;\nif this.value == \"Yes\" then Target.visible = true; end"),
            ("DriverB", "OnValueChange", "Target.visible = false;\nif this.value == \"No\" then Target.visible = true; end"));

        var target = Assert.Single(report.Visibility);
        Assert.Equal(StateShape.MultiSource, target.Shape);
        Assert.Equal(2, target.Sources.Count());
    }

    [Fact]
    public void A_guard_that_cannot_be_expressed_refuses_the_whole_field()
    {
        // One unreadable guard makes the field's state unknown, not partly known. Emitting
        // the readable half would show a field in cases the template hides it.
        var report = Build(("Driver", "OnValueChange", """
            Target.visible = false;
            if someLocal > compute(this.value) then
                Target.visible = true;
            end
            """));

        var target = Assert.Single(report.Visibility);
        Assert.Equal(StateShape.Refused, target.Shape);
        Assert.Equal(Inversion.None, target.Inversion);
    }

    [Fact]
    public void Enablement_is_tracked_separately_from_visibility()
    {
        // A field can be shown and not usable, so they are different properties of the
        // same field rather than one state.
        var report = Build(("Driver", "OnValueChange", """
            Target.enabled = false;
            if this.value == "Yes" then
                Target.enabled = true;
            end
            """));

        Assert.Empty(report.Visibility);
        Assert.Equal("enabled", Assert.Single(report.Enablement).Property);
    }

    [Fact]
    public void A_handler_writing_its_own_state_is_attributed_to_itself()
    {
        var report = Build(("Driver", "OnValueChange", """
            this.visible = false;
            if Other.value == "Yes" then
                this.visible = true;
            end
            """));

        Assert.Equal("Driver", Assert.Single(report.Visibility).Target.Label);
    }
}

/// <summary>
/// The rendered predicate, as opposed to the table behind it.
/// </summary>
public class VisibilityEmitterTests
{
    [Fact]
    public void An_else_branch_is_negated_rather_than_repeated()
    {
        // if/else is the commonest shape in the estate. Rendering the else with its
        // sibling's condition unnegated reads as "hide it when the condition holds", which
        // is the exact inverse of the template and looks plausible in the output.
        var condition = ConditionParser.Parse("this.value == \"No\"") with { Negated = true };

        var rendered = GuardRenderer.Render(
            [condition],
            new Dictionary<string, string> { ["Driver"] = "P.Driver" },
            "Driver",
            "IsShown");

        Assert.StartsWith("!(", rendered);
        Assert.Contains("string.Equals(report.P.Driver, \"No\"", rendered);
    }

    [Fact]
    public void A_condition_that_is_not_negated_is_left_alone()
    {
        var rendered = GuardRenderer.Render(
            [ConditionParser.Parse("this.value == \"No\"")],
            new Dictionary<string, string> { ["Driver"] = "P.Driver" },
            "Driver",
            "IsShown");

        Assert.DoesNotContain("!(", rendered);
    }
}

/// <summary>
/// Clearing a hidden field's answer departs from the templates deliberately, so the cases
/// where it must not fire are worth pinning down.
/// </summary>
public class ClearHiddenTests
{
    [Fact]
    public void A_field_sharing_its_name_with_a_section_is_not_cleared()
    {
        // Templates reuse one name for a section and a field: 483 such names across the
        // estate. The section's rule reads as the field's, and clearing on it erases the
        // answer that decides the rule, so the agent cannot pick the value at all.
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
                                // The question, and a section named after it.
                                new Dictionary<string, object>
                                {
                                    ["SingleSelection"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "SingleSelection", ["elementName"] = "Sighted",
                                        ["name"] = "P.Sighted", ["title"] = "Sighted?",
                                        ["options"] = new object[]
                                        {
                                            new Dictionary<string, string> { ["Value"] = "Yes", ["Content"] = "Yes" }
                                        },
                                        ["scripts"] = new Dictionary<string, string>
                                        {
                                            ["OnValueChange"] =
                                                "if this.value == \"Yes\" then Sighted.visible = true; "
                                                + "else Sighted.visible = false; end"
                                        }
                                    }
                                },
                                new Dictionary<string, object>
                                {
                                    ["Section"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Section", ["elementName"] = "Sighted",
                                        ["name"] = "P.SightedDetails", ["title"] = "Details"
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        var doc = TemplateLoader.LoadJson(json, "Fixture V1");
        var page = PageEmitModelBuilder.Build(doc, doc.Pages.Single(), ControlVocabulary.Default, "");

        var result = VisibilityEmitter.Emit(
            doc, [page], VisibilityTable.Build(doc), "App", "Vis", "Report");

        // The rule belongs to the section, so the question keeps its answer. It used to be
        // cleared on the section's rule, which erased the answer that decided the rule and
        // left the value impossible to select.
        Assert.DoesNotContain("report.P.Sighted = null;", result.Code);
    }
}

/// <summary>
/// Which element a state rule belongs to when a name is shared.
/// </summary>
public class StateOwnershipTests
{
    private static (TemplateDocument Doc, PageEmitModel Page, VisibilityResult State) Build()
    {
        // A section named after the question inside the section before it, which is how the
        // estate is written: 483 names are claimed by more than one element.
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
                            ["fieldType"] = "Page", ["elementName"] = "P", ["name"] = "P", ["title"] = "Page",
                            ["children"] = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["SingleSelection"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "SingleSelection", ["elementName"] = "Sighted",
                                        ["name"] = "P.Ask.Sighted", ["title"] = "Sighted?",
                                        ["options"] = new object[]
                                        {
                                            new Dictionary<string, string> { ["Value"] = "Yes", ["Content"] = "Yes" }
                                        },
                                        ["scripts"] = new Dictionary<string, string>
                                        {
                                            ["OnValueChange"] =
                                                "if this.value == \"Yes\" then Sighted.visible = true; "
                                                + "else Sighted.visible = false; end"
                                        }
                                    }
                                },
                                new Dictionary<string, object>
                                {
                                    ["Section"] = new Dictionary<string, object>
                                    {
                                        // Alias repeating the element name, as the estate does.
                                        ["fieldType"] = "Section", ["elementName"] = "Sighted",
                                        ["alias"] = "Sighted", ["name"] = "P.Sighted", ["title"] = "",
                                        ["children"] = new object[]
                                        {
                                            new Dictionary<string, object>
                                            {
                                                ["Text"] = new Dictionary<string, object>
                                                {
                                                    ["fieldType"] = "Text", ["elementName"] = "Where",
                                                    ["name"] = "P.Sighted.Where", ["title"] = "Where?"
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

        var doc = TemplateLoader.LoadJson(json, "Fixture V1");
        var page = PageEmitModelBuilder.Build(doc, doc.Pages.Single(), ControlVocabulary.Default, "");
        var state = VisibilityEmitter.Emit(doc, [page], VisibilityTable.Build(doc), "App", "Vis", "Report");

        return (doc, page with { StateNames = VisibilityEmitter.StateNames(page, state) }, state);
    }

    [Fact]
    public void The_rule_belongs_to_the_section_not_the_question_that_decides_it()
    {
        var (_, page, _) = Build();

        Assert.True(page.StateNames.ContainsKey("P.Sighted"), "the section carries the rule");
        Assert.False(page.StateNames.ContainsKey("P.Ask.Sighted"), "the question must not");
    }

    [Fact]
    public void The_question_is_not_hidden_behind_its_own_answer()
    {
        // Binding the section's rule to the question too hides the question until it is
        // already answered, so it can never be answered at all.
        var (_, page, _) = Build();
        var xaml = XamlEmitter.EmitPage(page, "App");

        var asking = xaml.IndexOf("Sighted?", StringComparison.Ordinal);
        var before = xaml[..asking];

        Assert.DoesNotContain("IsVisible", before[before.LastIndexOf("<VerticalStackLayout", StringComparison.Ordinal)..]);
        Assert.Contains("IsVisible=\"{Binding ShowSighted}\"", xaml);
    }

    [Fact]
    public void An_alias_repeating_the_element_name_is_one_claim_not_two()
    {
        // Counted twice, a single element looks like two competing for the name and the
        // rule is attributed to neither, leaving the section always shown.
        var (doc, _, _) = Build();

        var section = doc.AllNodes.Single(n => n.Label == "P.Sighted");

        Assert.Equal(["P.Sighted", "Sighted"], section.Names);
    }
}

/// <summary>
/// What happens to an element the generator has nothing to say about.
///
/// Three ways to have nothing to say, and each defaulted to shown, which is wrong for an
/// element the template starts hidden: 468 across the estate are hidden and never shown,
/// and they hold reference data rather than questions.
/// </summary>
public class StartingStateTests
{
    private static (TemplateDocument Doc, PageEmitModel Page, VisibilityResult State) Build(
        bool hidden, string? script)
    {
        var section = new Dictionary<string, object>
        {
            ["fieldType"] = "Section", ["elementName"] = "Quotas", ["name"] = "P.Quotas",
            ["title"] = "Quotas", ["hidden"] = hidden,
            ["children"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["Text"] = new Dictionary<string, object>
                    {
                        ["fieldType"] = "Text", ["elementName"] = "MinVisits",
                        ["name"] = "P.Quotas.MinVisits", ["title"] = "Minimum visits"
                    }
                }
            }
        };

        var children = new List<object> { new Dictionary<string, object> { ["Section"] = section } };

        if (script is not null)
        {
            children.Insert(0, new Dictionary<string, object>
            {
                ["Text"] = new Dictionary<string, object>
                {
                    ["fieldType"] = "Text", ["elementName"] = "Driver", ["name"] = "P.Driver",
                    ["title"] = "Driver",
                    ["scripts"] = new Dictionary<string, string> { ["OnValueChange"] = script }
                }
            });
        }

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
                            ["title"] = "Page", ["children"] = children.ToArray()
                        }
                    }
                }
            }
        });

        var doc = TemplateLoader.LoadJson(json, "Fixture V1");
        var page = PageEmitModelBuilder.Build(doc, doc.Pages.Single(), ControlVocabulary.Default, "");
        var state = VisibilityEmitter.Emit(doc, [page], VisibilityTable.Build(doc), "App", "Vis", "Report");

        return (doc, page with { StateNames = VisibilityEmitter.StateNames(page, state) }, state);
    }

    [Fact]
    public void An_element_the_template_hides_and_nothing_shows_stays_hidden()
    {
        var (_, page, state) = Build(hidden: true, script: null);

        Assert.Contains("\"P.Quotas\" => false,", state.Code);
        Assert.True(page.StateNames.ContainsKey("P.Quotas"), "it has to be bound to stay hidden");
    }

    [Fact]
    public void An_element_the_template_shows_and_nothing_hides_needs_no_rule()
    {
        // The switch already answers true for anything it does not name, so saying so again
        // would be a property and a binding per element on every page for no effect.
        var (_, page, _) = Build(hidden: false, script: null);

        Assert.False(page.StateNames.ContainsKey("P.Quotas"));
    }

    [Fact]
    public void A_refused_rule_starts_from_the_templates_own_state()
    {
        // Two handlers, so the rule is refused and left to a person. Until someone writes
        // it the element must not appear, because the template does not show it.
        var (_, page, _) = Build(
            hidden: true,
            script: "if this.value == \"Yes\" then Quotas.visible = true; end");

        var binding = page.StateNames["P.Quotas"];

        Assert.True(binding.StartsHidden);
    }
}

/// <summary>
/// Numeric comparisons, which the estate writes three different ways.
/// </summary>
public class NumericGuardTests
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["Age"] = "P.Age", ["Actual"] = "P.Actual", ["Required"] = "P.Required"
    };

    private static string Render(string lua)
        => GuardRenderer.Render([ConditionParser.Parse(lua)], Paths, "Age", "IsShown");

    [Fact]
    public void A_bare_tonumber_asks_whether_the_answer_is_a_number()
    {
        // Lua's tonumber gives nil for text that is not a number, and nil is false.
        Assert.Equal("Num(report.P.Age) is not null", Render("tonumber(this.value)"));
    }

    [Fact]
    public void A_number_comparison_is_false_when_the_answer_is_not_a_number()
    {
        // Nullable on purpose: null < 18 is false in C# as it is in Lua, so a blank age
        // does not read as under eighteen.
        Assert.Equal("Num(report.P.Age) < 18", Render("tonumber(this.value) < 18"));
    }

    [Fact]
    public void Two_fields_are_compared_as_numbers_not_as_text()
    {
        // The template writes this without tonumber, so Lua compares lexicographically and
        // ten visits reads as fewer than nine. The intent is plainly numeric.
        Assert.Equal(
            "Num(report.P.Actual) < Num(report.P.Required)",
            Render("Actual.value < Required.value"));
    }

    [Fact]
    public void A_number_written_as_text_is_still_a_number()
    {
        // actVisits.value < "1" in the estate, quoted, which is the same mistake again.
        Assert.Equal("Num(report.P.Actual) < 1", Render("Actual.value < \"1\""));
    }
}

/// <summary>
/// The walker's block tracking, which the estate exercises with one-line blocks.
/// </summary>
public class OneLineBlockTests
{
    [Fact]
    public void A_block_that_opens_and_closes_on_one_line_does_not_leak_its_condition()
    {
        // Four of these sit between the two branches of one example template's rule.
        // Leaking them made the elseif update the wrong frame, and the rule came out as a
        // conjunction of branches that exclude one another: dead code that looked plausible.
        var statements = ScriptWalker.Walk("""
            if A.value == "x" then
                this.valid = false;
                if B.value == "y" then local m = 1 end
                if C.value == "z" then local m = 2 end
            elseif D.value == "w" then
                this.valid = false;
            end
            """, line => line.Contains("this.valid", StringComparison.Ordinal));

        Assert.Equal(2, statements.Count);

        var second = statements[1].Own.Single().ToString();

        Assert.Contains("D", second);
        Assert.DoesNotContain("A ", second);
        Assert.DoesNotContain("B ", second);
    }
}

/// <summary>
/// Which element a rule is about when its name means different things in different places.
/// </summary>
public class RuleOwnershipTests
{
    /// <summary>Two pages, each with a section called Next; only one is ever written.</summary>
    private static (PageEmitModel First, PageEmitModel Second) Build()
    {
        static object Page(string name, string title, bool hidden, string? script) =>
            new Dictionary<string, object>
            {
                ["Page"] = new Dictionary<string, object>
                {
                    ["fieldType"] = "Page", ["elementName"] = name, ["name"] = name, ["title"] = title,
                    ["children"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["SingleSelection"] = new Dictionary<string, object>
                            {
                                ["fieldType"] = "SingleSelection", ["elementName"] = name + "Outcome",
                                ["name"] = $"{name}.Outcome", ["title"] = "Outcome",
                                ["options"] = new object[]
                                {
                                    new Dictionary<string, string> { ["Value"] = "Done", ["Content"] = "Done" }
                                },
                                ["scripts"] = script is null
                                    ? new Dictionary<string, string>()
                                    : new Dictionary<string, string> { ["OnValueChange"] = script }
                            }
                        },
                        new Dictionary<string, object>
                        {
                            ["Section"] = new Dictionary<string, object>
                            {
                                ["fieldType"] = "Section", ["elementName"] = "Next",
                                ["name"] = $"{name}.Next", ["title"] = "", ["hidden"] = hidden,
                                ["children"] = new object[]
                                {
                                    new Dictionary<string, object>
                                    {
                                        ["Button"] = new Dictionary<string, object>
                                        {
                                            ["fieldType"] = "Button", ["elementName"] = name + "Button",
                                            ["name"] = $"{name}.Next.Button", ["title"] = "Next"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Form"] = new Dictionary<string, object>
            {
                ["fieldType"] = "Form",
                ["children"] = new object[]
                {
                    Page("A", "Page A", hidden: true,
                        "if this.value ~= \"\" then Next.visible = true; else Next.visible = false; end"),
                    Page("B", "Page B", hidden: false, null)
                }
            }
        });

        var doc = TemplateLoader.LoadJson(json, "Fixture V1");
        var pages = doc.Pages
            .Select(p => PageEmitModelBuilder.Build(doc, p, ControlVocabulary.Default, ""))
            .ToList();

        var state = VisibilityEmitter.Emit(doc, pages, VisibilityTable.Build(doc), "App", "Vis", "Report");

        return (pages[0] with { StateNames = VisibilityEmitter.StateNames(pages[0], state) },
                pages[1] with { StateNames = VisibilityEmitter.StateNames(pages[1], state) });
    }

    [Fact]
    public void A_shared_name_resolves_to_the_page_the_handler_is_on()
    {
        // A script reaching a field by bare name means the one beside it. One example
        // template has fourteen sections called Next and only one is ever written.
        var (first, second) = Build();

        Assert.True(first.StateNames.ContainsKey("A.Next"), "the written one binds the rule");
        Assert.False(second.StateNames.ContainsKey("B.Next"), "the others are left alone");
    }

    [Fact]
    public void The_rule_binds_rather_than_becoming_a_question_for_a_person()
    {
        // It went the other way once: the name looked ambiguous, the rule became a seam
        // defaulting to the template's hidden flag, and the button never appeared at all.
        var (first, _) = Build();

        Assert.False(first.StateNames["A.Next"].Undecided);
        Assert.True(first.StateNames["A.Next"].Shown);
    }
}

/// <summary>
/// Comparisons with a conversion on both sides, which the estate uses for visit counts.
/// </summary>
public class NumberOnBothSidesTests
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["Actual"] = "P.Actual", ["Required"] = "P.Required"
    };

    [Fact]
    public void Both_sides_converted_stays_one_comparison()
    {
        // Read as two separate conversions it becomes "Actual is a number and Required is
        // a number", which holds whenever the question can be asked at all and silently
        // drops the comparison. It emitted a rule that was always true.
        var rendered = GuardRenderer.Render(
            [ConditionParser.Parse("tonumber(Actual.value) < tonumber(Required.value)")],
            Paths, "Actual", "IsShown");

        Assert.Equal("Num(report.P.Actual) < Num(report.P.Required)", rendered);
        Assert.DoesNotContain("is not null", rendered);
    }

    [Fact]
    public void A_conversion_on_its_own_is_still_a_test_that_it_is_a_number()
    {
        var rendered = GuardRenderer.Render(
            [ConditionParser.Parse("tonumber(Actual.value)")], Paths, "Actual", "IsShown");

        Assert.Equal("Num(report.P.Actual) is not null", rendered);
    }
}

/// <summary>
/// Asking whether a field was answered, when the answer is not text.
/// </summary>
public class UnansweredTests
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["Notes"] = "P.Notes", ["Date1"] = "P.Date1"
    };

    private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)
    {
        ["P.Notes"] = "string?", ["P.Date1"] = "DateTime?"
    };

    [Fact]
    public void Text_is_asked_whether_it_is_empty()
    {
        Assert.Equal(
            "string.IsNullOrEmpty(report.P.Notes)",
            GuardRenderer.Render(
                [ConditionParser.Parse("Notes.value == \"\"")], Paths, "Notes", "IsShown", null, Types));
    }

    [Fact]
    public void A_date_is_asked_whether_it_is_there()
    {
        // FormWorks holds every answer as text, so the template compares a date to "".
        // Here a date is a date, and asking whether it is an empty string does not compile.
        Assert.Equal(
            "report.P.Date1 is null",
            GuardRenderer.Render(
                [ConditionParser.Parse("Date1.value == \"\"")], Paths, "Date1", "IsShown", null, Types));
    }

    [Fact]
    public void A_date_that_must_be_answered_negates_cleanly()
    {
        Assert.Equal(
            "!(report.P.Date1 is null)",
            GuardRenderer.Render(
                [ConditionParser.Parse("Date1.value ~= \"\"")], Paths, "Date1", "IsShown", null, Types));
    }
}
