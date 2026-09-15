using FormWorks.Templates.Analysis;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Model;
using FormWorks.Templates.Parsing;

namespace FormWorks.Cli;

/// <summary>
/// Phase one is a reader, so every command here answers a question about the estate
/// rather than producing code. The order they were written in is the order the
/// generator needs the answers.
/// </summary>
public static class Commands
{
    public static int Help()
    {
        Console.WriteLine("""
            formworks — reads the FormWorks template estate.

            Commands
              list                       every template, with family and version
              check                      parse everything; report failures and surprises
              inventory  [--template T]  what a template is made of
              graph      --template T    pages, routing edges, unreachable pages
              routes     [--template T]  routing calls with the conditions that guard them
              coverage   [--template T]  branches checked against option lists
              validation [--template T]  what makes each field invalid, and what it says
              prefixes   [--field F]     the reference-prefix rule catalogue
              shapes     [--event E]     handlers grouped by shape, most repeated first
              repeats    --template T    field names differing only by an index
              generate   --template T --app DIR --namespace N [--all-pages | --page P]
                         [--isolate]
                                         emit a template: pages, answers, view models,
                                         the report, the navigator and the registrations

            Options
              --estate <path>   template folder (default: the nearest templates/ folder)
              --latest          only the highest version of each family
              --top <n>         limit long listings (default 20)
              --full            print whole script bodies rather than a first line
              --isolate         name generated classes after the template, so an app can
                                hold several without their pages colliding
            """);
        return 0;
    }

