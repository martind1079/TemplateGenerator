namespace FormWorks.Templates.Emit;

/// <summary>
/// What generated code has to look like to belong in its target app, rather than announcing
/// itself as generated: base classes, namespaces, and where files land.
///
/// Every emitter used to build these itself, by gluing a fixed suffix onto a root namespace -
/// which is what this app's own convention still is, and what <see cref="Default"/> reproduces.
/// A different host app rarely shares that shape: it may put every template's pages under one
/// shared folder instead of grouping by template, and its pages and view models almost always
/// have their own base class already, carrying a shared control template or a DI-injected
/// service the page needs. This is the seam a converter run points at a different app through.
///
/// Implicitly convertible from a plain root namespace, so every call site that only ever knew
/// this app's own shape - <c>EmitPage(model, "MyApp")</c> - keeps compiling exactly as it did
/// before a house style existed to override it.
/// </summary>
public sealed record HouseStyle
{
    public required string ViewsNamespace { get; init; }
    public required string ModelsNamespace { get; init; }
    public required string ViewModelsNamespace { get; init; }

    /// <summary>Where the converter's own house controls (NullableDatePicker and the rest) live.</summary>
    public required string ControlsNamespace { get; init; }

    /// <summary>Where <c>IFormStore&lt;TReport&gt;</c> lives.</summary>
    public required string ServicesNamespace { get; init; }

    /// <summary>Where the <c>FormStep</c> type lives, for the page's step strip.</summary>
    public required string JobFormsNamespace { get; init; }

    /// <summary>Where the report's own base types (a field's error state, and the like) live.</summary>
    public required string FormsNamespace { get; init; }

    /// <summary>
    /// The page's base class. The default needs no extra using or xmlns: it is a plain MAUI
    /// type already in scope. A host app's own base class usually lives elsewhere, hence
    /// <see cref="PageBaseClassNamespace"/> alongside it.
    /// </summary>
    public string PageBaseClass { get; init; } = "ContentPage";

    /// <summary>Null when <see cref="PageBaseClass"/> needs no using or xmlns of its own.</summary>
    public string? PageBaseClassNamespace { get; init; }

    /// <summary>
    /// The view model's base class. The default is CommunityToolkit.Mvvm's own type, already
    /// imported by every generated view model regardless. A host app's base class typically
    /// wraps that same type rather than replacing it, so the generated partial class still
    /// only has to carry <c>[ObservableProperty]</c> and <c>[RelayCommand]</c> members.
    /// </summary>
    public string ViewModelBaseClass { get; init; } = "ObservableObject";

    /// <summary>Null when <see cref="ViewModelBaseClass"/> needs no using of its own.</summary>
    public string? ViewModelBaseClassNamespace { get; init; }

    /// <summary>
    /// Where each area's files land, relative to the app directory. <c>{family}</c> is
    /// replaced with the template's class-name stem - present in this app's own convention,
    /// which keeps one template's output apart from another's, and typically absent from a
    /// host app's convention, which instead groups every template's pages into one shared
    /// folder alongside its hand-written ones.
    /// </summary>
    public string ViewsFolder { get; init; } = "Generated/{family}/Views";

    public string ModelsFolder { get; init; } = "Generated/{family}/Models";

    public string ViewModelsFolder { get; init; } = "Generated/{family}/ViewModels";

    /// <summary>This app's own shape: every namespace is the root plus the suffix an emitter used to hardcode.</summary>
    public static HouseStyle Default(string rootNamespace) => new()
    {
        ViewsNamespace = $"{rootNamespace}.Views.Generated",
        ModelsNamespace = $"{rootNamespace}.Models.Generated",
        ViewModelsNamespace = $"{rootNamespace}.ViewModels.Generated",
        ControlsNamespace = $"{rootNamespace}.Views.Controls",
        ServicesNamespace = $"{rootNamespace}.Services",
        JobFormsNamespace = $"{rootNamespace}.Models.JobForms",
        FormsNamespace = $"{rootNamespace}.Models.Forms",
    };

    public static implicit operator HouseStyle(string rootNamespace) => Default(rootNamespace);
}
