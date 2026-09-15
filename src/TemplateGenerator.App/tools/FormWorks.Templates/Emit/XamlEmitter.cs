using System.Text;
using FormWorks.Templates.Model;

namespace FormWorks.Templates.Emit;

/// <summary>Writes the page and its code-behind.</summary>
public static class XamlEmitter
{
    public static string EmitPage(PageEmitModel page, string rootNamespace)
    {
        var v = page.Vocabulary;
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
        sb.AppendLine("<!-- Generated from " + Attr(page.Document.FolderName) + ", page " + Attr(page.PageName) + ". Do not edit. -->");
        sb.AppendLine("<ContentPage xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\"");
        sb.AppendLine("             xmlns:x=\"http://schemas.microsoft.com/winfx/2009/xaml\"");
        sb.AppendLine($"             xmlns:controls=\"clr-namespace:{rootNamespace}.Views.Controls\"");
        sb.AppendLine($"             xmlns:viewModels=\"clr-namespace:{rootNamespace}.ViewModels.Generated\"");
        sb.AppendLine($"             x:Class=\"{rootNamespace}.Views.Generated.{page.ClassName}\"");
        sb.AppendLine($"             x:DataType=\"viewModels:{page.ClassName}ViewModel\"");
        sb.AppendLine($"             Title=\"{Attr(PageTitle(page))}\">");
        sb.AppendLine();
        sb.AppendLine("    <ScrollView>");
        sb.AppendLine("        <VerticalStackLayout Padding=\"20\" Spacing=\"20\">");
        sb.AppendLine();
        sb.AppendLine("            <!-- Which pages this report will pass through, given the answers so far. -->");
        sb.AppendLine("            <controls:FormStepSelectorControl Steps=\"{Binding Steps}\"");
        sb.AppendLine("                                             StepCommand=\"{Binding GoToStepCommand}\" />");
        sb.AppendLine();

        EmitRows(sb, page.Rows, indent: 3, v, page);

        sb.AppendLine("        </VerticalStackLayout>");
        sb.AppendLine("    </ScrollView>");
        sb.AppendLine();
        sb.AppendLine("</ContentPage>");
        return sb.ToString();
    }

    /// <summary>
    /// A container is a titled card, or a plain stack when the template gives it no
    /// title. Groups are cards too: the template uses them to put two columns of
    /// questions beside each other, and the agent reads them as separate panels.
    /// </summary>
    private static void EmitContainer(StringBuilder sb, EmittedGroup group, int indent, int? column, ControlVocabulary v, PageEmitModel page)
    {
        var pad = new string(' ', indent * 4);
        var grid = column is { } c ? $" Grid.Column=\"{c}\"" : "";

        if (group.Title.Length == 0)
        {
            sb.AppendLine($"{pad}<VerticalStackLayout{Margin(group.Source)}{State(group.Source, page)}{grid}>");
            EmitRows(sb, group.Rows, indent + 1, v, page);
            sb.AppendLine($"{pad}</VerticalStackLayout>");
            return;
        }

        sb.AppendLine($"{pad}<controls:SubSectionControl Title=\"{Attr(group.Title)}\"{Margin(group.Source)}{State(group.Source, page)}{grid}>");
        sb.AppendLine($"{pad}    <VerticalStackLayout Padding=\"20,10\">");
        EmitRows(sb, group.Rows, indent + 2, v, page);
        sb.AppendLine($"{pad}    </VerticalStackLayout>");
        sb.AppendLine($"{pad}</controls:SubSectionControl>");
    }

    private static void EmitRows(StringBuilder sb, IReadOnlyList<RowItem> rows, int indent, ControlVocabulary v, PageEmitModel page)
    {
        foreach (var row in rows)
            EmitRow(sb, row, indent, v, page);
    }

    /// <summary>
    /// A row of one is emitted as the cell alone. Wrapping a single field in a grid adds
    /// a layout pass per field, and a page has dozens.
    /// </summary>
    private static void EmitRow(StringBuilder sb, RowItem row, int indent, ControlVocabulary v, PageEmitModel page)
    {
        var pad = new string(' ', indent * 4);

        // A single cell that fills its row needs no grid; one that does not needs a
        // column beside it to take the slack, or it stretches.
        var columns = row.ColumnDefinitions.Count(c => c == ',') + 1;
        if (row.Cells.Count == 1 && columns == 1)
        {
            EmitCell(sb, row.Cells[0], indent, column: null, v, page);
            return;
        }

        sb.AppendLine($"{pad}<Grid ColumnDefinitions=\"{row.ColumnDefinitions}\">");
        for (var i = 0; i < row.Cells.Count; i++)
            EmitCell(sb, row.Cells[i], indent + 1, i, v, page);
        sb.AppendLine($"{pad}</Grid>");
    }

