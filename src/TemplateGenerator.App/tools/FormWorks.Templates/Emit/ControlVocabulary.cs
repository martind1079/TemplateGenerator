namespace FormWorks.Templates.Emit;

/// <summary>Where a field's caption sits relative to its control.</summary>
public enum CaptionPlacement
{
    /// <summary>Caption above the control, which suits anything with a box to fill.</summary>
    Above,

    /// <summary>
    /// Control on the left, caption beside it. A checkbox reads as a tick against a
    /// statement, not as a box with a heading, and the caption wraps rather than
    /// truncating, so the row grows taller when the text is long.
    /// </summary>
    Trailing
}

/// <summary>
/// How one FormWorks field type is rendered and stored.
/// </summary>
/// <param name="Control">The MAUI control element name.</param>
/// <param name="BindingProperty">The control property the answer binds to.</param>
/// <param name="ClrType">The type of the generated model property.</param>
/// <param name="DefaultExpression">Initialiser for that property, or null for the type default.</param>
/// <param name="UsesOptions">Whether the control needs an item source built from the field's options.</param>
/// <param name="UsesTextLines">Whether the control is sized from the field's declared line count.</param>
/// <param name="Caption">Where the field's caption sits relative to the control.</param>
public sealed record ControlMapping(
    string Control,
    string BindingProperty,
    string ClrType,
    string? DefaultExpression = null,
    bool UsesOptions = false,
    bool UsesTextLines = false,
    CaptionPlacement Caption = CaptionPlacement.Above);

/// <summary>
/// The emitter's control vocabulary, in one table.
///
/// The target repository's conventions may differ from the ones proved here, and the
/// plan is explicit that this must be repointable without touching emitter logic. So
/// nothing below is referenced by name anywhere else: the emitters ask this table.
/// </summary>
public sealed class ControlVocabulary
{
    public static ControlVocabulary Default { get; } = new();

    /// <summary>
    /// Points per line of text, and the padding an editor adds around them.
    ///
    /// Configuration rather than a constant in the emitter, because it is a house style
    /// decision that belongs with the control names: a repository with different type
    /// sizes needs different numbers here and nothing else changed.
    /// </summary>
    public double TextLineHeight { get; init; } = 22;

    public double TextBoxPadding { get; init; } = 16;

    /// <summary>
    /// Resource keys the emitted pages name for appearance.
    ///
    /// The generator commits to no colour, font or thickness of its own. It names these
    /// and the style sheet decides what they mean, so restyling a converted form is an
    /// edit to Styles.xaml with no regeneration and no generator change.
    /// </summary>
    public string CaptionStyleKey { get; init; } = "FormFieldCaption";

    public string InstructionStyleKey { get; init; } = "FormFieldInstruction";

    public string DividerStyleKey { get; init; } = "FormDivider";

    /// <summary>Gap between a control and a caption sitting beside it.</summary>
    public double CaptionGap { get; init; } = 8;

    /// <summary>
    /// The height an input occupies, so a control that is naturally shorter still sits in
    /// the middle of the same band as the entries beside it. A checkbox is a tick and a
    /// line of text and is shorter than a text box, so without this it rides high in its
    /// row. Must match the input height the style sheet sets.
    /// </summary>
    public double InputHeight { get; init; } = 48;

    /// <summary>
    /// Date formats accepted from a job, tried in order, all invariant.
    ///
    /// Explicit rather than letting the parser guess, because a locale-sensitive parse
    /// turns the third of April into the fourth of March without complaining. Point these
    /// at whatever the portal actually sends.
    /// </summary>
    public IReadOnlyList<string> DateInputFormats { get; init; } =
        ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "dd/MM/yyyy"];

    public IReadOnlyList<string> TimeInputFormats { get; init; } = ["HH:mm", "HH:mm:ss", "hh\\:mm"];

    /// <summary>How dates and times are written back out.</summary>
    public string DateOutputFormat { get; init; } = "yyyy-MM-dd";

    public string TimeOutputFormat { get; init; } = "hh\\:mm";

    /// <summary>
    /// Values accepted for a checkbox, lower-cased. Liberal enough for the exports seen so
    /// far and no more: anything else is reported rather than guessed at.
    /// </summary>
    public IReadOnlyDictionary<string, bool> BooleanInputs { get; init; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["true"] = true, ["yes"] = true, ["y"] = true, ["1"] = true,
            ["false"] = false, ["no"] = false, ["n"] = false, ["0"] = false
        };

    public double HeightForLines(int lines) => (lines * TextLineHeight) + TextBoxPadding;

    private readonly Dictionary<string, ControlMapping> _inputs = new(StringComparer.Ordinal)
    {
        ["Text"] = new("Entry", "Text", "string", "string.Empty"),
        ["ParagraphText"] = new("Editor", "Text", "string", "string.Empty", UsesTextLines: true),
        ["SingleSelection"] = new("Picker", "SelectedItem", "string?", null, UsesOptions: true),
        ["MultiSelection"] = new("Picker", "SelectedItem", "string?", null, UsesOptions: true),
        ["Checkbox"] = new("CheckBox", "IsChecked", "bool", Caption: CaptionPlacement.Trailing),
        // House controls, because MAUI's own have no empty state: a DatePicker always
        // shows a date and a TimePicker defaults to midnight, so an untouched field cannot
        // be told from one answered today, or at midnight.
        ["Date"] = new("controls:NullableDatePicker", "Date", "DateTime?"),
        // A house control rather than a framework one. The framework has no MediaPicker
        // wrapper, which is still an open question for the team; naming it here means
        // swapping in theirs is a one-line change.
        ["Photo"] = new("controls:PhotoCaptureControl", "PhotoPath", "string?"),
        ["Time"] = new("controls:NullableTimePicker", "Time", "TimeSpan?")
    };

    /// <summary>Types that hold other fields rather than an answer of their own.</summary>
    public IReadOnlySet<string> Containers { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "Page", "Section", "Group" };

    /// <summary>
    /// Types that raise an action rather than hold an answer.
    ///
    /// A button contributes no model property. What it does when tapped is routing, which
    /// is emitted as a table rather than as page code, so the generated view model exposes
    /// a command with an empty seam for that table to fill.
    /// </summary>
    public IReadOnlyDictionary<string, string> Actions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Button"] = "Button"
    };

    /// <summary>Types rendered as static text, contributing no model property.</summary>
    public IReadOnlySet<string> Decorations { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "Label", "Line", "Image" };

    /// <summary>
    /// Types deliberately not emitted yet, with the reason. Buttons carry routing, which
    /// belongs to the routing table rather than the page. Photo and Signature need
    /// framework controls that do not exist yet, which is an open question for the team.
    /// </summary>
    public IReadOnlyDictionary<string, string> Deferred { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Signature"] = "needs a DrawingView wrapper that the framework does not have yet",
        ["Table"] = "not yet designed",
        ["TableCell"] = "not yet designed"
    };

    public bool TryGetInput(string fieldType, out ControlMapping mapping)
        => _inputs.TryGetValue(fieldType, out mapping!);

    public bool IsContainer(string fieldType) => Containers.Contains(fieldType);
    public bool IsAction(string fieldType) => Actions.ContainsKey(fieldType);
    public bool IsDecoration(string fieldType) => Decorations.Contains(fieldType);
}
