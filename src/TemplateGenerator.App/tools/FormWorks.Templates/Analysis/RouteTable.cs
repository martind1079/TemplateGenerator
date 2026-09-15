using System.Text;
using FormWorks.Templates.Model;

namespace FormWorks.Templates.Analysis;

/// <summary>
/// A single route: the conditions that must hold, and where the form then goes.
/// </summary>
/// <param name="When">
/// Every condition that must hold, in source order. A branch inside a branch appears
/// as two entries, and an else branch contributes its siblings negated. Empty means
/// the route is unconditional.
/// </param>
/// <param name="Own">
/// Only the conditions this branch states for itself, without the negated siblings.
/// An elseif chain is order significant, so a generator emitting routes in source
/// order gets the negations for free and only needs these. This is the compact form,
/// and the one worth reading.
/// </param>
public sealed record Route(
    string? FromPage,
    string SourceField,
    string Event,
    string ToPage,
    IReadOnlyList<Condition> When,
    IReadOnlyList<Condition> Own)
{
    /// <summary>True when every condition came apart into comparisons a generator can emit.</summary>
    public bool FullyParsed => When.All(c => c.FullyParsed);

    public IEnumerable<Guard> Atoms => When.SelectMany(c => c.Atoms);

    /// <summary>The branch's own condition. Only correct when routes are kept in order.</summary>
    public string Describe() => Own.Count > 0
        ? string.Join(" AND ", Own)
        : When.Count == 0 ? "(always)" : "(otherwise)";

    /// <summary>The complete guard, negated siblings included. Correct in any order.</summary>
    public string DescribeFull() => When.Count == 0 ? "(always)" : string.Join(" AND ", When);
}

public sealed record RouteTableReport(
    string Template,
    IReadOnlyList<Route> Routes,
    int ChangePageCalls,
    IReadOnlyList<string> UnparsedHandlers)
{
    /// <summary>Routes whose conditions came apart cleanly into atoms a generator can emit.</summary>
    public IEnumerable<Route> Emittable => Routes.Where(r => r.FullyParsed);

    public double EmittableShare => Routes.Count == 0 ? 0 : (double)Emittable.Count() / Routes.Count;
}

/// <summary>
/// Recovers the guard on every routing call, not just its destination.
///
/// The page graph answers where a form can go. Emitting routing as data needs the
/// other half: under what condition. That means walking the if/elseif/else structure
/// rather than pattern matching for changePage, because a destination inside a third
/// branch is guarded by its own condition and by the negation of the two before it.
///
/// This is a structural reader over line-oriented Lua, not a Lua parser. Handlers that
/// do not come apart are reported rather than half-converted, because a routing table
/// that is quietly wrong is worse than one that is visibly incomplete.
/// </summary>
public static class RouteTable
{
    public static RouteTableReport Build(TemplateDocument doc)
    {
        var routes = new List<Route>();
        var unparsed = new List<string>();
        var calls = 0;

        foreach (var node in doc.AllNodes)
        {
            foreach (var (evt, script) in node.Scripts)
            {
                var found = LuaScript.ChangePage().Matches(script).Count;
                if (found == 0) continue;
                calls += found;

                var from = node.IsPage ? node.ElementName ?? node.Label : node.Page?.ElementName ?? node.Page?.Label;
                var extracted = Walk(script, from, node.Label, evt);

                routes.AddRange(extracted);

                // Every call must come out the other side with a guard attached. If the
                // count does not match, the structure was not understood and saying so is
                // the only safe answer.
                if (extracted.Count != found)
                    unparsed.Add($"{node.Label} [{evt}]: {found} call(s), {extracted.Count} recovered");
            }
        }

        return new RouteTableReport(doc.FolderName, routes, calls, unparsed);
    }

    private static List<Route> Walk(string script, string? fromPage, string sourceField, string evt)
        => ScriptWalker.Walk(script, line => LuaScript.ChangePage().IsMatch(line))
            .SelectMany(statement => LuaScript.ChangePage().Matches(statement.Line)
                .Select(m => new Route(
                    fromPage, sourceField, evt, m.Groups["page"].Value,
                    statement.When, statement.Own)))
            .ToList();
}
