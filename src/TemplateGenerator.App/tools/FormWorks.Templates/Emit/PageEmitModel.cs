using System.Text.RegularExpressions;
using FormWorks.Templates.Model;

namespace FormWorks.Templates.Emit;

/// <summary>
/// One thing on the page: an input bound to a model property, or a piece of static text
/// the template carries as instructions to the agent.
/// </summary>
public sealed record EmittedElement(
    TemplateNode Source,
    string? PropertyName,
    ControlMapping? Mapping,
    string Title,
    IReadOnlyList<string> Options,
    double? HeightRequest = null,
    double? ContentWidth = null,
    string? CommandName = null,
    string? ActionControl = null)
{
    public bool IsInput => Mapping is not null && PropertyName is not null;

    /// <summary>A button: it raises a command rather than holding an answer.</summary>
    public bool IsAction => CommandName is not null;
    public string BindingPath => PropertyName ?? "";
}

/// <summary>One cell of a row: a field, or a container laid out beside it.</summary>
public abstract record Cell;

public sealed record ElementCell(EmittedElement Element) : Cell;

public sealed record ContainerCell(EmittedGroup Group) : Cell;

/// <summary>Elements and containers laid out side by side, as the template lays them out.</summary>
public sealed record RowItem(IReadOnlyList<Cell> Cells, string ColumnDefinitions);

/// <summary>
/// A section or a group.
///
/// FormWorks treats both as layout containers with a declared width that their children
/// are laid out against. A group is how a section puts two columns side by side: the
/// customer details section holds two 490-point groups, and the financials section a
/// 330 beside a 650. Emitting containers one per row stacks those, which is a different
/// form from the one the template describes.
/// </summary>
public sealed record EmittedGroup(
    TemplateNode Source, string Title, string FieldType, IReadOnlyList<RowItem> Rows);

public sealed record PageEmitModel(
    TemplateDocument Document,
    TemplateNode Page,
    string PageName,
    string ClassName,
    string AnswersClassName,
    IReadOnlyList<RowItem> Rows,
    IReadOnlyList<EmittedElement> AllFields,
    IReadOnlyList<EmittedElement> Actions,
    IReadOnlyList<string> Skipped,
    ControlVocabulary Vocabulary)
{
    /// <summary>
    /// What each element on this page binds its shown and usable state to, keyed by the
    /// element's own label. Set after the visibility table is built.
    /// </summary>
    public IReadOnlyDictionary<string, VisibilityEmitter.StateBinding> StateNames { get; init; }
        = new Dictionary<string, VisibilityEmitter.StateBinding>(StringComparer.Ordinal);

    /// <summary>Fields this page validates. Set after the rules are recovered.</summary>
    public IReadOnlySet<string> Validated { get; init; } = new HashSet<string>();
}