    private static List<TemplateDocument> Load(Args args, out List<(string Folder, Exception Error)> failed)
    {
        var root = EstateLocator.Resolve(args.Value("estate"));
        var (loaded, failures) = EstateLocator.LoadAll(root);
        failed = failures;

        var wanted = args.Value("template");
        if (wanted is not null)
        {
            var matches = loaded
                .Where(d => d.FolderName.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                throw new ArgumentException($"No template matches '{wanted}'. Try `formworks list`.");

            // An exact folder name always wins, so "My example template V23" does not
            // also drag in V22.
            var exact = matches.FirstOrDefault(d =>
                d.FolderName.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            return exact is not null ? [exact] : matches;
        }

        return args.Has("latest") ? EstateLocator.LatestPerFamily(loaded).ToList() : loaded;
    }

    public static int List(Args args)
    {
        var docs = Load(args, out _);
        var latest = EstateLocator.LatestPerFamily(docs).Select(d => d.FolderName).ToHashSet(StringComparer.Ordinal);

        foreach (var d in docs.OrderBy(d => d.Family, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Version))
            Console.WriteLine($"{(latest.Contains(d.FolderName) ? "*" : " ")} {d.FolderName}");

        Console.WriteLine();
        Console.WriteLine($"{docs.Count} templates, {latest.Count} families (* = latest version)");
        return 0;
    }

    public static int Check(Args args)
    {
        var docs = Load(args, out var failed);

        foreach (var (folder, error) in failed)
            Console.WriteLine($"FAILED  {folder}: {error.Message}");

        var unknown = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var mismatches = new List<string>();
        var nodes = 0;
        var scripts = 0;

        foreach (var doc in docs)
        {
            var report = FormWorks.Templates.Analysis.Inventory.Build(doc);
            nodes += report.NodeCount;
            scripts += report.ScriptCount;

            foreach (var t in report.UnknownFieldTypes)
            {
                if (!unknown.TryGetValue(t, out var list)) unknown[t] = list = [];
                list.Add(doc.FolderName);
            }
            mismatches.AddRange(report.FieldTypeMismatches.Select(m => $"{doc.FolderName}: {m}"));
        }

        Console.WriteLine($"parsed   {docs.Count} templates, {nodes} nodes, {scripts} script handlers");
        Console.WriteLine($"failed   {failed.Count}");

        // An unrecognised node type is the failure mode that matters: it means a control
        // exists in the estate that the converter has no plan for.
        Console.WriteLine($"unknown  {unknown.Count} node type(s)");
        foreach (var (type, templates) in unknown)
            Console.WriteLine($"         {type} — in {templates.Count} template(s), e.g. {templates[0]}");

        Console.WriteLine($"mismatch {mismatches.Count} wrapper/declared field type disagreement(s)");
        foreach (var m in mismatches.Take(args.Int("top", 20)))
            Console.WriteLine($"         {m}");

        return failed.Count == 0 && unknown.Count == 0 ? 0 : 1;
    }

    public static int Inventory(Args args)
    {
        foreach (var doc in Load(args, out _))
        {
            var r = FormWorks.Templates.Analysis.Inventory.Build(doc);
            Console.WriteLine($"== {r.Template}");
            Console.WriteLine($"   {r.NodeCount} nodes, {r.PageCount} pages, depth {r.MaxDepth}, {r.ScriptCount} handlers");
            Console.WriteLine("   controls: " + string.Join(", ", r.FieldTypes.Select(f => $"{f.FieldType} {f.Count}")));
            Console.WriteLine("   scripts:  " + string.Join(", ", r.ScriptEvents.Select(e => $"{e.Event} {e.Count}")));
            if (r.UnknownFieldTypes.Count > 0)
                Console.WriteLine("   UNKNOWN:  " + string.Join(", ", r.UnknownFieldTypes));
            Console.WriteLine();
        }
        return 0;
    }

    public static int Graph(Args args)
    {
        foreach (var doc in Load(args, out _))
        {
            var g = PageGraph.Build(doc);
            Console.WriteLine($"== {g.Template}");
            Console.WriteLine($"   {g.Pages.Count} pages, {g.Edges.Count} routing calls in {g.HandlerCount} handlers");
            Console.WriteLine($"   {g.MixedHandlerCount} handler(s) also set visibility or enablement");
            Console.WriteLine($"   entry: {g.EntryPage}");
            Console.WriteLine();

            Console.WriteLine("   edges");
            foreach (var e in g.Edges)
                Console.WriteLine($"     {e.FromPage,-28} -> {e.ToPage,-26} [{e.Event}] {e.SourceField}{(e.MixedWithState ? "  (mixed)" : "")}");

            if (g.UnreachablePages.Count > 0)
                Console.WriteLine("\n   UNREACHABLE pages: " + string.Join(", ", g.UnreachablePages));
            if (g.MissingTargets.Count > 0)
                Console.WriteLine("   MISSING targets:   " + string.Join(", ", g.MissingTargets));

            Console.WriteLine("\n   handler shapes, most repeated first");
            foreach (var (shape, handlers) in g.HandlerShapes.Take(args.Int("top", 20)))
                Console.WriteLine($"     {handlers.Count,3}x  {Trim(shape, args.Has("full"))}");

            Console.WriteLine();
        }
        return 0;
    }

    public static int Routes(Args args)
    {
        var totalCalls = 0; var totalRoutes = 0; var emittable = 0;
        var unparsed = new List<string>();

        foreach (var doc in Load(args, out _))
        {
            var r = RouteTable.Build(doc);
            totalCalls += r.ChangePageCalls;
            totalRoutes += r.Routes.Count;
            emittable += r.Emittable.Count();
            unparsed.AddRange(r.UnparsedHandlers.Select(u => $"{doc.FolderName}: {u}"));

            if (!args.Has("summary"))
            {
                Console.WriteLine($"== {r.Template}");
                Console.WriteLine($"   {r.ChangePageCalls} calls, {r.Routes.Count} guarded routes, "
                                  + $"{r.Emittable.Count()} fully decomposed ({r.EmittableShare:P0})");
                Console.WriteLine("   order is significant: the first matching route wins, as in the source chain");
                Console.WriteLine();

                foreach (var route in r.Routes)
                {
                    var text = args.Has("full") ? route.DescribeFull() : route.Describe();
                    Console.WriteLine($"   {route.FromPage,-26} -> {route.ToPage,-26} {(route.FullyParsed ? " " : "?")} {Trim(text, false)}");
                }
                Console.WriteLine();
            }
        }

        Console.WriteLine($"== {totalCalls} routing calls, {totalRoutes} recovered with a guard, "
                          + $"{emittable} fully decomposed into atoms");
        Console.WriteLine($"   {unparsed.Count} handler(s) whose structure did not come apart");
        foreach (var u in unparsed.Take(args.Int("top", 20)))
            Console.WriteLine($"     {u}");

        return unparsed.Count == 0 ? 0 : 1;
    }

    public static int Generate(Args args)
    {
        var doc = Load(args, out _).Single();
        var app = args.Value("app") ?? throw new ArgumentException("Pass --app <path to the MAUI project>.");

        var result = TemplateConverter.Convert(new ConversionRequest
        {
            Document = doc,
            AppDirectory = app,
            RootNamespace = args.Value("namespace")
                            ?? throw new ArgumentException("Pass --namespace <the app's root namespace>."),
            Prefix = args.Value("prefix")
                     ?? (args.Has("isolate") ? TemplateConverter.PrefixFor(doc) : ""),
            Pages = args.Has("all-pages") ? [] : [args.Value("page") ?? ""],
            DryRun = args.Has("dry-run")
        });

        Report(result, args);
        return result.Succeeded ? 0 : 1;
    }

    /// <summary>
    /// Prints what the conversion did. The numbers all come off the result, so this and the
    /// app's own conversion screen report the same run the same way.
    /// </summary>
    private static void Report(ConversionResult result, Args args)
    {
        var top = args.Int("top", 20);

        foreach (var page in result.Pages)
        {
            Console.WriteLine($"== {result.Document.FolderName} / {page.PageName}");
            Console.WriteLine($"   {page.FieldCount} fields bound, {page.RowCount} top-level rows");

            if (page.Skipped.Count > 0)
            {
                Console.WriteLine($"   {page.Skipped.Count} element(s) not emitted:");
                foreach (var skipped in page.Skipped.Take(top))
                    Console.WriteLine($"     {skipped}");
            }

            if (!page.Verified)
            {
                Console.WriteLine($"   {page.Problems.Count} PROBLEM(S) — nothing written:");
                foreach (var problem in page.Problems)
                    Console.WriteLine($"     [{problem.Kind}] {problem.Detail}");
                continue;
            }

            Console.WriteLine("   verification passed: bindings resolve, resource keys exist, every answer has a setter");
            if (args.Has("dry-run") && args.Has("print")) Console.WriteLine(page.Xaml);
        }

        foreach (var path in result.FilesWritten)
            Console.WriteLine($"   wrote {path}");

        if (result.RoutesClass is { } routesClass)
        {
            Console.WriteLine();
            Console.WriteLine($"   routes: {routesClass}.RegisterShellContent() and .AddGeneratedPages()");
            Console.WriteLine($"   entry:  {routesClass}.EntryRoute is \"{result.EntryRoute}\"");
            Console.WriteLine($"   values: {result.ValuesFilledIn} filled in by {result.ComputedClass}"
                              + $"; {result.ValuesLeftToAPerson} left to a person");
            Console.WriteLine($"   report: {result.ReportClass}, one property per page");
            Console.WriteLine($"   routes: {result.RoutesEmitted} emitted as typed C# in {result.NavigatorClass}");
            Console.WriteLine($"   state:  {result.ShownRules} shown and {result.UsableRules} usable rule(s) in {result.VisibilityClass}"
                              + $"; {result.StateLeftToAPerson} left to a person");
            Console.WriteLine($"   rules:  {result.RulesEmitted} of {result.RulesTotal} emitted in {result.ValidatorClass}"
                              + (result.MessagesNeedingWording > 0
                                  ? $"; {result.MessagesNeedingWording} message(s) needing wording"
                                  : ""));

            if (result.Unroutable.Count > 0)
            {
                Console.WriteLine($"   {result.Unroutable.Count} guard(s) could not be expressed:");
                foreach (var u in result.Unroutable.Take(args.Int("top", 10)))
                    Console.WriteLine($"     {u.SourceField}: {u.Detail}");
            }
        }

        if (result.Pages.Count > 1)
        {
            Console.WriteLine();
            Console.WriteLine($"== {result.Pages.Count} pages, {result.FieldsBound} fields bound, {result.Failures} failed, "
                              + $"in {result.Duration.TotalSeconds:F2}s");
        }
    }

    public static int Validation(Args args)
    {
        var total = 0; var expressible = 0; var handlers = 0; var unrecovered = 0;
        var atoms = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var doc in Load(args, out _))
        {
            var report = ValidationTable.Build(doc);
            total += report.Rules.Count;
            expressible += report.Expressible.Count();
            handlers += report.HandlerCount;
            unrecovered += report.Unrecovered.Count;

            foreach (var guard in report.Rules.SelectMany(r => r.When).SelectMany(c => c.Atoms))
            {
                var kind = guard.IsPrefixTest ? "prefix" : guard.IsStateTest ? "state" : guard.IsLiteralBoolean ? "boolean" : "text";
                atoms[kind] = atoms.GetValueOrDefault(kind) + 1;
            }

            if (!args.Has("summary"))
            {
                Console.WriteLine($"== {report.Template}: {report.HandlerCount} handlers, {report.Rules.Count} rules");
                foreach (var rule in report.Rules.Take(args.Int("top", 20)))
                {
                    var when = rule.When.Count == 0 ? "(always)" : string.Join(" AND ", rule.When);
                    Console.WriteLine($"   {rule.Field}");
                    Console.WriteLine($"     invalid when {Trim(when, false)}");
                    Console.WriteLine($"     says {(rule.Message is null ? "(computed) " + rule.MessageExpression : "\"" + rule.Message + "\"")}");
                }
                Console.WriteLine();
            }
        }

        Console.WriteLine($"== {handlers} handlers, {total} rules, {expressible} expressible ({(total == 0 ? 0 : (double)expressible / total):P0})");
        Console.WriteLine($"   {unrecovered} handler(s) whose structure was not followed");
        Console.WriteLine("   atoms: " + string.Join(", ", atoms.OrderByDescending(a => a.Value).Select(a => $"{a.Key} {a.Value}")));
        return 0;
    }

