namespace FormWorks.Cli;

/// <summary>
/// A small option reader. Hand rolled so the tool keeps its no-package promise and
/// builds on a machine that cannot reach NuGet.
/// </summary>
public sealed class Args
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public string Command { get; }
    public IReadOnlyList<string> Positional { get; }

    public Args(string[] argv)
    {
        Command = argv.Length > 0 && !argv[0].StartsWith('-') ? argv[0] : "help";

        var positional = new List<string>();
        for (var i = Command == "help" ? 0 : 1; i < argv.Length; i++)
        {
            var a = argv[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) { positional.Add(a); continue; }

            var name = a[2..];
            var next = i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? argv[++i]
                : null;
            _options[name] = next;
        }
        Positional = positional;
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Value(string name) => _options.GetValueOrDefault(name);

    public int Int(string name, int fallback)
        => _options.TryGetValue(name, out var v) && int.TryParse(v, out var i) ? i : fallback;
}
