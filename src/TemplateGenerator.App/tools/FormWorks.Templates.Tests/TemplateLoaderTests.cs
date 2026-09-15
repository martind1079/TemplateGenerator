using FormWorks.Templates.Analysis;
using FormWorks.Templates.Model;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

public class TemplateLoaderTests
{
    private const string Minimal = """
        {"Form":{"fieldType":"Form","name":"F","children":[
          {"Page":{"fieldType":"Page","elementName":"P1","name":"P1","children":[
            {"Section":{"fieldType":"Section","elementName":"S","name":"P1.S","children":[
              {"SingleSelection":{"fieldType":"SingleSelection","elementName":"Reason","name":"P1.S.Reason",
                "options":[{"Value":"a","Content":"A"},{"Value":"b","Content":"B"}],
                "scripts":{"OnValidate":"this.valid = false;"}}}
            ]}}
          ]}}
        ]}}
        """;

    [Fact]
    public void A_byte_order_mark_does_not_stop_the_parse()
    {
        var doc = TemplateLoader.LoadJson('﻿' + Minimal, "Fixture V1");

        Assert.Equal("Form", doc.Root.FieldType);
    }

    [Fact]
    public void Children_are_typed_by_their_wrapper_key()
    {
        var doc = TemplateLoader.LoadJson(Minimal, "Fixture V1");

        var page = Assert.Single(doc.Pages);
        Assert.Equal("Page", page.FieldType);
        Assert.Equal("Section", page.Children[0].FieldType);
        Assert.Equal("SingleSelection", page.Children[0].Children[0].FieldType);
    }

    [Fact]
    public void Options_and_scripts_come_through()
    {
        var doc = TemplateLoader.LoadJson(Minimal, "Fixture V1");
        var field = doc.AllNodes.Single(n => n.ElementName == "Reason");

        Assert.Equal(["a", "b"], field.Options.Select(o => o.Value));
        Assert.Equal("A", field.Options[0].Content);
        Assert.True(field.Scripts.ContainsKey("OnValidate"));
    }

    [Fact]
    public void A_node_knows_which_page_it_sits_on()
    {
        var doc = TemplateLoader.LoadJson(Minimal, "Fixture V1");
        var field = doc.AllNodes.Single(n => n.ElementName == "Reason");

        Assert.Equal("P1", field.Page?.ElementName);
        Assert.Equal(3, field.Depth);
    }

    [Theory]
    [InlineData("Property Survey (Audio) V23", "Property Survey (Audio)", 23)]
    [InlineData("Mortgage Arrears Standard V14", "Mortgage Arrears Standard", 14)]
    [InlineData("No Version Here", "No Version Here", null)]
    public void The_version_suffix_is_split_from_the_family(string folder, string family, int? version)
    {
        var (f, v) = TemplateDocument.SplitVersion(folder);

        Assert.Equal(family, f);
        Assert.Equal(version, v);
    }

    [Fact]
    public void Only_the_highest_version_of_a_family_survives_the_filter()
    {
        var docs = new[] { "Thing V1", "Thing V3", "Thing V2", "Other V9" }
            .Select(n => TemplateLoader.LoadJson(Minimal, n));

        var latest = EstateLocator.LatestPerFamily(docs);

        Assert.Equal(2, latest.Count);
        Assert.Contains(latest, d => d.FolderName == "Thing V3");
        Assert.DoesNotContain(latest, d => d.FolderName == "Thing V2");
    }
}
