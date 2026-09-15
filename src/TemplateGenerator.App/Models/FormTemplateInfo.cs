namespace TemplateGenerator.App.Models;

/// <summary>
/// A converted template, as the menu lists it. Everything here is read from the converter's
/// own output rather than written down anywhere by hand.
/// </summary>
public sealed class FormTemplateInfo
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	/// <summary>Where the form starts. Selecting the template navigates here.</summary>
	public required string EntryRoute { get; init; }

	public required IReadOnlyList<string> PageTitles { get; init; }

	public int PageCount => PageTitles.Count;

	public string PageSummary => PageCount == 1 ? "1 page" : $"{PageCount} pages";
}
