using System.Diagnostics;
using FormWorks.Templates.Analysis;
using FormWorks.Templates.Model;

namespace FormWorks.Templates.Emit;

/// <summary>What to convert, and where to put it.</summary>
public sealed record ConversionRequest
{
    public required TemplateDocument Document { get; init; }

    /// <summary>The MAUI project directory. Output lands in Views, ViewModels and Models beneath it.</summary>
    public required string AppDirectory { get; init; }

    /// <summary>
    /// The root namespace of the app being generated into. Required: there is no sensible
    /// default, and a plausible wrong one produces output that will not compile.
    /// </summary>
    public required string RootNamespace { get; init; }

    /// <summary>Overrides the class-name stem derived from the template's family and version.</summary>
    public string Prefix { get; init; } = "";

    /// <summary>Element names of the pages to emit. Empty means every page.</summary>
    public IReadOnlyList<string> Pages { get; init; } = [];

    /// <summary>Verify and report, writing nothing.</summary>
    public bool DryRun { get; init; }


    public ControlVocabulary Vocabulary { get; init; } = ControlVocabulary.Default;
}

/// <summary>One page's outcome. <see cref="Xaml"/> is carried so a caller can preview it.</summary>
public sealed record ConvertedPage(
    string PageName,
    string ClassName,
    int FieldCount,
    int RowCount,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<EmitProblem> Problems,
    string Xaml)
{
    public bool Verified => Problems.Count == 0;
}

/// <summary>
/// What a conversion did, as numbers rather than console output.
///
/// Every count the command line prints is here, so the two callers cannot drift into
/// reporting different things about the same run.
/// </summary>
public sealed record ConversionResult
{
    public required TemplateDocument Document { get; init; }

    public required IReadOnlyList<ConvertedPage> Pages { get; init; }

    public IReadOnlyList<string> FilesWritten { get; init; } = [];

    public TimeSpan Duration { get; init; }

    /// <summary>Set once the whole template is emitted; null for a single page or a failure.</summary>
    public string? EntryRoute { get; init; }

    public string? RoutesClass { get; init; }

    public string? RemainingWorkPath { get; init; }

    public string ReportClass { get; init; } = "";

    public string NavigatorClass { get; init; } = "";

    public string ValidatorClass { get; init; } = "";

    public string VisibilityClass { get; init; } = "";

    public string ComputedClass { get; init; } = "";

    public int RoutesEmitted { get; init; }

    public int RulesEmitted { get; init; }

    public int RulesTotal { get; init; }

    public int MessagesNeedingWording { get; init; }

    public int ShownRules { get; init; }

    public int UsableRules { get; init; }

    public int StateLeftToAPerson { get; init; }

    public int ValuesFilledIn { get; init; }

    public int ValuesLeftToAPerson { get; init; }

    public IReadOnlyList<(string SourceField, string Detail)> Unroutable { get; init; } = [];

    public bool Succeeded => Pages.Count > 0 && Pages.All(p => p.Verified);

    public int FieldsBound => Pages.Sum(p => p.FieldCount);

    public int Failures => Pages.Count(p => !p.Verified);
}

/// <summary>
/// Runs a template through every emitter, in the order they depend on each other.
///
/// This is the whole of conversion. It lives in the library rather than in the command line
/// because the command line is not the only caller: the demo app converts a template the
/// same way, and a second implementation of this order would drift from the first.
///
/// Nothing is written unless every page passes verification. A template that half-converts
/// leaves an app that does not compile and a page whose siblings are unregistered.
/// </summary>
public static class TemplateConverter
{
    /// <summary>
    /// The class-name stem that keeps one template's pages from colliding with another's.
    ///
    /// Page classes are otherwise named after the page alone, and two templates in one app
    /// routinely share a page name — Closure, Photographs, Next. An app that holds more than
    /// one converted template should pass this as the request's prefix; one that holds a
    /// single template need not.
    /// </summary>
    public static string PrefixFor(TemplateDocument document)
        => document.Version is { } version
            ? $"{PageEmitModelBuilder.Identifier(document.Family)}V{version}"
            : PageEmitModelBuilder.Identifier(document.Family);

