using System.Text;
using FormWorks.Templates.Model;

namespace FormWorks.Templates.Emit;

/// <summary>
/// Writes the glue that makes generated pages reachable: route registration, dependency
/// registration, and an index page to walk them from.
///
/// Three hand edits per page does not scale to sixteen, let alone to the estate, and
/// until it is generated only the one page anybody wired up can actually be opened. A
/// page that compiles and cannot be shown is a page nobody has checked.
/// </summary>
public static class RoutesEmitter
{
    public static string EmitRegistrations(
        TemplateDocument doc, IReadOnlyList<PageEmitModel> pages, HouseStyle style, string className,
        string reportClassName, string? templateName = null)
    {
        var sb = new StringBuilder();
        Header(sb, doc);

        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine($"using {style.ModelsNamespace};");
        sb.AppendLine($"using {style.ServicesNamespace};");
        sb.AppendLine($"using {style.ViewModelsNamespace};");
        sb.AppendLine();
        sb.AppendLine($"namespace {style.ViewsNamespace};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Routes and registrations for {Escape(doc.FolderName)}.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Where the form starts: the template's first page.");
        sb.AppendLine("    ///");
        sb.AppendLine("    /// Wire the menu entry to this. There is no index of pages, because the form");
        sb.AppendLine("    /// decides where to go next from the answers and the step strip shows where");
        sb.AppendLine("    /// that is. A menu of every page is a thing to review a conversion with, not a");
        sb.AppendLine("    /// thing to hand an agent.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public const string EntryRoute = \"//{Route(pages[0].ClassName)}\";");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// The template this came from, as the estate names it.");
        sb.AppendLine("    ///");
        sb.AppendLine("    /// Written down rather than inferred from the class name, so a host app can list");
        sb.AppendLine("    /// what it has converted without a table somebody has to remember to update.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public const string TemplateName = \"{Escape(templateName ?? doc.FolderName)}\";");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Every page of this template, in template order.</summary>");
        sb.AppendLine("    public static IReadOnlyList<(string Title, string Route)> Pages { get; } = new[]");
        sb.AppendLine("    {");
        foreach (var page in pages)
            sb.AppendLine($"        (\"{Escape(Title(page))}\", \"//{Route(page.ClassName)}\"),");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Adds a shell entry per page, so each has an absolute route.");
        sb.AppendLine("    ///");
        sb.AppendLine("    /// Absolute routes replace the page; relative ones push onto a stack. A form moves");
        sb.AppendLine("    /// between pages of one report, so it replaces: pushing would grow a back stack");
        sb.AppendLine("    /// sixteen deep and put a back chevron on a form.");
        sb.AppendLine("    ///");
        sb.AppendLine("    /// Built in code rather than declared in AppShell.xaml, because sixteen entries a");
        sb.AppendLine("    /// template is not something anybody should hand-write.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static void RegisterShellContent(Shell shell, IServiceProvider services)");
        sb.AppendLine("    {");
        foreach (var page in pages)
        {
            sb.AppendLine("        shell.Items.Add(new ShellContent");
            sb.AppendLine("        {");
            sb.AppendLine($"            Title = \"{XmlAttr(Title(page))}\",");
            sb.AppendLine($"            Route = \"{Route(page.ClassName)}\",");
            // Resolved from the container rather than by Activator: the page needs its
            // view model, which needs the report store.
            sb.AppendLine($"            ContentTemplate = new DataTemplate(() => services.GetRequiredService<{page.ClassName}>()),");
            sb.AppendLine("            FlyoutItemIsVisible = false");
            sb.AppendLine("        });");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Pages and view models are transient, as the app's own are. The report is a");
        sb.AppendLine("    /// singleton: it outlives them, which is what lets answers survive navigation.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static IServiceCollection AddGeneratedPages(this IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine($"        services.AddSingleton<IFormStore<{reportClassName}>, FormStore<{reportClassName}>>();");
        foreach (var page in pages)
        {
            sb.AppendLine($"        services.AddTransient<{page.ClassName}>();");
            sb.AppendLine($"        services.AddTransient<{page.ClassName}ViewModel>();");
        }
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Runs every page's rules against the current report, then says whether the");
        sb.AppendLine("    /// whole thing came back clean.");
        sb.AppendLine("    ///");
        sb.AppendLine("    /// A page not currently open has no view model to run its rules through - pages");
        sb.AppendLine("    /// are transient - so each one is resolved from services just long enough to");
        sb.AppendLine("    /// call Validate() on it, then let go. What persists is the answer, not the");
        sb.AppendLine("    /// page: Validate() writes each field's error state onto the report, which is");
        sb.AppendLine($"    /// what {reportClassName}.IsValid then reads back.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static bool ValidateAll(IServiceProvider services)");
        sb.AppendLine("    {");
        foreach (var page in pages)
            sb.AppendLine($"        services.GetRequiredService<{page.ClassName}ViewModel>().Validate();");
        sb.AppendLine();
        sb.AppendLine($"        var report = services.GetRequiredService<IFormStore<{reportClassName}>>().Current;");
        sb.AppendLine("        report.ShowValidationErrors = true;");
        sb.AppendLine("        return report.IsValid;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Title(PageEmitModel page)
        => page.Page.Title is { Length: > 0 } t ? t : page.PageName;

    public static string Route(string className)
        => char.ToLowerInvariant(className[0]) + className[1..];

    private static void Header(StringBuilder sb, TemplateDocument doc)
    {
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"//     Generated from {doc.FolderName}.");
        sb.AppendLine("//     Do not edit. Regenerate instead; edits here are lost.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
    }

    private static string Escape(string text)
        => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string XmlAttr(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
