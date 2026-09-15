namespace TemplateGenerator.App.Services;

/// <summary>
/// The app's own shell routes. Converted templates bring their own, generated alongside
/// their pages, and are reached through each template's EntryRoute rather than from here.
/// </summary>
public static class Routes
{
	public const string Templates = "templates";
	public const string About = "about";
}
