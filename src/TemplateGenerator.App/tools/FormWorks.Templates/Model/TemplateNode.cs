using System.Text.Json;

namespace FormWorks.Templates.Model;

/// <summary>
/// One node of a FormWorks template tree: the form, a page, a section, or a control.
///
/// Templates nest every child inside a single-key wrapper object keyed by its type,
/// like { "Page": { ... } }. That wrapper key is carried here as
/// <see cref="FieldType"/>. The node's own "fieldType" property normally repeats it,
/// and <see cref="DeclaredFieldType"/> keeps it so the two can be compared; a
/// mismatch means an assumption of the reader is wrong and should be reported rather
/// than silently resolved.
///
/// Only the properties the converter reasons about are mapped. Everything else stays
/// reachable through <see cref="Raw"/>, because layout geometry, colours and a long
/// tail of flags are needed by later passes and guessing which ones now would mean
/// re-reading the estate later.
/// </summary>
public sealed class TemplateNode
{
    /// <summary>The wrapper key this node arrived under, e.g. "Page" or "Checkbox".</summary>
    public required string FieldType { get; init; }

    /// <summary>The node's own "fieldType" property, when it has one.</summary>
    public string? DeclaredFieldType { get; init; }

    public int? Id { get; init; }

    /// <summary>
    /// Fully qualified name, e.g. "JobSheet.VisitOutcome.Reason". Unique within a
    /// template and the most useful handle for a human reading a report.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The short name scripts refer to. Scripts say <c>VisitOutcome.value</c>, not the
    /// qualified name, so resolving a script reference to a node goes through this.
    /// </summary>
    public string? ElementName { get; init; }

    public string? Alias { get; init; }
    public string? Title { get; init; }

    public bool Hidden { get; init; }
    public bool ReadOnly { get; init; }
    public bool Required { get; init; }

    /// <summary>
    /// Declared width in points against the form's design surface, which is 980 for
    /// every template read so far.
    ///
    /// This is how rows are recovered. FormWorks carries no row marker: fields flow left
    /// to right and wrap when the next one would not fit, so a row is an accumulation of
    /// widths and margins against the container. The contact summary's first row comes to
    /// 954 across five fields and the sixth would overflow, which is the whole of it.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// How many lines of text a paragraph field is sized for. Every one of the 3,197
    /// paragraph fields in the estate declares it, ranging from 2 to 14, so an editor
    /// left to size itself is always wrong rather than occasionally.
    /// </summary>
    public int? TextLines { get; init; }

    /// <summary>
    /// How wide the control itself may draw, as against <see cref="Width"/>, which is
    /// the space the field occupies in its row.
    ///
    /// A field 230 points wide whose content width is 140 keeps its place in the row and
    /// its label at full width, but the input box itself is narrower. 4,369 fields across
    /// the estate set the two differently, so ignoring it makes every one of them a text
    /// box stretched wider than it was drawn.
    /// </summary>
    public int? ContentWidth { get; init; }

    /// <summary>How tall the control itself draws. Photo fields declare it; most do not.</summary>
    public int? ContentHeight { get; init; }

    /// <summary>
    /// Whether to keep a caption's worth of space above the control when no caption is
    /// drawn there.
    ///
    /// This is how a row stays aligned when its fields disagree about captions. A
    /// checkbox wears its caption on the right, so without reserved space it floats to
    /// the top of a row whose other fields sit under their headings. Estate-wide 809
    /// checkboxes ask for the space and 1,700 do not, which is the difference between a
    /// tick in a table column and a tick against a statement.
    /// </summary>
    public bool AssignSpaceForTitle { get; init; }

    public int MarginLeft { get; init; }
    public int MarginRight { get; init; }
    public int MarginTop { get; init; }
    public int MarginBottom { get; init; }

    /// <summary>Width including its margins, which is what actually consumes the row.</summary>
    public int OuterWidth => (Width ?? 0) + MarginLeft + MarginRight;

    /// <summary>
    /// The width this node lays its children out against: its own, or the nearest
    /// ancestor that declares one.
    ///
    /// Not to be confused with <see cref="ContentWidth"/>, which is the template's own
    /// property and means something else entirely.
    /// </summary>
    public int LayoutWidth
    {
        get
        {
            for (var n = this; n is not null; n = n.Parent)
                if (n.Width is > 0) return n.Width.Value;
            return 980;
        }
    }

    public IReadOnlyList<TemplateOption> Options { get; init; } = [];

    /// <summary>Lua handlers by event name: OnValidate, OnValueChange, OnTap and so on.</summary>
    public IReadOnlyDictionary<string, string> Scripts { get; init; }
        = new Dictionary<string, string>();

    public IReadOnlyList<TemplateNode> Children { get; init; } = [];

    /// <summary>Null on the form root only.</summary>
    public TemplateNode? Parent { get; internal set; }

    /// <summary>Every property of the node as it appeared in the file.</summary>
    public JsonElement Raw { get; init; }

    /// <summary>The best available human label, falling back through the name fields.</summary>
    public string Label => Name ?? ElementName ?? Alias ?? Title ?? $"{FieldType}#{Id}";

    /// <summary>
    /// Every name this node answers to.
    ///
    /// Scripts reach a field by element name or alias, the node's own label is usually the
    /// fully qualified name, and matching one against the other silently finds nothing. A
    /// miss there does not fail: it reads as a field the template never writes, which is
    /// indistinguishable from one that is simply always shown.
    /// </summary>
    public IEnumerable<string> Names
    {
        get
        {
            yield return Label;

            if (!string.IsNullOrEmpty(ElementName) && ElementName != Label)
                yield return ElementName;

            // Distinct, because a node whose alias repeats its element name would otherwise
            // claim the same name twice and read as two elements competing for it.
            if (!string.IsNullOrEmpty(Alias) && Alias != Label && Alias != ElementName)
                yield return Alias;
        }
    }

    public bool IsPage => FieldType == "Page";

    /// <summary>The page this node sits on, or null for the form root and the pages themselves.</summary>
    public TemplateNode? Page
    {
        get
        {
            for (var n = Parent; n is not null; n = n.Parent)
                if (n.IsPage) return n;
            return null;
        }
    }

    /// <summary>This node and everything beneath it, depth first, parents before children.</summary>
    public IEnumerable<TemplateNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var n in child.DescendantsAndSelf())
                yield return n;
    }

    public IEnumerable<TemplateNode> Descendants()
        => DescendantsAndSelf().Skip(1);

    /// <summary>Depth below the form root. The root is 0, pages are 1.</summary>
    public int Depth
    {
        get
        {
            var d = 0;
            for (var n = Parent; n is not null; n = n.Parent) d++;
            return d;
        }
    }

    public override string ToString() => $"{FieldType} {Label}";
}