    private static void EmitCell(StringBuilder sb, Cell cell, int indent, int? column, ControlVocabulary v, PageEmitModel page)
    {
        switch (cell)
        {
            case ContainerCell container:
                EmitContainer(sb, container.Group, indent, column, v, page);
                return;
            case ElementCell element:
                EmitElement(sb, element.Element, indent, column, v, page);
                return;
        }
    }

    private static void EmitElement(StringBuilder sb, EmittedElement cell, int indent, int? column, ControlVocabulary v, PageEmitModel page)
    {
        var pad = new string(' ', indent * 4);
        var grid = column is { } c ? $" Grid.Column=\"{c}\"" : "";

        // A button. It carries a caption and a width like anything else, so it packs into
        // its row the same way; what it does when tapped belongs to the routing table.
        if (cell.IsAction)
        {
            sb.AppendLine($"{pad}<{cell.ActionControl} Text=\"{Attr(cell.Title)}\" Command=\"{{Binding {cell.CommandName}}}\"");
            sb.AppendLine($"{pad}        HorizontalOptions=\"Fill\"{Margin(cell.Source)}{State(cell.Source, page)}{grid} />");
            return;
        }

        // Static text the template carries as instructions to the agent. Dropping these
        // changes what the form asks, so they are rendered rather than skipped.
        if (!cell.IsInput)
        {
            if (cell.Source.FieldType == "Line")
                sb.AppendLine($"{pad}<BoxView Style=\"{{StaticResource {v.DividerStyleKey}}}\"{Margin(cell.Source)}{State(cell.Source, page)}{grid} />");
            else if (cell.Title.Length > 0)
                sb.AppendLine($"{pad}<Label Text=\"{Attr(cell.Title)}\" Style=\"{{StaticResource {v.InstructionStyleKey}}}\"{Margin(cell.Source)}{State(cell.Source, page)}{grid} />");
            return;
        }

        var mapping = cell.Mapping!;
        var path = $"Answers.{cell.BindingPath}";
        var height = cell.HeightRequest is { } h
            ? $" HeightRequest=\"{Num(h)}\""
            : "";

        // The field keeps its place in the row and its label runs full width; only the
        // control is narrowed, and it has to be told not to stretch back out.
        var content = cell.ContentWidth is { } cw
            ? $" WidthRequest=\"{Num(cw)}\" HorizontalOptions=\"Start\""
            : "";

        // A caption's worth of blank space, so a field whose caption is not above it still
        // lines up with the fields beside it that have one. Same style and same stack as a
        // real caption, so it cannot drift out of step with one.
        var reservesTitleSpace = cell.Source.AssignSpaceForTitle && cell.Title.Length == 0;
        var reservesForTrailing = cell.Source.AssignSpaceForTitle
                                  && mapping.Caption == CaptionPlacement.Trailing;

        if (mapping.Caption == CaptionPlacement.Trailing)
        {
            // A message needs a line of its own, which the control's two-column grid has
            // nowhere to put, so the pair is stacked when there is a rule to report.
            if (page.Validated.Contains(cell.BindingPath) && !reservesForTrailing)
            {
                sb.AppendLine($"{pad}<VerticalStackLayout Spacing=\"5\"{Margin(cell.Source)}{State(cell.Source, page)}{grid}>");
                EmitTrailing(sb, cell, mapping, path, indent + 1, null, v);
                EmitError(sb, cell, page, indent + 1);
                sb.AppendLine($"{pad}</VerticalStackLayout>");
                return;
            }

            if (reservesForTrailing)
            {
                sb.AppendLine($"{pad}<VerticalStackLayout Spacing=\"5\"{Margin(cell.Source)}{State(cell.Source, page)}{grid}>");
                sb.AppendLine($"{pad}    <Label Text=\" \" Style=\"{{StaticResource {v.CaptionStyleKey}}}\" />");
                EmitTrailing(sb, cell, mapping, path, indent + 1, null, v);
                EmitError(sb, cell, page, indent + 1);
                sb.AppendLine($"{pad}</VerticalStackLayout>");
                return;
            }

            EmitTrailing(sb, cell, mapping, path, indent, Margin(cell.Source) + State(cell.Source, page) + grid, v);
            return;
        }

        sb.AppendLine($"{pad}<VerticalStackLayout Spacing=\"5\"{Margin(cell.Source)}{State(cell.Source, page)}{grid}>");

        if (cell.Title.Length > 0)
            sb.AppendLine($"{pad}    <Label Text=\"{Attr(cell.Title)}\" Style=\"{{StaticResource {v.CaptionStyleKey}}}\" />");
        else if (reservesTitleSpace)
            sb.AppendLine($"{pad}    <Label Text=\" \" Style=\"{{StaticResource {v.CaptionStyleKey}}}\" />");

        if (cell.Options.Count > 0)
        {
            sb.AppendLine($"{pad}    <{mapping.Control} ItemsSource=\"{{Binding Answers.{cell.BindingPath}Options}}\"");
            sb.AppendLine($"{pad}            {mapping.BindingProperty}=\"{{Binding {path}}}\"{content}{Leaving(cell)} />");
        }
        else
        {
            sb.AppendLine($"{pad}    <{mapping.Control} {mapping.BindingProperty}=\"{{Binding {path}}}\"{height}{content}{Leaving(cell)} />");
        }

        EmitError(sb, cell, page, indent + 1);
        sb.AppendLine($"{pad}</VerticalStackLayout>");
    }

