using System.Text;
using static FormWorks.Templates.Emit.PageEmitModelBuilder;

namespace FormWorks.Templates.Emit;

/// <summary>Writes the answers class and the page view model.</summary>
public static class ModelEmitter
{
    public static string EmitAnswers(
        PageEmitModel page, HouseStyle style, IReadOnlySet<string>? validated = null)
    {
        var sb = new StringBuilder();
        Header(sb, page);

        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine("using CommunityToolkit.Mvvm.ComponentModel;");
        sb.AppendLine();
        sb.AppendLine($"namespace {style.ModelsNamespace};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Answers for the {page.PageName} page of {Escape(page.Document.FolderName)}.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public partial class {page.AnswersClassName} : ObservableObject");
        sb.AppendLine("{");

        foreach (var field in page.AllFields)
        {
            var mapping = field.Mapping!;
            var name = field.PropertyName!;
            var backing = "_" + char.ToLowerInvariant(name[0]) + name[1..];
            var init = mapping.DefaultExpression is { } d ? $" = {d}" : "";

            sb.AppendLine();
            sb.AppendLine($"    /// <summary>{Escape(field.Title)} — {Escape(field.Source.Label)}</summary>");
            sb.AppendLine($"    private {mapping.ClrType} {backing}{init};");
            sb.AppendLine($"    public {mapping.ClrType} {name}");
            sb.AppendLine("    {");
            sb.AppendLine($"        get => {backing};");
            sb.AppendLine($"        set => SetProperty(ref {backing}, value);");
            sb.AppendLine("    }");

            if (validated?.Contains(name) == true)
            {
                var errorBacking = backing + "Error";

                sb.AppendLine();
                sb.AppendLine($"    /// <summary>Why {Escape(field.Title)} is not valid, or null when it is.</summary>");
                sb.AppendLine($"    private string? {errorBacking};");
                sb.AppendLine($"    public string? {name}Error");
                sb.AppendLine("    {");
                sb.AppendLine($"        get => {errorBacking};");
                sb.AppendLine($"        set => SetProperty(ref {errorBacking}, value);");
                sb.AppendLine("    }");
            }

            if (field.Options.Count > 0)
            {
                // Options are fixed by the template, so they are not answers and must not
                // travel through save and resume.
                sb.AppendLine();
                sb.AppendLine("    [JsonIgnore]");
                sb.AppendLine($"    public IReadOnlyList<string> {name}Options {{ get; }} = new[]");
                sb.AppendLine("    {");
                foreach (var option in field.Options)
                    sb.AppendLine($"        \"{option.Replace("\\", "\\\\").Replace("\"", "\\\"")}\",");
                sb.AppendLine("    };");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// The name the template knows each of this page's answers by.
    ///
    /// The bindings work in generated property names and the template's own rules work in
    /// FormWorks names, so something has to hold the pairing. It is generated rather than
    /// derived at runtime so the compiler sees every name.
    /// </summary>
    private static void EmitNameMap(StringBuilder sb, PageEmitModel page, string? computedClassName)
    {
        if (computedClassName is null) return;

        var fields = page.AllFields.Where(f => f.PropertyName is not null).ToList();
        if (fields.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine("    /// <summary>What the template calls one of this page's answers.</summary>");
        sb.AppendLine("    private static string? FormWorksName(string? property) => property switch");
        sb.AppendLine("    {");

        foreach (var field in fields.DistinctBy(f => f.PropertyName, StringComparer.Ordinal))
        {
            sb.AppendLine(
                $"        \"{Escape(field.PropertyName!)}\" => \"{Escape(field.Source.Label)}\",");
        }

        sb.AppendLine("        _ => null");
        sb.AppendLine("    };");
    }

    /// <summary>
    /// A property per element whose shown or usable state the template decides.
    ///
    /// A property rather than a call from the XAML, because bindings cannot call methods.
    /// One per element rather than a dictionary lookup, so the compiler checks the name and
    /// a developer can put a breakpoint on the element that is wrongly hidden.
    /// </summary>
    private static void EmitStateProperties(
        StringBuilder sb, PageEmitModel page, string? visibilityClassName)
    {
        if (visibilityClassName is null || page.StateNames.Count == 0) return;

        var titles = page.AllFields
            .GroupBy(f => f.Source.Label, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Title, StringComparer.Ordinal);

        foreach (var (label, binding) in page.StateNames.OrderBy(n => n.Value.Name, StringComparer.Ordinal))
        {
            var what = titles.GetValueOrDefault(label) is { Length: > 0 } t ? t : binding.Key;

            if (binding.Shown)
            {
                sb.AppendLine($"    /// <summary>Whether {Escape(what)} is asked at all.</summary>");
                sb.AppendLine(
                    $"    public bool Show{binding.Name} => "
                    + $"{visibilityClassName}.IsShown(Report, \"{Escape(binding.Key)}\");");
                sb.AppendLine();
            }
            else if (binding.UndecidedShown)
            {
                Seam(sb, "Show", binding.Name, what, "shown", binding.StartsHidden);
            }

            if (binding.Usable)
            {
                sb.AppendLine($"    /// <summary>Whether {Escape(what)} can be answered yet.</summary>");
                sb.AppendLine(
                    $"    public bool Usable{binding.Name} => "
                    + $"{visibilityClassName}.IsUsable(Report, \"{Escape(binding.Key)}\");");
                sb.AppendLine();
            }
            else if (binding.UndecidedUsable)
            {
                // Usable, not shown: a field can be on screen and not answerable, and a
                // refused enablement rule asked on the wrong property would hide it.
                Seam(sb, "Usable", binding.Name, what, "usable", startsHidden: false);
            }
        }
    }

    /// <summary>
    /// Writes down what a person decided about this page's fields.
    ///
    /// The page asks these properties and the generated rules ask the report, so without
    /// this the two disagree: a section would vanish from the screen while its answers
    /// stayed in the report and its rules kept firing against them.
    /// </summary>
    private static void EmitRecordDecisions(StringBuilder sb, PageEmitModel page)
    {
        var decided = page.StateNames
            .Where(b => b.Value.UndecidedShown || b.Value.UndecidedUsable)
            .OrderBy(b => b.Value.Name, StringComparer.Ordinal)
            .ToList();

        sb.AppendLine();
        sb.AppendLine("    /// <summary>Records what this page decides for itself, so the rules see it.</summary>");
        sb.AppendLine("    private void RecordDecisions()");
        sb.AppendLine("    {");

        if (decided.Count == 0)
        {
            sb.AppendLine("        // Nothing on this page is left to a person.");
        }

        foreach (var (label, binding) in decided)
        {
            if (binding.UndecidedShown)
                sb.AppendLine($"        Report.Decided[\"{Escape(label)}\"] = Show{binding.Name};");

            if (binding.UndecidedUsable)
                sb.AppendLine($"        Report.Decided[\"{Escape(label)}\"] = Usable{binding.Name};");
        }

        sb.AppendLine("    }");
    }

    /// <summary>
    /// Somewhere to write a rule the generator refused.
    ///
    /// It defaults to the state the template gave the element, and asks the hand-written
    /// half to say otherwise. Defaulting to shown regardless would be wrong for an element
    /// the template starts hidden: that reveals something the form was never going to show,
    /// and the estate hides sections holding reference data the agent must not edit. A
    /// partial method nobody implements compiles away, so the default costs nothing.
    /// </summary>
    private static void Seam(
        StringBuilder sb, string prefix, string name, string what, string verb, bool startsHidden)
    {
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// Whether {Escape(what)} is {verb}. Left to a person: see the template's");
        sb.AppendLine($"    /// Remaining.md for why, and implement Decide{prefix}{name} to decide it.");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public bool {prefix}{name}");
        sb.AppendLine("    {");
        sb.AppendLine("        get");
        sb.AppendLine("        {");
        sb.AppendLine($"            var {verb} = {(startsHidden ? "false" : "true")};");
        sb.AppendLine($"            Decide{prefix}{name}(ref {verb});");
        sb.AppendLine($"            return {verb};");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    partial void Decide{prefix}{name}(ref bool {verb});");
        sb.AppendLine();
    }

    public static string EmitViewModel(
        PageEmitModel page, HouseStyle style, string reportClassName, string navigatorClassName,
        string validatorClassName, string routesClassName, IReadOnlySet<string> routedActions,
        string? visibilityClassName = null,
        string? computedClassName = null)
    {
        var sb = new StringBuilder();
        Header(sb, page);

        sb.AppendLine("using System.Collections.ObjectModel;");
        sb.AppendLine("using CommunityToolkit.Mvvm.ComponentModel;");
        if (style.ViewModelBaseClassNamespace is not null)
            sb.AppendLine($"using {style.ViewModelBaseClassNamespace};");
        sb.AppendLine($"using {style.ModelsNamespace};");
        // The type alone, not the namespace: the POC's own answer classes live there and
        // share names with the generated ones.
        sb.AppendLine($"using FormStep = {style.JobFormsNamespace}.FormStep;");
        sb.AppendLine($"using {style.ServicesNamespace};");
        sb.AppendLine($"using {style.ViewsNamespace};");
        sb.AppendLine();
        sb.AppendLine($"namespace {style.ViewModelsNamespace};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Generated half of the page's view model. Hand-written logic belongs in the other");
        sb.AppendLine("/// half of this partial class, in a file the generator never touches.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public partial class {page.ClassName}ViewModel : {style.ViewModelBaseClass}");
        sb.AppendLine("{");
        sb.AppendLine($"    private readonly IFormStore<{reportClassName}> _store;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// This page's slice of the report. Not a copy: the report outlives the view model,");
        sb.AppendLine("    /// which is rebuilt on every visit.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public {page.AnswersClassName} Answers => _store.Current.{page.PageName};");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The whole report, so a page can read form-level state.</summary>");
        sb.AppendLine($"    public {reportClassName} Report => _store.Current;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// The step strip. Built from the same routing the buttons use, so the two cannot");
        sb.AppendLine("    /// disagree about which pages this report visits.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public ObservableCollection<FormStep> Steps { get; } = [];");
        sb.AppendLine();
        sb.AppendLine("    public Command GoToStepCommand { get; }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Runs every page's rules against the current report and arms their messages,");
        sb.AppendLine("    /// so a person testing the conversion can see the whole report's validation state");
        sb.AppendLine("    /// without wiring up submission first. Bind a house control's validate affordance");
        sb.AppendLine($"    /// to it, or call {routesClassName}.ValidateAll directly once submission exists.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public Command ValidateAllCommand { get; }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Re-applies this page's rules. Run whenever an answer changes, because a rule");
        sb.AppendLine("    /// can read a field other than the one just edited.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public void Validate()");
        sb.AppendLine("    {");

        if (visibilityClassName is not null)
        {
            sb.AppendLine("        // What a person decided, written down before anything reads it. The rules");
            sb.AppendLine("        // and the clearing below ask the report, not this class, so a decision made");
            sb.AppendLine("        // here has to be there before they run.");
            sb.AppendLine("        RecordDecisions();");
            sb.AppendLine();
        }


        if (visibilityClassName is not null && page.StateNames.Count > 0)
        {
            sb.AppendLine("        // A question no longer being asked has no answer. FormWorks kept the value,");
            sb.AppendLine("        // which left an answer given and then hidden still in the outbound payload.");
            sb.AppendLine($"        {visibilityClassName}.ClearHidden(_store.Current);");
        }

        sb.AppendLine($"        {validatorClassName}.Validate{page.PageName}(_store.Current);");
        sb.AppendLine("        OnValidated();");
        sb.AppendLine();
        sb.AppendLine("        // Any answer can change which fields are shown, and a field shown by a rule");
        sb.AppendLine("        // reading another page's answer changes without this page's answers changing");
        sb.AppendLine("        // at all. Raising for everything is one line and cannot miss one.");
        sb.AppendLine("        OnPropertyChanged(string.Empty);");
        sb.AppendLine("    }");
        sb.AppendLine();

        EmitStateProperties(sb, page, visibilityClassName);

        if (visibilityClassName is not null) EmitRecordDecisions(sb, page);

        EmitNameMap(sb, page, computedClassName);
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Where rules the generator refused to express are written, in the hand-written");
        sb.AppendLine("    /// half of this class. Runs after the generated rules, so it can overwrite any");
        sb.AppendLine("    /// message they set. Listed per template in the page's Remaining.md.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    partial void OnValidated();");
        sb.AppendLine();

        if (computedClassName is not null)
        {
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Where answers the form fills in are written, in the hand-written half of this");
            sb.AppendLine("    /// class. Listed per template in the page's Remaining.md.");
            sb.AppendLine("    ///");
            sb.AppendLine("    /// Runs when an answer changes, before the rules read it, and is told which");
            sb.AppendLine("    /// field was edited by the name the template knows it by. That matters: filling");
            sb.AppendLine("    /// in an answer on every change would overwrite the agent as they typed, where");
            sb.AppendLine("    /// the template acts once, when the field it watches changes.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    /// <param name=\"field\">");
            sb.AppendLine("    /// The FormWorks name of the answer just edited, or null where it is not one of");
            sb.AppendLine("    /// this page's.");
            sb.AppendLine("    /// </param>");
            sb.AppendLine("    partial void OnAnswerChanged(string? field);");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// The agent has finished with a field, which is when a form does what its");
            sb.AppendLine("    /// OnBlur handlers do. Called by the page, not by a change in the answer.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    /// <remarks>");
            sb.AppendLine("    /// It does not re-run the rules. Anything either of these fills in is an answer,");
            sb.AppendLine("    /// and setting an answer raises a change that runs them already. Leaving a");
            sb.AppendLine("    /// field without changing anything cannot change what the rules decide.");
            sb.AppendLine("    /// </remarks>");
            sb.AppendLine("    public void AnswerLeft(string? property)");
            sb.AppendLine("    {");
            sb.AppendLine("        var field = FormWorksName(property);");
            sb.AppendLine();
            sb.AppendLine($"        {computedClassName}.ApplyOnLeaving(_store.Current, field);");
            sb.AppendLine("        OnAnswerLeft(field);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Where answers the form fills in on leaving a field are written, in the");
            sb.AppendLine("    /// hand-written half of this class.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    partial void OnAnswerLeft(string? field);");
        }
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Rebuilt whenever the page appears, because an answer given on a later page can");
        sb.AppendLine("    /// change which pages come before it.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public void RefreshSteps()");
        sb.AppendLine("    {");
        sb.AppendLine("        Steps.Clear();");
        sb.AppendLine();
        sb.AppendLine($"        foreach (var (title, route) in {navigatorClassName}.Path(_store.Current))");
        sb.AppendLine("        {");
        sb.AppendLine("            Steps.Add(new FormStep");
        sb.AppendLine("            {");
        sb.AppendLine("                Title = title,");
        sb.AppendLine($"                ShortTitle = $\"{{Steps.Count + 1}}. {{title}}\",");
        sb.AppendLine("                Route = route,");
        sb.AppendLine($"                IsCurrent = route == \"//{RoutesEmitter.Route(page.ClassName)}\",");
        sb.AppendLine("                IsEnabled = true");
        sb.AppendLine("            });");
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        foreach (var action in page.Actions)
        {
            var name = action.CommandName!;
            var hook = "On" + name[..^"Command".Length];

            sb.AppendLine();
            sb.AppendLine($"    /// <summary>{Escape(action.Title)} — {Escape(action.Source.Label)}</summary>");
            sb.AppendLine($"    public Command {name} {{ get; }}");

            if (!routedActions.Contains(action.Source.Label))
            {
                sb.AppendLine();
                sb.AppendLine("    /// <summary>");
                sb.AppendLine($"    /// What {Escape(action.Title)} does. The template gives it no route, so this is");
                sb.AppendLine("    /// empty; a partial method nobody implements compiles away to nothing rather");
                sb.AppendLine("    /// than throwing when the button is tapped.");
                sb.AppendLine("    /// </summary>");
                sb.AppendLine($"    partial void {hook}();");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"    public {page.ClassName}ViewModel(IFormStore<{reportClassName}> store, IServiceProvider services)");
        sb.AppendLine("    {");
        sb.AppendLine("        _store = store;");
        sb.AppendLine("        // Stands in for a real submit until one exists: validate, and only print");
        sb.AppendLine("        // the report - what a real submit would send - once nothing is wrong with it.");
        sb.AppendLine("        // A field can be invalid on a page nobody is currently looking at, so failure");
        sb.AppendLine("        // is not left to a red message the agent might not be on the right page to see.");
        sb.AppendLine("        ValidateAllCommand = new Command(async () =>");
        sb.AppendLine("        {");
        sb.AppendLine($"            if ({routesClassName}.ValidateAll(services))");
        sb.AppendLine("            {");
        sb.AppendLine("                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(");
        sb.AppendLine("                    _store.Current, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));");
        sb.AppendLine("                return;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine($"            var invalid = {validatorClassName}.Invalid(_store.Current);");
        sb.AppendLine("            var summary = string.Join(\"\\n\", invalid.Select(i => $\"{i.Field}: {i.Message}\"));");
        sb.AppendLine("            var page = Application.Current?.Windows.FirstOrDefault()?.Page;");
        sb.AppendLine();
        sb.AppendLine("            if (page is not null)");
        sb.AppendLine("                await page.DisplayAlertAsync(\"Not ready to submit\", summary, \"OK\");");
        sb.AppendLine("        });");
        sb.AppendLine();
        sb.AppendLine("        // A rule can read any field, so any change can change any message.");
        sb.AppendLine("        Answers.PropertyChanged += (_, e) =>");
        sb.AppendLine("        {");
        sb.AppendLine("            if (e.PropertyName?.EndsWith(\"Error\", StringComparison.Ordinal) == true)");
        sb.AppendLine("                return;");
        sb.AppendLine();

        if (computedClassName is not null)
        {
            sb.AppendLine("            // What the form fills in, before the rules read it. Which field was");
            sb.AppendLine("            // edited matters: most of it is a reaction to one field changing, and");
            sb.AppendLine("            // running it continuously would overwrite the agent as they typed.");
            sb.AppendLine($"            {computedClassName}.Apply(_store.Current, FormWorksName(e.PropertyName));");
            sb.AppendLine("            OnAnswerChanged(FormWorksName(e.PropertyName));");
            sb.AppendLine();
        }

        sb.AppendLine("            Validate();");
        sb.AppendLine("        };");
        sb.AppendLine("        GoToStepCommand = new Command(async p => await GoAsync((p as FormStep)?.Route));");

        foreach (var action in page.Actions)
        {
            var name = action.CommandName!;
            var hook = "On" + name[..^"Command".Length];

            if (routedActions.Contains(action.Source.Label))
            {
                var method = NavigatorEmitter.MethodName(action.Source.Label);
                // Moving between pages does not arm the messages. FormWorks armed them at a
                // submission trigger, and a Next that armed them would leave every later
                // page red for the rest of the visit. Submission will arm them when it
                // exists; until then the flag stays off and the rules run quietly.
                sb.AppendLine($"        {name} = new Command(async () =>");
                sb.AppendLine("        {");
                sb.AppendLine("            Validate();");
                sb.AppendLine($"            await GoAsync({navigatorClassName}.{method}(_store.Current));");
                sb.AppendLine("        });");
            }
            else
            {
                sb.AppendLine($"        {name} = new Command(() => {hook}());");
            }
        }

        sb.AppendLine("    }");

        {
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// A null route means no branch matched, which is what the template does when an");
            sb.AppendLine("    /// agent taps Next without answering the question the routing turns on. Going");
            sb.AppendLine("    /// nowhere is the template's behaviour; inventing a destination would not be.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    private static async Task GoAsync(string? route)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!string.IsNullOrEmpty(route))");
            sb.AppendLine("            await Shell.Current.GoToAsync(route);");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// The answers property on any owning class needs a setter. System.Text.Json skips
    /// read-only properties of complex type when deserialising, so a get-only section
    /// saves correctly and comes back empty with no error anywhere.
    /// </summary>
    private static void Header(StringBuilder sb, PageEmitModel page)
    {
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"//     Generated from {page.Document.FolderName}, page {page.PageName}.");
        sb.AppendLine("//     Do not edit. Regenerate instead; edits here are lost.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
    }

    private static string Escape(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
               .ReplaceLineEndings(" ").Trim();
}
