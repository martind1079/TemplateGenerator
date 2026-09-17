using System.Text.Json;
using FormWorks.Templates.Emit;
using FormWorks.Templates.Parsing;
using Xunit;

namespace FormWorks.Templates.Tests;

/// <summary>
/// A house style is how a different host app tells the converter what generated code has to
/// look like to belong there. These cover that it actually changes what comes out - a plain
/// root namespace has to keep producing exactly what it always did, since every existing call
/// site (including every other test in this project) still passes one.
/// </summary>
public class HouseStyleTests
{
    private static readonly HouseStyle Host = new()
    {
        ViewsNamespace = "Host.Views.JobForms",
        ModelsNamespace = "Host.Models.JobForms",
        ViewModelsNamespace = "Host.ViewModels.JobForms",
        ControlsNamespace = "Host.Views.Controls",
        ServicesNamespace = "Host.Services",
        JobFormsNamespace = "Host.Models.Common",
        FormsNamespace = "Host.Models.Common",
        PageBaseClass = "BaseContentPage",
        PageBaseClassNamespace = "Host.BaseObjects",
        ViewModelBaseClass = "BaseViewModel",
        ViewModelBaseClassNamespace = "Host.BaseObjects",
        ViewsFolder = "Views/JobForms",
        ModelsFolder = "Models/JobForms",
        ViewModelsFolder = "ViewModels/JobForms",
    };

    private static PageEmitModel Model()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
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
                            ["fieldType"] = "Page", ["elementName"] = "JobSheet", ["name"] = "JobSheet",
                            ["title"] = "Job Sheet",
                            ["children"] = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["Text"] = new Dictionary<string, object>
                                    {
                                        ["fieldType"] = "Text", ["elementName"] = "Name",
                                        ["name"] = "JobSheet.Name", ["title"] = "Name",
                                        ["options"] = Array.Empty<object>()
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        var doc = TemplateLoader.LoadJson(json, "Fixture V1");
        return PageEmitModelBuilder.Build(doc, doc.Pages.Single(), ControlVocabulary.Default, "");
    }

    [Fact]
    public void A_plain_root_namespace_still_produces_this_apps_own_shape()
    {
        HouseStyle implicitStyle = "TemplateGenerator.App";
        var explicitStyle = HouseStyle.Default("TemplateGenerator.App");

        Assert.Equal(explicitStyle, implicitStyle);
        Assert.Equal("ContentPage", implicitStyle.PageBaseClass);
        Assert.Null(implicitStyle.PageBaseClassNamespace);
        Assert.Equal("ObservableObject", implicitStyle.ViewModelBaseClass);
        Assert.Equal("TemplateGenerator.App.Views.Generated", implicitStyle.ViewsNamespace);
    }

    [Fact]
    public void A_house_styles_page_base_class_replaces_ContentPage_everywhere_it_appears()
    {
        var xaml = XamlEmitter.EmitPage(Model(), Host);
        var codeBehind = XamlEmitter.EmitCodeBehind(Model(), Host);

        Assert.Contains("<base:BaseContentPage ", xaml);
        Assert.Contains("</base:BaseContentPage>", xaml);
        Assert.Contains("xmlns:base=\"clr-namespace:Host.BaseObjects\"", xaml);
        Assert.DoesNotContain("<ContentPage ", xaml);

        Assert.Contains("using Host.BaseObjects;", codeBehind);
        Assert.Contains(": BaseContentPage", codeBehind);
    }

    [Fact]
    public void A_house_styles_view_model_base_class_replaces_ObservableObject()
    {
        var code = ModelEmitter.EmitViewModel(
            Model(), Host, "Report", "Nav", "Validator", "Routes", new HashSet<string>());

        Assert.Contains("using Host.BaseObjects;", code);
        Assert.Contains(": BaseViewModel", code);
        Assert.DoesNotContain(": ObservableObject", code);
    }

    [Fact]
    public void A_house_styles_namespaces_replace_the_Generated_suffixes()
    {
        var xaml = XamlEmitter.EmitPage(Model(), Host);
        var code = ModelEmitter.EmitViewModel(
            Model(), Host, "Report", "Nav", "Validator", "Routes", new HashSet<string>());

        Assert.Contains("Host.Views.JobForms.", xaml);
        Assert.Contains("clr-namespace:Host.ViewModels.JobForms", xaml);
        Assert.Contains("clr-namespace:Host.Views.Controls", xaml);

        Assert.Contains("namespace Host.ViewModels.JobForms;", code);
        Assert.Contains("using Host.Models.JobForms;", code);
        Assert.Contains("using Host.Services;", code);
        Assert.Contains("using FormStep = Host.Models.Common.FormStep;", code);
    }

    [Fact]
    public void An_answers_class_stays_on_ObservableObject_regardless_of_house_style()
    {
        // The answers class is a plain data holder, not a view model: a host app's own
        // view-model base class (navigation, busy state, DI-injected services) has nothing
        // for it to use, so house styles do not touch it.
        var code = ModelEmitter.EmitAnswers(Model(), Host);

        Assert.Contains(": ObservableObject", code);
        Assert.Contains("namespace Host.Models.JobForms;", code);
    }
}