/// <summary>
/// Turns one page of a template into the shape the emitters need.
///
/// The only real decision here is naming. Field names are unique within a page only by
/// their full path, but a property called JobSheetDetailsBorrower1Name is nobody's
/// friend. So the leaf name is used where it is unique and qualified with its parent
/// only where it collides, which is what a person would have written.
/// </summary>
public static partial class PageEmitModelBuilder
{
    [GeneratedRegex(@"[^A-Za-z0-9_]", RegexOptions.CultureInvariant)]
    private static partial Regex NonIdentifier();

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const",
        "continue","decimal","default","delegate","do","double","else","enum","event","explicit","extern",
        "false","finally","fixed","float","for","foreach","goto","if","implicit","in","int","interface",
        "internal","is","lock","long","namespace","new","null","object","operator","out","override",
        "params","private","protected","public","readonly","ref","return","sbyte","sealed","short",
        "sizeof","stackalloc","static","string","struct","switch","this","throw","true","try","typeof",
        "uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile","while"
    };

    public static PageEmitModel Build(
        TemplateDocument doc, TemplateNode page, ControlVocabulary vocabulary, string classPrefix)
    {
        var skipped = new List<string>();
        var candidates = new List<(TemplateNode Node, ControlMapping Mapping)>();
        var actionNodes = new List<TemplateNode>();
        Collect(page, vocabulary, candidates, actionNodes, skipped);

        var names = AssignNames(candidates.Select(c => c.Node).ToList());
        // Same treatment as field names: a page with one Next button keeps the plain name,
        // and one with several (a Next per branch, say) gets each qualified by its section
        // rather than colliding on a single NextCommand.
        var actionNames = AssignNames(actionNodes);
        var mappings = candidates.ToDictionary(c => c.Node, c => c.Mapping);

        EmittedElement Element(TemplateNode node)
        {
            if (vocabulary.IsAction(node.FieldType))
            {
                return new EmittedElement(
                    node, null, null, node.Title ?? "", [],
                    CommandName: actionNames[node] + "Command",
                    ActionControl: vocabulary.Actions[node.FieldType]);
            }

            if (!mappings.TryGetValue(node, out var mapping))
                return new EmittedElement(node, null, null, node.Title ?? "", []);

            return new EmittedElement(
                node, names[node], mapping,
                // No fallback to the element name. A field the template gives no title is
                // a field with no caption, and substituting "mandAM" for a missing one
                // puts an internal name in front of the agent.
                node.Title ?? "",
                mapping.UsesOptions ? node.Options.Select(o => o.Value).ToList() : [],
                mapping.UsesTextLines && node.TextLines is { } lines
                    ? vocabulary.HeightForLines(lines)
                    : node.ContentHeight is { } ch && ch > 0 ? ch : null,
                node.ContentWidth is { } cw && cw > 0 && cw != node.Width ? cw : null);
        }

        bool Renders(TemplateNode n)
            => mappings.ContainsKey(n)
               || vocabulary.IsAction(n.FieldType)
               || vocabulary.IsContainer(n.FieldType)
               || (vocabulary.IsDecoration(n.FieldType) && n.FieldType != "Image");

        var all = new List<EmittedElement>();
        var actions = new List<EmittedElement>();

        // Rows of a container. Children are packed against the container's own width, so
        // a 490-point group lays its fields out against 490 rather than the page's 980.
        List<RowItem> Rows(TemplateNode node)
        {
            var rows = new List<RowItem>();
            var renderable = node.Children.Where(Renders).ToList();

            foreach (var row in RowPacker.Pack(renderable, node.LayoutWidth, _ => true))
            {
                var cells = new List<Cell>();

                foreach (var child in row)
                {
                    if (vocabulary.IsContainer(child.FieldType))
                    {
                        var nested = Container(child);
                        if (nested is not null) cells.Add(new ContainerCell(nested));
                        continue;
                    }

                    var element = Element(child);
                    if (element.IsInput) all.Add(element);
                    if (element.IsAction) actions.Add(element);
                    cells.Add(new ElementCell(element));
                }

                if (cells.Count > 0)
                    rows.Add(new RowItem(cells, RowPacker.ColumnDefinitions(row, node.LayoutWidth)));
            }

            return rows;
        }

        EmittedGroup? Container(TemplateNode node)
        {
            var rows = Rows(node);

            // No fallback to the element name. An untitled container is untitled, and
            // showing "WhyNoPhotoEvidence" as a heading leaks an internal name at the
            // agent.
            return rows.Count == 0
                ? null
                : new EmittedGroup(node, node.Title ?? "", node.FieldType, rows);
        }

        var pageRows = Rows(page);
        var pageName = Identifier(page.ElementName ?? page.Label);

        return new PageEmitModel(
            doc, page, pageName,
            $"{classPrefix}{pageName}Page",
            $"{classPrefix}{pageName}Answers",
            pageRows, all, actions, skipped, vocabulary);
    }

    private static void Collect(
        TemplateNode node,
        ControlVocabulary vocabulary,
        List<(TemplateNode, ControlMapping)> into,
        List<TemplateNode> actions,
        List<string> skipped)
    {
        foreach (var child in node.Children)
        {
            if (vocabulary.TryGetInput(child.FieldType, out var mapping))
                into.Add((child, mapping));
            else if (vocabulary.IsAction(child.FieldType))
                actions.Add(child);
            else if (vocabulary.Deferred.TryGetValue(child.FieldType, out var reason))
                skipped.Add($"{child.Label} ({child.FieldType}): {reason}");
            else if (!vocabulary.IsContainer(child.FieldType)
                     && !vocabulary.IsDecoration(child.FieldType))
                skipped.Add($"{child.Label} ({child.FieldType}): no mapping");

            Collect(child, vocabulary, into, actions, skipped);
        }
    }

    /// <summary>
    /// Leaf name where unique, qualified by ancestors where not, and a numeric suffix as
    /// the last resort so two fields can never share a property.
    /// </summary>
    private static Dictionary<TemplateNode, string> AssignNames(IReadOnlyList<TemplateNode> nodes)
    {
        var assigned = new Dictionary<TemplateNode, string>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        var byLeaf = nodes.GroupBy(n => Identifier(Leaf(n)), StringComparer.Ordinal);

        foreach (var group in byLeaf)
        {
            var list = group.ToList();
            if (list.Count == 1 && taken.Add(group.Key))
            {
                assigned[list[0]] = group.Key;
                continue;
            }

            foreach (var node in list)
            {
                var name = Qualify(node, taken);
                taken.Add(name);
                assigned[node] = name;
            }
        }

        return assigned;
    }

    private static string Qualify(TemplateNode node, HashSet<string> taken)
    {
        var parts = new List<string> { Identifier(Leaf(node)) };

        for (var p = node.Parent; p is not null && !p.IsPage; p = p.Parent)
        {
            parts.Insert(0, Identifier(Leaf(p)));
            var candidate = string.Concat(parts);
            if (!taken.Contains(candidate)) return candidate;
        }

        var baseName = string.Concat(parts);
        var n = 2;
        while (taken.Contains($"{baseName}{n}")) n++;
        return $"{baseName}{n}";
    }

    private static string Leaf(TemplateNode node)
        => node.ElementName ?? node.Name?.Split('.').Last() ?? node.FieldType;

    public static string Identifier(string raw)
    {
        var cleaned = NonIdentifier().Replace(raw, "");
        if (cleaned.Length == 0) cleaned = "Field";
        if (char.IsDigit(cleaned[0])) cleaned = "_" + cleaned;
        cleaned = char.ToUpperInvariant(cleaned[0]) + cleaned[1..];
        return Keywords.Contains(cleaned) ? "@" + cleaned : cleaned;
    }
}