    /// <summary>
    /// What is known about when fields are shown and when they are usable.
    ///
    /// Reports the shape of each field's state rather than a rule count, because the
    /// question for visibility is not how many conditions parse but whether a field's
    /// state can be reconstructed as a predicate at all.
    /// </summary>
    public static int Visibility(Args args)
    {
        var shapes = new Dictionary<StateShape, int>();
        var inversions = new Dictionary<Inversion, int>();
        var cross = new Dictionary<(Inversion, StateShape), int>();
        var byProperty = new Dictionary<string, int>(StringComparer.Ordinal);
        var sourceCounts = new List<int>();
        var unrecovered = 0;
        var fieldsTotal = 0;

        foreach (var doc in Load(args, out _))
        {
            var report = VisibilityTable.Build(doc);
            unrecovered += report.Unrecovered.Count;
            fieldsTotal += doc.AllNodes.Count();

            foreach (var field in report.Fields)
            {
                shapes[field.Shape] = shapes.GetValueOrDefault(field.Shape) + 1;
                inversions[field.Inversion] = inversions.GetValueOrDefault(field.Inversion) + 1;
                cross[(field.Inversion, field.Shape)] = cross.GetValueOrDefault((field.Inversion, field.Shape)) + 1;
                byProperty[field.Property] = byProperty.GetValueOrDefault(field.Property) + 1;
                sourceCounts.Add(field.Sources.Count());
            }

            if (!args.Has("summary"))
            {
                Console.WriteLine($"== {report.Template}: {report.Fields.Count} fields with written state");

                foreach (var field in report.Fields
                             .OrderByDescending(f => f.Sources.Count())
                             .Take(args.Int("top", 15)))
                {
                    Console.WriteLine(
                        $"   {field.Target}.{field.Property} [{field.Shape}]"
                        + $" {field.Writes.Count} write(s) from {field.Sources.Count()} handler(s)"
                        + (field.StartsHidden ? ", starts hidden" : ""));

                    foreach (var write in field.Writes.Take(4))
                    {
                        var when = write.IsUnconditional
                            ? "(always)"
                            : string.Join(" AND ", write.Own);
                        Console.WriteLine($"     = {write.Value.ToString().ToLowerInvariant()} when {Trim(when, false)}");
                    }
                }

                Console.WriteLine();
            }
        }

        var written = shapes.Values.Sum();
        Console.WriteLine($"== {written} field/property pairs written across {fieldsTotal} nodes");

        foreach (var shape in new[] { StateShape.SingleSource, StateShape.MultiSource, StateShape.Refused, StateShape.Static })
        {
            var count = shapes.GetValueOrDefault(shape);
            Console.WriteLine($"   {shape,-13} {count,5} ({(written == 0 ? 0 : (double)count / written):P0})");
        }

        Console.WriteLine();
        Console.WriteLine("   inversion x source        single   multi    total");
        foreach (var inversion in new[]
                 { Inversion.ResetThenShow, Inversion.GuardedBothWays, Inversion.ShowOnly, Inversion.HideOnly, Inversion.None })
        {
            var single = cross.GetValueOrDefault((inversion, StateShape.SingleSource));
            var multi = cross.GetValueOrDefault((inversion, StateShape.MultiSource));
            var total = cross.Where(c => c.Key.Item1 == inversion).Sum(c => c.Value);
            Console.WriteLine($"   {inversion,-20} {single,6} {multi,7} {total,8}");
        }

        Console.WriteLine();
        foreach (var inversion in new[]
                 { Inversion.ResetThenShow, Inversion.GuardedBothWays, Inversion.ShowOnly, Inversion.HideOnly, Inversion.None })
        {
            var count = inversions.GetValueOrDefault(inversion);
            Console.WriteLine($"   {inversion,-16} {count,5} ({(written == 0 ? 0 : (double)count / written):P0})");
        }

        Console.WriteLine();
        Console.WriteLine("   properties: " + string.Join(", ", byProperty.Select(p => $"{p.Key} {p.Value}")));

        if (sourceCounts.Count > 0)
            Console.WriteLine(
                $"   handlers per field: max {sourceCounts.Max()}, "
                + $"mean {sourceCounts.Average():F1}, "
                + $"{sourceCounts.Count(c => c == 1)} written from exactly one");

        Console.WriteLine($"   {unrecovered} handler(s) whose structure was not followed");
        return 0;
    }

