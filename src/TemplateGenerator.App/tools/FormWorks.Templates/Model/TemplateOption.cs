namespace FormWorks.Templates.Model;

/// <summary>
/// One choice on a selection control.
///
/// FormWorks stores these as { "Value": ..., "Content": ... }. The two are equal in
/// every template read so far, but they are kept apart because routing scripts
/// compare against <see cref="Value"/> and a generator must bind the same one.
/// </summary>
/// <param name="Value">What the script sees in <c>Field.value</c>.</param>
/// <param name="Content">What the agent sees on screen.</param>
public sealed record TemplateOption(string Value, string Content);
