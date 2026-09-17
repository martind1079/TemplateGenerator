using System.Text.Json;
using System.Text.RegularExpressions;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// The verifier checks meaning, not syntax, so these cover the shape of the emitted
/// text. A backing field emitted without its semicolon passed every semantic check and
/// was caught only by the compiler.
/// </summary>
public class EmitterTests
{
    private static PageEmitModel Model(params (string Type, string Name, string Title, string[] Options)[] fields)
        => Model(ControlVocabulary.Default, fields);

    private static PageEmitModel Model(
        ControlVocabulary vocabulary, params (string Type, string Name, string Title, string[] Options)[] fields)
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
                            ["fieldType"] = "Page", ["elementName"] = "JobSheet", ["name"] = "JobSheet",
                            ["title"] = "Job Sheet",
                            ["children"] = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["Section"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Section", ["elementName"] = "S", ["name"] = "JobSheet.S",
                                        ["title"] = "Details",
                                        ["children"] = fields.Select(f => (object)new Dictionary<string, object>
                                        {
                                            [f.Type] = new Dictionary<string, object>
                                            {
                                                ["fieldType"] = f.Type,
                                                ["elementName"] = f.Name,
                                                ["name"] = $"JobSheet.S.{f.Name}",
                                                ["title"] = f.Title,
                                                ["options"] = f.Options
                                                    .Select(o => new Dictionary<string, string> { ["Value"] = o, ["Content"] = o })
                                                    .ToArray()
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

        var doc = TemplateLoader.LoadJson(json, "Fixture V1");
        return PageEmitModelBuilder.Build(doc, doc.Pages.Single(), vocabulary, "");
    }

    [Fact]
    public void Every_backing_field_declaration_is_terminated()
    {
        var code = ModelEmitter.EmitAnswers(
            Model(("Text", "Name", "Name", []), ("SingleSelection", "Reason", "Why", ["a"])),
            "App");

        var unterminated = Regex.Matches(code, @"^\s*private\s+[^\s]+\s+_\w+\s*$", RegexOptions.Multiline);
        Assert.Empty(unterminated);
    }

    [Fact]
    public void Answers_have_setters_so_they_survive_a_round_trip()
    {
        var code = ModelEmitter.EmitAnswers(Model(("Text", "Name", "Name", [])), "App");

        Assert.Contains("set => SetProperty", code);
        Assert.DoesNotContain("public string Name { get; }", code);
    }

    [Fact]
    public void Option_lists_are_kept_out_of_serialisation()
    {
        var code = ModelEmitter.EmitAnswers(Model(("SingleSelection", "Reason", "Why", ["a", "b"])), "App");

        Assert.Contains("[JsonIgnore]", code);
        Assert.Contains("ReasonOptions", code);
    }

    [Fact]
    public void Colliding_leaf_names_are_qualified_rather_than_overwritten()
    {
        var model = Model(("Text", "Name", "Name", []), ("Text", "Name", "Name", []));

        Assert.Equal(2, model.AllFields.Select(f => f.PropertyName).Distinct().Count());
    }

    [Fact]
    public void An_ampersand_in_a_title_is_escaped_so_the_page_parses()
    {
        var xaml = XamlEmitter.EmitPage(Model(("Text", "Id", "ID&V evidence", [])), "App");

        Assert.Contains("ID&amp;V evidence", xaml);
        Assert.DoesNotContain("ID&V", xaml);
    }

    [Fact]
    public void A_newline_in_a_title_does_not_break_the_attribute()
    {
        var xaml = XamlEmitter.EmitPage(Model(("Text", "Id", "Line one\nLine two", [])), "App");

        Assert.Contains("&#10;", xaml);
    }

    [Fact]
    public void A_button_is_emitted_with_its_caption_and_a_command()
    {
        var model = Model(("Button", "Next", "Next", []));

        // It holds no answer, so it is not a field.
        Assert.Empty(model.AllFields);

        var action = Assert.Single(model.Actions);
        Assert.Equal("NextCommand", action.CommandName);

        var xaml = XamlEmitter.EmitPage(model, "App");
        Assert.Contains("<Button Text=\"Next\"", xaml);
        Assert.Contains("Command=\"{Binding NextCommand}\"", xaml);
    }

    [Fact]
    public void A_buttons_command_is_left_empty_for_routing_to_fill()
    {
        // A partial method nobody implements compiles away to nothing, so the button is
        // inert rather than throwing when it is tapped.
        var code = ModelEmitter.EmitViewModel(Model(("Button", "Next", "Next", [])), "App", "Report", "Nav", "Validator", "Routes", new HashSet<string>());

        Assert.Contains("public Command NextCommand { get; }", code);
        Assert.Contains("partial void OnNext();", code);
        Assert.Contains("NextCommand = new Command(() => OnNext());", code);
    }

    [Fact]
    public void A_page_with_no_buttons_still_takes_the_report_store()
    {
        // Every page reads its answers from the shared report, buttons or not.
        var code = ModelEmitter.EmitViewModel(
            Model(("Text", "Name", "Name", [])), "App", "Report", "Nav", "Validator", "Routes", new HashSet<string>());

        Assert.Contains("IFormStore<Report> store", code);

        // Every page gets the hand-written validation hook, buttons or not: a rule the
        // generator refused can sit on a page that has no Next of its own.
        Assert.Contains("partial void OnValidated();", code);
        Assert.Contains("OnValidated();", code);
    }

    [Fact]
    public void The_page_commits_to_no_appearance_of_its_own()
    {
        // Colours, fonts and thicknesses belong to the style sheet, so a restyle needs no
        // regeneration. The page names styles and nothing else.
        var xaml = XamlEmitter.EmitPage(
            Model(("Text", "Name", "Name", []), ("ParagraphText", "Notes", "Notes", [])), "App");

        foreach (var attribute in new[]
                 { "TextColor=", "BackgroundColor=", "FontSize=", "FontFamily=", "FontAttributes=", "Stroke=" })
            Assert.DoesNotContain(attribute, xaml);

        Assert.Contains("Style=\"{StaticResource FormFieldCaption}\"", xaml);
    }

    [Fact]
    public void Style_keys_are_configuration_so_a_repository_can_use_its_own()
    {
        var model = Model(
            new ControlVocabulary { CaptionStyleKey = "HouseCaption" },
            ("Text", "Name", "Name", []));

        Assert.Contains("{StaticResource HouseCaption}", XamlEmitter.EmitPage(model, "App"));
    }

    [Fact]
    public void The_verifier_rejects_a_resource_key_the_app_does_not_have()
    {
        var model = Model(("Text", "Name", "Name", []));
        var xaml = XamlEmitter.EmitPage(model, "App")
            .Replace("FontAttributes=\"Bold\"", "Style=\"{StaticResource Gray100}\"");

        var problems = EmitVerifier.Verify(model, xaml, ModelEmitter.EmitAnswers(model, "App"),
            new HashSet<string> { "Grey100" });

        Assert.Contains(problems, p => p.Kind == EmitVerifier.MissingResourceKey);
    }
}