    /// <summary>
    /// Answers the form fills in for the agent.
    ///
    /// The category that fails most quietly: a field the template would have filled is
    /// simply blank, which looks like a field nobody has reached yet.
    /// </summary>
    public static int Computed(Args args)
    {
        var bySource = new Dictionary<ValueSource, int>();
        var expressible = 0;
        var total = 0;
        var unresolved = 0;
        var events = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var doc in Load(args, out _))
        {
            var report = ComputedValueTable.Build(doc);
            total += report.Writes.Count;
            expressible += report.Expressible.Count();
            unresolved += report.Unresolved.Count;

            foreach (var write in report.Writes)
            {
                bySource[write.Source] = bySource.GetValueOrDefault(write.Source) + 1;
                if (write.Expressible) events[write.Event] = events.GetValueOrDefault(write.Event) + 1;
            }

            if (!args.Has("summary"))
            {
                Console.WriteLine($"== {report.Template}: {report.Writes.Count} field(s) filled in by a handler");

                foreach (var write in report.Writes.Take(args.Int("top", 15)))
                {
                    var when = write.When.Count == 0 ? "(always)" : string.Join(" AND ", write.When);

                    Console.WriteLine(
                        $"   {write.Target.Label} = {Describe(write)} [{write.Source}]"
                        + (write.Expressible ? "" : "  LEFT TO A PERSON"));
                    Console.WriteLine($"     from {write.Owner.Label} [{write.Event}] when {Trim(when, false)}");
                }

                Console.WriteLine();
            }
        }