    public static string EmitCodeBehind(PageEmitModel page, string rootNamespace)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"//     Generated from {page.Document.FolderName}, page {page.PageName}.");
        sb.AppendLine("//     Do not edit. Regenerate instead; edits here are lost.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"using {rootNamespace}.ViewModels.Generated;");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace}.Views.Generated;");
        sb.AppendLine();
        sb.AppendLine($"public partial class {page.ClassName} : ContentPage");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// The view model is injected rather than constructed here, because it needs the");
        sb.AppendLine("    /// report store and a page that builds its own would get a private copy of the");
        sb.AppendLine("    /// answers. There is deliberately no parameterless constructor.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public {page.ClassName}({page.ClassName}ViewModel viewModel)");
        sb.AppendLine("    {");
        sb.AppendLine("        InitializeComponent();");
        sb.AppendLine("        BindingContext = viewModel;");
        sb.AppendLine("    }");
        sb.AppendLine();

        if (page.AllFields.Any(f => f.Source.Scripts.ContainsKey("OnBlur") && f.PropertyName is not null))
        {
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// The agent has finished with a field.");
            sb.AppendLine("    ///");
            sb.AppendLine("    /// Only wired to fields the template itself watches this way. What the form");
            sb.AppendLine("    /// does on leaving a field must not happen as the agent types in it: a rule");
            sb.AppendLine("    /// reading an age would fire on the \"3\" of \"30\".");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    private void OnAnswerLeft(object? sender, FocusEventArgs e)");
            sb.AppendLine("    {");
            sb.AppendLine($"        if (BindingContext is {page.ClassName}ViewModel model && sender is VisualElement field)");
            sb.AppendLine("            model.AnswerLeft(field.AutomationId);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// The strip is rebuilt on appearing, because an answer given on another page can");
        sb.AppendLine("    /// change which pages this report visits.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    protected override void OnAppearing()");
        sb.AppendLine("    {");
        sb.AppendLine("        base.OnAppearing();");
        sb.AppendLine($"        if (BindingContext is not {page.ClassName}ViewModel model) return;");
        sb.AppendLine();
        sb.AppendLine("        model.RefreshSteps();");
        sb.AppendLine("        model.Validate();");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// The message shown when a field is not valid. Emitted only for fields the template
    /// has a rule for, which is about a third of them.
    /// </summary>
    private static void EmitError(StringBuilder sb, EmittedElement cell, PageEmitModel page, int indent)
    {
        if (!page.Validated.Contains(cell.BindingPath)) return;

        var pad = new string(' ', indent * 4);
        sb.AppendLine($"{pad}<controls:FormFieldError Message=\"{{Binding Answers.{cell.BindingPath}Error}}\"");
        sb.AppendLine($"{pad}                        IsArmed=\"{{Binding Report.ShowValidationErrors}}\" />");
    }

    /// <summary>
    /// The control, then its caption beside it.
    ///
    /// A grid rather than a horizontal stack, because a stack lets its children take
    /// whatever width they ask for and the caption would run off the row instead of
    /// wrapping and growing the row taller.
    /// </summary>
    private static void EmitTrailing(
        StringBuilder sb, EmittedElement cell, ControlMapping mapping, string path,
        int indent, string? attributes, ControlVocabulary v)
    {
        var pad = new string(' ', indent * 4);

        // A minimum rather than a fixed height: short captions centre in the same band as
        // the entries beside them, and a caption long enough to wrap grows past it.
        sb.AppendLine($"{pad}<Grid ColumnDefinitions=\"Auto,*\" ColumnSpacing=\"{Num(v.CaptionGap)}\"");
        sb.AppendLine($"{pad}      MinimumHeightRequest=\"{Num(v.InputHeight)}\"{attributes}>");
        sb.AppendLine($"{pad}    <{mapping.Control} {mapping.BindingProperty}=\"{{Binding {path}}}\" VerticalOptions=\"Center\" />");

        if (cell.Title.Length > 0)
        {
            sb.AppendLine($"{pad}    <Label Text=\"{Attr(cell.Title)}\" Style=\"{{StaticResource {v.CaptionStyleKey}}}\"");
            sb.AppendLine($"{pad}           Grid.Column=\"1\" VerticalOptions=\"Center\" />");
        }

        sb.AppendLine($"{pad}</Grid>");
    }

    /// <summary>
    /// The template's own margins, in MAUI's left,top,right,bottom order. These are the
    /// spacing between components, which is why the emitter sets none of its own: a
    /// hardcoded gap on top of a declared margin is two gaps.
    /// </summary>
    /// <summary>
    /// The bindings that let the template decide whether an element is asked at all.
    ///
    /// Hidden means collapsed here, where FormWorks left the space: reclaimSpaceIfHidden is
    /// false on all 37,650 nodes in the estate because FormWorks positions absolutely and a
    /// hole is what that gives. This layout reflows, so the fields below move up.
    /// </summary>
    /// <summary>
    /// The hook that says the agent has finished with a field.
    ///
    /// Only on fields the template itself watches that way. A rule written on OnBlur waits
    /// for the agent to leave, and running it as they type fires it on the "3" of "30".
    ///
    /// The field names itself through AutomationId, because the handler is given the
    /// control and has to know which answer it holds, and a binding cannot carry that.
    /// </summary>
    private static string Leaving(EmittedElement cell)
        => cell.Source.Scripts.ContainsKey("OnBlur") && cell.PropertyName is { } property
            ? $" AutomationId=\"{Attr(property)}\" Unfocused=\"OnAnswerLeft\""
            : "";

    private static string State(TemplateNode node, PageEmitModel page)
    {
        if (!page.StateNames.TryGetValue(node.Label, out var binding)) return "";

        var visible = binding.Shown || binding.UndecidedShown
            ? $" IsVisible=\"{{Binding Show{binding.Name}}}\""
            : "";

        var enabled = binding.Usable || binding.UndecidedUsable
            ? $" IsEnabled=\"{{Binding Usable{binding.Name}}}\""
            : "";

        return visible + enabled;
    }

    private static string Margin(TemplateNode node)
    {
        if ((node.MarginLeft | node.MarginTop | node.MarginRight | node.MarginBottom) == 0)
            return "";

        return $" Margin=\"{node.MarginLeft},{node.MarginTop},{node.MarginRight},{node.MarginBottom}\"";
    }

    private static string Num(double value)
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string PageTitle(PageEmitModel page)
        => page.Page.Title is { Length: > 0 } t ? t : page.PageName;

    /// <summary>
    /// XAML attribute escaping. Titles in the estate carry ampersands and newlines, and
    /// an unescaped ampersand is a parse failure at build rather than a wrong pixel.
    /// </summary>
    private static string Attr(string text)
        => text.Replace("&", "&amp;")
               .Replace("<", "&lt;")
               .Replace(">", "&gt;")
               .Replace("\"", "&quot;")
               .Replace("\r\n", "&#10;")
               .Replace("\n", "&#10;")
               .Replace("\r", "&#10;");
}
