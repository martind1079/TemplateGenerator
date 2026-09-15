using System.Text.Json;
using FormWorks.Templates.Analysis;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// Keying on a trailing digit alone is wrong in both directions: it reports numbered
/// sections that share nothing, and misses repeats whose index sits mid-name.
/// </summary>
public class RepeatedBlockTests
{
    private static string Page(params (string Type, string Name, string[] Children)[] sections)
    {
        object Node(string type, string name, string[] kids) => new Dictionary<string, object>
        {
            [type] = new Dictionary<string, object>
            {
                ["fieldType"] = type,
                ["elementName"] = name,
                ["name"] = name,
                ["children"] = kids.Select(k => (object)new Dictionary<string, object>
                {
                    ["Text"] = new Dictionary<string, object>
                    {
                        ["fieldType"] = "Text", ["elementName"] = k, ["name"] = k
                    }
                }).ToArray()
            }
        };

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Form"] = new Dictionary<string, object>
            {
                ["fieldType"] = "Form",
                ["children"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["Page"] = new Dictionary<string, object>
                        {
                            ["fieldType"] = "Page", ["elementName"] = "P", ["name"] = "P",
                            ["children"] = sections.Select(s => Node(s.Type, s.Name, s.Children)).ToArray()
                        }
                    }
                }
            }
        });
    }

    [Fact]
    public void Siblings_holding_the_same_fields_are_a_repeat()
    {
        var doc = TemplateLoader.LoadJson(Page(
            ("Group", "Borrower1", ["Name", "DateofBirth"]),
            ("Group", "Borrower2", ["Name", "DateofBirth"])), "F V1");

        var r = RepeatedBlocks.Build(doc);

        var repeat = Assert.Single(r.Structural);
        Assert.Equal("Borrower", repeat.Stem);
        Assert.Equal([1, 2], repeat.Indices);
        Assert.Empty(r.Incidental);
    }

    [Fact]
    public void Siblings_numbered_but_holding_different_fields_are_not_a_repeat()
    {
        var doc = TemplateLoader.LoadJson(Page(
            ("Section", "Part1", ["LightsOn", "CurtainsOpen"]),
            ("Section", "Part2", ["AnyPets"])), "F V1");

        var r = RepeatedBlocks.Build(doc);

        Assert.Empty(r.Structural);
        var incidental = Assert.Single(r.Incidental);
        Assert.Equal("Part", incidental.Stem);
    }

    [Fact]
    public void An_index_inside_the_field_name_is_a_flattened_repeat()
    {
        var doc = TemplateLoader.LoadJson(Page(
            ("Section", "Occupants",
                ["Tenant1Name", "Tenant1Age", "Tenant2Name", "Tenant2Age", "Tenant3Name", "Tenant3Age"])),
            "F V1");

        var r = RepeatedBlocks.Build(doc);

        var flat = Assert.Single(r.Flattened);
        Assert.Equal("Tenant", flat.Stem);
        Assert.Equal([1, 2, 3], flat.Indices);
        Assert.Equal(["Age", "Name"], flat.Fields);
        Assert.Equal(6, flat.FieldCount);
    }

    [Fact]
    public void An_uneven_set_of_indexed_fields_is_not_claimed_as_a_repeat()
    {
        var doc = TemplateLoader.LoadJson(Page(
            ("Section", "Mixed", ["Tenant1Name", "Tenant1Age", "Tenant2Name"])), "F V1");

        var r = RepeatedBlocks.Build(doc);

        Assert.Empty(r.Flattened);
    }
}