        Console.WriteLine($"== {total} assignments, {expressible} expressible ({(total == 0 ? 0 : (double)expressible / total):P0})");

        foreach (var (source, count) in bySource.OrderByDescending(s => s.Value))
            Console.WriteLine($"   {source,-9} {count,5}");

        Console.WriteLine("   expressible by event: " + string.Join(", ", events.OrderByDescending(e => e.Value).Select(e => $"{e.Key} {e.Value}")));
        Console.WriteLine($"   {unresolved} write(s) naming no one element");
        return 0;
    }

    private static string Describe(ValueWrite write) => write.Source switch
    {
        ValueSource.Clear => "(blank)",
        ValueSource.Literal => $"\"{write.Expression}\"",
        ValueSource.Copy => $"{write.Expression}'s answer",
        _ => write.Expression
    };

    public static int Coverage(Args args)
    {
        var findings = new List<CoverageFinding>();
        foreach (var doc in Load(args, out _))
            findings.AddRange(SelectionCoverage.Build(doc).Findings);

        foreach (var kind in new[]
                 {
                     SelectionCoverageReport.TestedButNotAnOption,
                     SelectionCoverageReport.ShadowedBranch,
                     SelectionCoverageReport.OptionNeverTested,
                     SelectionCoverageReport.UnresolvedField
                 })
        {
            var of = findings.Where(f => f.Kind == kind).ToList();
            Console.WriteLine($"== {kind}: {of.Count}");
            foreach (var f in of.Take(args.Int("top", 20)))
                Console.WriteLine($"   {f.Template} / {f.SourceField} [{f.Event}] {f.Detail}");
            if (of.Count > args.Int("top", 20)) Console.WriteLine($"   … and {of.Count - args.Int("top", 20)} more");
            Console.WriteLine();
        }
        return 0;
    }

    public static int Prefixes(Args args)
    {
        var docs = Load(args, out _);
        var report = PrefixRules.Build(docs, args.Value("field") ?? PrefixRules.ReferenceField);

        Console.WriteLine("== fields sliced with string.sub, across the selection");
        foreach (var (field, count) in report.SlicedFields.Take(args.Int("top", 20)))
            Console.WriteLine($"   {count,5}  {field}");

        Console.WriteLine();
        Console.WriteLine($"== prefix rules on '{args.Value("field") ?? PrefixRules.ReferenceField}': "
                          + $"{report.Rules.Count} test(s), {report.Distinct.Count} distinct literal(s)");
        Console.WriteLine($"   {"literal",-10} {"len",3} {"count",5}  negated somewhere");
        foreach (var (literal, length, count, negated) in report.Distinct)
            Console.WriteLine($"   {literal,-10} {length,3} {count,5}  {(negated ? "yes" : "")}");

        // A literal tested at two different lengths is order dependent: the shorter test
        // swallows the longer one if it runs first.
        var overlapping = report.Distinct
            .GroupBy(d => d.Literal.Length >= 4 ? d.Literal[..4] : d.Literal, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.Length).Distinct().Count() > 1)
            .ToList();

        if (overlapping.Count > 0)
        {
            Console.WriteLine("\n   ORDER DEPENDENT — same stem tested at more than one length");
            foreach (var g in overlapping)
                Console.WriteLine($"     {g.Key}: lengths {string.Join(", ", g.Select(x => x.Length).Distinct().Order())}");
        }

        var negatedRules = report.Rules.Where(r => r.IsNegated).ToList();
        if (negatedRules.Count > 0)
        {
            Console.WriteLine($"\n   NEGATED ({negatedRules.Count}) — inverting one of these by eye is silent");
            foreach (var r in negatedRules.Take(args.Int("top", 20)))
                Console.WriteLine($"     {r.Template} / {r.SourceField} [{r.Event}] ~= \"{r.Literal}\"");
        }

        return 0;
    }

    public static int Shapes(Args args)
    {
        var docs = Load(args, out _);
        var report = ScriptShapes.Build(docs, args.Value("event"));

        Console.WriteLine($"== {report.TotalHandlers} handlers across {docs.Count} template(s)");
        Console.WriteLine($"   {"event",-16} {"handlers",8} {"shapes",7}  repetition");
        foreach (var (evt, handlers, shapes) in report.ByEvent)
            Console.WriteLine($"   {evt,-16} {handlers,8} {shapes,7}  {(double)handlers / shapes:F1}x");

        Console.WriteLine();
        Console.WriteLine($"== {report.Shapes.Count} distinct shapes; {report.SingletonShapes} occur exactly once");
        Console.WriteLine($"   {"top shapes",10} {"handlers",8}  share of all handlers");
        foreach (var (shapes, handlers, share) in report.CoverageCurve(1, 5, 10, 25, 50, 100, 250, 500))
            Console.WriteLine($"   {shapes,10} {handlers,8}  {share,6:P1}");

        Console.WriteLine();
        Console.WriteLine("== most repeated shapes");
        foreach (var s in report.Shapes.Take(args.Int("top", 20)))
        {
            Console.WriteLine($"   {s.Count,4}x [{s.Event}] {s.Examples[0]}");
            Console.WriteLine($"         {Trim(args.Has("full") ? s.Sample : s.Shape, args.Has("full"))}");
        }
        return 0;
    }

    public static int Repeats(Args args)
    {
        foreach (var doc in Load(args, out _))
        {
            var r = RepeatedBlocks.Build(doc);
            Console.WriteLine($"== {doc.FolderName}");

            Console.WriteLine($"   structural repeats: {r.Structural.Count} (siblings holding the same fields)");
            foreach (var g in r.Structural.Take(args.Int("top", 20)))
                Console.WriteLine($"     {g.Indices.Count}x {g.FieldType,-8} {g.Stem,-24} on {g.Page,-24} [{g.Signature}]");

            Console.WriteLine($"   flattened repeats:  {r.Flattened.Count} (index inside the field name)");
            foreach (var g in r.Flattened.Take(args.Int("top", 20)))
                Console.WriteLine($"     {g.Indices.Count}x {g.Stem,-24} on {g.Page,-24} {g.FieldCount} fields: {string.Join(", ", g.Fields)}");

            Console.WriteLine($"   incidental numbering: {r.Incidental.Count} (numbered, but not a repeat)");
            foreach (var g in r.Incidental.Take(args.Int("top", 10)))
                Console.WriteLine($"     {g.Count}x {g.FieldType,-8} {g.Stem,-24} on {g.Page}");

            Console.WriteLine();
        }
        return 0;
    }

    private static string Trim(string text, bool full)
    {
        if (full) return Environment.NewLine + "         " + text.Replace("\n", "\n         ");
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 110 ? line : line[..110] + " …";
    }
}
