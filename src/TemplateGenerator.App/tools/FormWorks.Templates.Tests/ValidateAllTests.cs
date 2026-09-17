using System.Text.Json;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Model;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// A page not currently open has no view model to run its rules through, because pages are
/// transient - so checking the whole report means resolving every page's view model from
/// services just long enough to validate it, then reading back what persisted on the report
/// itself. ValidateAll and IsValid are the two halves of that: one runs every page, the
/// other reads what running them left behind.
/// </summary>
public class ValidateAllTests
{
    private static (IReadOnlyList<PageEmitModel> Pages, TemplateDocument Doc) TwoPages()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Form"] = new Dictionary<string, object>
            {
                ["fieldType"] = "Form", ["width"] = 980,
                ["children"] = new object[]
                {
                    Page("JobSheet", "Job Sheet", ("Text", "Reference")),
                    Page("Closure", "Closure", ("Text", "Notes")),
                }
            }
        });

        var doc = TemplateLoader.LoadJson(json, "F V1");
        var pages = doc.Pages
            .Select(p => PageEmitModelBuilder.Build(doc, p, ControlVocabulary.Default, ""))
            .ToList();

        return (pages, doc);
    }

    private static object Page(string name, string title, params (string Type, string Field)[] fields)
        => new Dictionary<string, object>
        {
            ["Page"] = new Dictionary<string, object>
            {
                ["fieldType"] = "Page", ["elementName"] = name, ["name"] = name,
                ["title"] = title, ["width"] = 980,
                ["children"] = fields.Select(f => (object)new Dictionary<string, object>
                {
                    [f.Type] = new Dictionary<string, object>
                    {
                        ["fieldType"] = f.Type, ["elementName"] = f.Field,
                        ["name"] = $"{name}.{f.Field}", ["title"] = f.Field, ["width"] = 940
                    }
                }).ToArray()
            }
        };

    [Fact]
    public void A_template_with_no_validated_fields_is_trivially_valid()
    {
        var (pages, doc) = TwoPages();

        var code = ReportEmitter.Emit(doc, pages, "App", "Report", ControlVocabulary.Default);

        Assert.Contains("public bool IsValid => true;", code);
    }

    [Fact]
    public void IsValid_checks_every_validated_field_on_every_page()
    {
        var (pages, doc) = TwoPages();
        var validated = new List<PageEmitModel>
        {
            pages[0] with { Validated = new HashSet<string> { "Reference" } },
            pages[1] with { Validated = new HashSet<string> { "Notes" } },
        };

        var code = ReportEmitter.Emit(doc, validated, "App", "Report", ControlVocabulary.Default);

        Assert.Contains("public bool IsValid =>", code);
        Assert.Contains("JobSheet.ReferenceError is null &&", code);
        Assert.Contains("Closure.NotesError is null;", code);
    }

    [Fact]
    public void ValidateAll_runs_every_pages_view_model_before_reading_the_report()
    {
        var (pages, doc) = TwoPages();

        var code = RoutesEmitter.EmitRegistrations(doc, pages, "App", "Routes", "Report");

        Assert.Contains("services.GetRequiredService<JobSheetPageViewModel>().Validate();", code);
        Assert.Contains("services.GetRequiredService<ClosurePageViewModel>().Validate();", code);
        Assert.Contains("report.ShowValidationErrors = true;", code);
        Assert.Contains("return report.IsValid;", code);

        // Both pages are validated, and the report read, only after - a page validated after
        // the report has already been read would leave that page's answers unchecked.
        var lastValidate = code.LastIndexOf(".Validate();", StringComparison.Ordinal);
        var reportRead = code.IndexOf("var report =", StringComparison.Ordinal);
        Assert.True(lastValidate < reportRead);
    }
}