    public static ConversionResult Convert(ConversionRequest request)
    {
        var started = Stopwatch.StartNew();
        var doc = request.Document;
        var app = request.AppDirectory;
        var vocabulary = request.Vocabulary;

        var wholeTemplate = request.Pages.Count == 0;
        var pages = wholeTemplate
            ? doc.Pages.ToList()
            : request.Pages
                .Select(name => doc.Pages.FirstOrDefault(p =>
                            string.Equals(p.ElementName, name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException(
                            $"No page '{name}'. Pages: {string.Join(", ", doc.Pages.Select(p => p.ElementName))}"))
                .ToList();

        // The keys the app's style sheet actually declares, so a page naming one it does not
        // have is stopped here rather than crashing when it is shown.
        var keys = EmitVerifier.ReadResourceKeys(app);

        var family = request.Prefix.Length > 0
            ? request.Prefix
            : $"{PageEmitModelBuilder.Identifier(doc.Family)}V{doc.Version}";

        var reportClass = $"{family}Report";
        var navigatorClass = $"{family}Navigator";
        var validatorClass = $"{family}Validator";
        var visibilityClass = $"{family}Visibility";
        var computedClass = $"{family}Computed";

        var routes = RouteTable.Build(doc);
        var validation = ValidationTable.Build(doc);

        // Which fields a page validates, so the model emits an error property and the page a
        // place to show it.
        var validatedFields = validation.Rules
            .Where(r => r.FullyParsed)
            .Select(r => r.Field)
            .ToHashSet(StringComparer.Ordinal);

        // Buttons the template actually routes from. The rest keep their empty seam.
        var routedActions = routes.Routes
            .Select(r => r.SourceField)
            .ToHashSet(StringComparer.Ordinal);

        // Every page is built before any is written, because visibility cannot be decided one
        // page at a time: a guard on this page routinely reads a field on another, and the
        // predicate needs every field resolvable before it can say which are safe.
        var models = pages
            .Select(node =>
            {
                var built = PageEmitModelBuilder.Build(doc, node, vocabulary, request.Prefix);
                return built with
                {
                    Validated = built.AllFields
                        .Where(f => f.PropertyName is not null && validatedFields.Contains(f.Source.Label))
                        .Select(f => f.PropertyName!)
                        .ToHashSet(StringComparer.Ordinal)
                };
            })
            .ToList();

        var state = VisibilityEmitter.Emit(
            doc, models, VisibilityTable.Build(doc), request.RootNamespace, visibilityClass, reportClass);

        for (var i = 0; i < models.Count; i++)
            models[i] = models[i] with { StateNames = VisibilityEmitter.StateNames(models[i], state) };

        var written = new List<string>();
        var converted = new List<ConvertedPage>();

        foreach (var model in models)
        {
            converted.Add(EmitPage(
                model, request, keys, written,
                reportClass, navigatorClass, validatorClass, routedActions, visibilityClass, computedClass));
        }

        var result = new ConversionResult
        {
            Document = doc,
            Pages = converted,
            Duration = started.Elapsed,
            ReportClass = reportClass,
            NavigatorClass = navigatorClass,
            ValidatorClass = validatorClass,
            VisibilityClass = visibilityClass,
            ComputedClass = computedClass,
            RulesTotal = validation.Rules.Count,
            ShownRules = state.Shown.Count,
            UsableRules = state.Usable.Count,
            StateLeftToAPerson = state.LeftToAPerson.Count,
        };

        // Routes, registrations, the report and the tables are only worth writing when the
        // whole template is emitted: a single page would otherwise unregister its siblings.
        if (!wholeTemplate || converted.Any(p => !p.Verified) || request.DryRun)
            return result;

        var routesClass = request.Prefix.Length > 0 ? $"{request.Prefix}FormRoutes" : $"{family}Routes";

        Write(written, app, "Views", $"{routesClass}.g.cs",
            RoutesEmitter.EmitRegistrations(doc, models, request.RootNamespace, routesClass, reportClass, doc.FolderName));

        Write(written, app, "Models", $"{reportClass}.g.cs",
            ReportEmitter.Emit(doc, models, request.RootNamespace, reportClass, vocabulary));

        var navigator = NavigatorEmitter.Emit(
            doc, models, routes, request.RootNamespace, navigatorClass, reportClass);

        var validator = ValidatorEmitter.Emit(
            doc, models, validation, request.RootNamespace, validatorClass, reportClass, visibilityClass);

        var computed = ComputedEmitter.Emit(
            doc, models, ComputedValueTable.Build(doc), state.Cleared,
            request.RootNamespace, computedClass, reportClass);

        Write(written, app, "Views", $"{validatorClass}.g.cs", validator.Code);
        Write(written, app, "Views", $"{visibilityClass}.g.cs", state.Code);
        Write(written, app, "Views", $"{computedClass}.g.cs", computed.Code);
        Write(written, app, "Views", $"{navigatorClass}.g.cs", navigator.Code);

        var remaining = Write(written, app, "Views", $"{family}.Remaining.md",
            RemainingWorkEmitter.Emit(doc, validation, validator, state, computed, models));

        return result with
        {
            FilesWritten = written,
            Duration = started.Elapsed,
            RoutesClass = routesClass,
            EntryRoute = $"//{RoutesEmitter.Route(models[0].ClassName)}",
            RemainingWorkPath = remaining,
            RoutesEmitted = routes.Routes.Count,
            RulesEmitted = validator.RulesEmitted,
            MessagesNeedingWording = validator.Wording.Distinct().Count(),
            ValuesFilledIn = computed.Applied,
            ValuesLeftToAPerson = computed.LeftToAPerson.Count,
            Unroutable = navigator.Unroutable.Select(u => (u.SourceField, u.Detail)).ToList(),
        };
    }

    private static ConvertedPage EmitPage(
        PageEmitModel model, ConversionRequest request, IReadOnlySet<string> keys, List<string> written,
        string reportClass, string navigatorClass, string validatorClass, IReadOnlySet<string> routedActions,
        string visibilityClass, string computedClass)
    {
        var ns = request.RootNamespace;

        var xaml = XamlEmitter.EmitPage(model, ns);
        var codeBehind = XamlEmitter.EmitCodeBehind(model, ns);
        var answers = ModelEmitter.EmitAnswers(model, ns, model.Validated);
        var viewModel = ModelEmitter.EmitViewModel(
            model, ns, reportClass, navigatorClass, validatorClass, routedActions, visibilityClass, computedClass);

        var problems = EmitVerifier.Verify(model, xaml, answers, keys);

        var page = new ConvertedPage(
            model.PageName, model.ClassName, model.AllFields.Count, model.Rows.Count,
            model.Skipped.ToList(), problems, xaml);

        if (problems.Count > 0 || request.DryRun) return page;

        var app = request.AppDirectory;
        Write(written, app, "Views", $"{model.ClassName}.xaml", xaml);
        Write(written, app, "Views", $"{model.ClassName}.xaml.cs", codeBehind);
        Write(written, app, "Models", $"{model.AnswersClassName}.g.cs", answers);
        Write(written, app, "ViewModels", $"{model.ClassName}ViewModel.g.cs", viewModel);

        return page;
    }

    private static string Write(List<string> written, string app, string area, string fileName, string content)
    {
        var path = Path.Combine(app, area, "Generated", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        written.Add(path);
        return path;
    }
}
