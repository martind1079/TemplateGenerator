namespace TemplateGenerator.App.Models;

/// <summary>A fixed (non-template) destination in the side menu.</summary>
public sealed class MenuEntry
{
	public required string Title { get; init; }

	public required string Route { get; init; }

	public string Subtitle { get; init; } = string.Empty;
}
