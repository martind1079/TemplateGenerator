using FormWorks.Templates.Analysis;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// The connective is the thing worth guarding. An "or" chain rendered as "and" routes
/// nobody anywhere while reading perfectly reasonably, and the compiler cannot see it.
/// </summary>
public class ConditionParserTests
{
    [Fact]
    public void Or_chain_becomes_alternatives_not_a_conjunction()
    {
        var c = ConditionParser.Parse(
            """Outcome.value == "Refused" or Outcome.value == "Deceased" """);

        Assert.Equal(2, c.AnyOf.Count);
        Assert.All(c.AnyOf, term => Assert.Single(term));
        Assert.Contains(" OR ", c.ToString());
        Assert.DoesNotContain(" AND ", c.ToString());
    }

    [Fact]
    public void And_within_a_term_stays_a_conjunction()
    {
        var c = ConditionParser.Parse(
            """Outcome.value == "Refused" and Stage.value == "Two" """);

        Assert.Single(c.AnyOf);
        Assert.Equal(2, c.AnyOf[0].Count);
    }

    [Fact]
    public void Outer_parentheses_do_not_change_the_split()
    {
        var bare = ConditionParser.Parse("""A.value == "x" or B.value == "y" """);
        var wrapped = ConditionParser.Parse("""(A.value == "x" or B.value == "y") """);

        Assert.Equal(bare.AnyOf.Count, wrapped.AnyOf.Count);
    }

    [Fact]
    public void Or_inside_parentheses_is_not_split_at_the_top_level()
    {
        var c = ConditionParser.Parse(
            """A.value == "x" and (B.value == "y" or C.value == "z")""");

        // One top level term, because the only "or" is parenthesised.
        Assert.Single(c.AnyOf);
        Assert.Equal(3, c.AnyOf[0].Count);
    }

    [Fact]
    public void A_keyword_inside_a_string_literal_is_not_a_connective()
    {
        var c = ConditionParser.Parse("""Outcome.value == "Gone or Away" """);

        Assert.Single(c.AnyOf);
        Assert.Equal("Gone or Away", c.AnyOf[0][0].Literal);
    }

    [Fact]
    public void Prefix_test_keeps_its_slice_bounds_and_is_not_double_counted()
    {
        var c = ConditionParser.Parse("""string.sub(Reference.value, 1, 6) == "ASBHM1" """);

        var atom = Assert.Single(c.AnyOf[0]);
        Assert.True(atom.IsPrefixTest);
        Assert.Equal(1, atom.From);
        Assert.Equal(6, atom.To);
        Assert.Equal("ASBHM1", atom.Literal);
    }

    [Fact]
    public void Negated_operator_is_preserved()
    {
        var c = ConditionParser.Parse("""string.sub(Reference.value, 1, 4) ~= "MSUK" """);

        Assert.Equal("~=", c.AnyOf[0][0].Operator);
    }

    [Fact]
    public void Negating_a_condition_shows_in_its_text()
    {
        var c = ConditionParser.Parse("""A.value == "x" """).Negate();

        Assert.StartsWith("NOT ", c.ToString());
    }

    [Fact]
    public void A_condition_with_no_comparison_is_reported_as_unparsed()
    {
        var c = ConditionParser.Parse("someFunction(x) > 3");

        Assert.False(c.FullyParsed);
        Assert.Contains("someFunction", c.ToString());
    }
}
