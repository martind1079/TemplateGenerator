using FormWorks.Cli;
using FormWorks.Templates.Analysis;
using FormWorks.Templates.Model;
using FormWorks.Templates.Parsing;

var args1 = new Args(args);

try
{
    return args1.Command switch
    {
        "check" => Commands.Check(args1),
        "inventory" => Commands.Inventory(args1),
        "graph" => Commands.Graph(args1),
        "routes" => Commands.Routes(args1),
        "generate" => Commands.Generate(args1),
        "worklist" => Commands.Worklist(args1),
        "prefixes" => Commands.Prefixes(args1),
        "shapes" => Commands.Shapes(args1),
        "coverage" => Commands.Coverage(args1),
        "validation" => Commands.Validation(args1),
        "visibility" => Commands.Visibility(args1),
        "computed" => Commands.Computed(args1),
        "repeats" => Commands.Repeats(args1),
        "list" => Commands.List(args1),
        _ => Commands.Help()
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
