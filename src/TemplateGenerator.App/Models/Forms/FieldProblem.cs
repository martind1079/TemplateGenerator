namespace TemplateGenerator.App.Models.Forms;

/// <summary>Why one incoming value could not be applied to a report.</summary>
public enum FieldProblemKind
{
	/// <summary>The key names no field on this template.</summary>
	UnknownField,

	/// <summary>The key names a field, but the value is not of its type.</summary>
	UnreadableValue,
}

/// <summary>
/// One value that did not make it onto the report.
///
/// Collected and returned rather than thrown or ignored: throwing loses a whole job for one
/// bad field, and ignoring loads a job that looks complete with a field quietly empty.
/// </summary>
/// <param name="Field">The fully qualified FormWorks name as it arrived.</param>
/// <param name="Value">The value that arrived, so a bad export can be identified from a log.</param>
public sealed record FieldProblem(FieldProblemKind Kind, string Field, string? Value, string Detail)
{
	public override string ToString() => $"{Field}: {Detail}";
}
