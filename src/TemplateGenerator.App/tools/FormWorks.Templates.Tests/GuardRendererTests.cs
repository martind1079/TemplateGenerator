using FormWorks.Templates.Analysis;
using FormWorks.Templates.Emit;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// FormWorks holds every answer as text, so a template asks whether a field was answered by
/// comparing it to "". The generated property is typed, so that comparison has to be
/// rendered as whatever "unanswered" means for the type it lands on.
/// </summary>
public class GuardRendererTests
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["Page.Notes"] = "Page.Notes",
        ["Page.Visited"] = "Page.Visited",
        ["Page.Seen"] = "Page.Seen"
    };

    private static readonly Dictionary<string, string> Types = new()
    {
        ["Page.Notes"] = "string",
        ["Page.Visited"] = "DateTime?",
        ["Page.Seen"] = "bool"
    };

    private static string Render(string field, string op) => GuardRenderer.Atom(
        new Guard(field, op, "", null, null), Paths, self: null, stateCall: "IsShown", types: Types);

    [Fact]
    public void Text_is_unanswered_when_empty()
        => Assert.Equal("string.IsNullOrEmpty(report.Page.Notes)", Render("Page.Notes", "=="));

    [Fact]
    public void A_nullable_value_is_unanswered_when_null()
        => Assert.Equal("report.Page.Visited is null", Render("Page.Visited", "=="));

    /// <summary>
    /// A checkbox is ticked or it is not, so it cannot be unanswered. Rendering the test as a
    /// null check produced code that did not compile, which the emitter's own verification
    /// could not see: it checks bindings and resource keys, not types.
    /// </summary>
    [Fact]
    public void A_checkbox_can_never_be_unanswered()
        => Assert.Equal("false", Render("Page.Seen", "=="));

    [Fact]
    public void A_checkbox_is_always_answered()
        => Assert.Equal("true", Render("Page.Seen", "~="));
}
