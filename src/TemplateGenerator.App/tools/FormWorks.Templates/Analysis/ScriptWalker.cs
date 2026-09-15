using FormWorks.Templates.Model;

namespace FormWorks.Templates.Analysis;

/// <summary>One statement, with the conditions that have to hold for it to run.</summary>
/// <param name="Own">
/// Only the conditions the enclosing branches state for themselves. An if/elseif chain is
/// order significant, so code emitted in source order gets the negations for free.
/// </param>
/// <param name="When">Every condition, negated siblings included. Correct in any order.</param>
public sealed record GuardedStatement(
    string Line, IReadOnlyList<Condition> When, IReadOnlyList<Condition> Own);

/// <summary>
/// Finds statements inside a Lua handler along with the branch conditions guarding them.
///
/// Shared by routing and validation because it is the same problem both times: a statement
/// in a third branch is guarded by its own condition and by the negation of the two before
/// it, and none of that is stated anywhere except in the shape of the script.
///
/// A structural reader over line-oriented Lua, not a parser. Handlers whose structure it
/// does not follow are visible to the caller as statements it failed to find, rather than
/// being half understood.
/// </summary>
public static class ScriptWalker
{
    private sealed class Frame
    {
        public string? Current;
        public List<string> Priors = [];
    }

    public static List<GuardedStatement> Walk(string script, Func<string, bool> isInteresting)
    {
        var found = new List<GuardedStatement>();
        var stack = new List<Frame>();
        var pending = new System.Text.StringBuilder();

        foreach (var raw in script.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            // Conditions occasionally run across lines, so accumulate until "then".
            if (pending.Length > 0)
            {
                pending.Append(' ').Append(line);
                if (!ContainsKeyword(line, "then")) continue;
                line = pending.ToString();
                pending.Clear();
            }

            if (StartsWithKeyword(line, "elseif"))
            {
                if (!ContainsKeyword(line, "then")) { pending.Append(line); continue; }
                if (stack.Count > 0)
                {
                    var frame = stack[^1];
                    if (frame.Current is not null) frame.Priors.Add(frame.Current);
                    frame.Current = ConditionText(line, "elseif");
                }
            }
            else if (StartsWithKeyword(line, "if"))
            {
                if (!ContainsKeyword(line, "then")) { pending.Append(line); continue; }
                stack.Add(new Frame { Current = ConditionText(line, "if") });
            }
            else if (StartsWithKeyword(line, "else"))
            {
                if (stack.Count > 0)
                {
                    var frame = stack[^1];
                    if (frame.Current is not null) frame.Priors.Add(frame.Current);
                    frame.Current = null;
                }
            }
            else if (StartsWithKeyword(line, "end"))
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }

            if (isInteresting(line))
            {
                var when = new List<Condition>();
                var own = new List<Condition>();

                foreach (var frame in stack)
                {
                    foreach (var prior in frame.Priors)
                        when.Add(ConditionParser.Parse(prior).Negate());

                    if (frame.Current is null) continue;

                    var parsed = ConditionParser.Parse(frame.Current);
                    when.Add(parsed);
                    own.Add(parsed);
                }

                found.Add(new GuardedStatement(line, when, own));
            }

            // A block that opens and closes on one line, "if x then y end", closes here.
            // Counted after the statement is recorded, because the statement is inside the
            // block. Without this the frame leaks and every later statement inherits a
            // condition that stopped applying on the line it was written: an elseif further
            // down then updates the leaked frame rather than its own, and the rule comes out
            // as a conjunction of branches that exclude one another.
            var closes = CountKeyword(line, "end");
            if (StartsWithKeyword(line, "end")) closes--;

            for (var i = 0; i < closes && stack.Count > 0; i++)
                stack.RemoveAt(stack.Count - 1);
        }

        return found;
    }

    private static string ConditionText(string line, string keyword)
    {
        var start = line.IndexOf(keyword, StringComparison.Ordinal) + keyword.Length;
        var end = line.LastIndexOf("then", StringComparison.Ordinal);
        return end > start ? line[start..end].Trim() : line[start..].Trim();
    }

    private static string StripComment(string line)
    {
        var i = line.IndexOf("--", StringComparison.Ordinal);
        return i >= 0 ? line[..i] : line;
    }

    private static bool StartsWithKeyword(string line, string keyword)
        => line.StartsWith(keyword, StringComparison.Ordinal)
           && (line.Length == keyword.Length || !char.IsLetterOrDigit(line[keyword.Length]));

    private static bool ContainsKeyword(string line, string keyword)
        => System.Text.RegularExpressions.Regex.IsMatch(line, $@"\b{keyword}\b");

    private static int CountKeyword(string line, string keyword)
        => System.Text.RegularExpressions.Regex.Matches(line, $@"\b{keyword}\b").Count;
}
