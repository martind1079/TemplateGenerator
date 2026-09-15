using System.Text.Json;
using FormWorks.Templates.Model;

namespace FormWorks.Templates.Parsing;

/// <summary>Reads a template.json into a <see cref="TemplateDocument"/>.</summary>
public static class TemplateLoader
{
    /// <summary>
    /// The node types seen across the estate. Anything outside this set is reported
    /// rather than skipped: an unrecognised control is exactly the sort of thing that
    /// would otherwise be dropped silently during conversion.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownFieldTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Form", "Page", "Section", "Group", "Label", "Line", "Image",
        "Text", "ParagraphText", "SingleSelection", "MultiSelection", "Checkbox",
        "Button", "Date", "Time", "Photo", "Signature", "Table", "TableCell"
    };

    public static TemplateDocument LoadFolder(string templateFolder)
    {
        var path = System.IO.Path.Combine(templateFolder, "template.json");
        return LoadFile(path, System.IO.Path.GetFileName(templateFolder.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))!);
    }

    public static TemplateDocument LoadFile(string templateJsonPath, string? folderName = null)
    {
        // ReadAllText strips a UTF-8 byte order mark; every file in the estate has one,
        // and JsonDocument.Parse rejects it outright.
        var text = File.ReadAllText(templateJsonPath);

        var name = folderName
            ?? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(templateJsonPath))
            ?? templateJsonPath;

        return LoadJson(text, name, templateJsonPath);
    }

    /// <summary>Reads a template from text. The path is carried for reporting only.</summary>
    public static TemplateDocument LoadJson(string text, string name, string path = "")
    {
        // A byte order mark survives when the text did not come from ReadAllText.
        text = text.TrimStart('\uFEFF');

        // Cloned so the backing document detaches from the using-scope. Raw elements are
        // held on every node, and reading one after the parse document was disposed
        // throws.
        JsonElement root;
        using (var doc = JsonDocument.Parse(text))
            root = doc.RootElement.Clone();

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{name}: root is {root.ValueKind}, expected an object.");

        var wrapper = root.EnumerateObject().FirstOrDefault();
        if (wrapper.Value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{name}: root object does not wrap a node.");

        var node = ReadNode(wrapper.Name, wrapper.Value);
        var (family, version) = TemplateDocument.SplitVersion(name);

        return new TemplateDocument
        {
            FolderName = name,
            Path = path,
            Root = node,
            Family = family,
            Version = version
        };
    }

    private static TemplateNode ReadNode(string fieldType, JsonElement e)
    {
        var children = new List<TemplateNode>();
        if (e.TryGetProperty("children", out var kids) && kids.ValueKind == JsonValueKind.Array)
        {
            foreach (var wrapped in kids.EnumerateArray())
            {
                if (wrapped.ValueKind != JsonValueKind.Object) continue;

                // Each child is { "<FieldType>": { ... } }. More than one key would mean
                // the shape is not what the reader assumes, so take them all rather than
                // guess which is the real one.
                foreach (var prop in wrapped.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                        children.Add(ReadNode(prop.Name, prop.Value));
            }
        }

        var node = new TemplateNode
        {
            FieldType = fieldType,
            DeclaredFieldType = Str(e, "fieldType"),
            Id = Int(e, "id"),
            Name = Str(e, "name"),
            ElementName = Str(e, "elementName"),
            Alias = Str(e, "alias"),
            Title = Str(e, "title"),
            Hidden = Bool(e, "hidden"),
            ReadOnly = Bool(e, "readOnly"),
            Required = Bool(e, "required"),
            Width = Int(e, "width"),
            TextLines = Int(e, "textLines"),
            ContentWidth = Int(e, "contentWidth"),
            ContentHeight = Int(e, "contentHeight"),
            AssignSpaceForTitle = Bool(e, "assignSpaceForTitle"),
            MarginLeft = Int(e, "marginLeft") ?? 0,
            MarginRight = Int(e, "marginRight") ?? 0,
            MarginTop = Int(e, "marginTop") ?? 0,
            MarginBottom = Int(e, "marginBottom") ?? 0,
            Options = ReadOptions(e),
            Scripts = ReadScripts(e),
            Children = children,
            Raw = e
        };

        foreach (var child in children) child.Parent = node;
        return node;
    }

    private static IReadOnlyList<TemplateOption> ReadOptions(JsonElement e)
    {
        if (!e.TryGetProperty("options", out var opts) || opts.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<TemplateOption>();
        foreach (var o in opts.EnumerateArray())
        {
            if (o.ValueKind != JsonValueKind.Object) continue;
            var value = Str(o, "Value") ?? Str(o, "value");
            var content = Str(o, "Content") ?? Str(o, "content");
            if (value is not null || content is not null)
                list.Add(new TemplateOption(value ?? content!, content ?? value!));
        }
        return list;
    }

    private static IReadOnlyDictionary<string, string> ReadScripts(JsonElement e)
    {
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in new[] { "scripts", "jsScripts" })
        {
            if (!e.TryGetProperty(key, out var s) || s.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in s.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var body = prop.Value.GetString();
                if (string.IsNullOrWhiteSpace(body)) continue;
                // jsScripts has been empty everywhere so far; prefix it if it ever is not,
                // so the two namespaces cannot collide in a report.
                var name = key == "jsScripts" ? "js:" + prop.Name : prop.Name;
                scripts[name] = body;
            }
        }
        return scripts;
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
