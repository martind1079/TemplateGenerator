namespace TemplateGenerator.App.Services;

/// <summary>
/// Service locator for the few places XAML instantiates a type directly (Shell flyout content),
/// where constructor injection is not available.
/// </summary>
public static class ServiceHelper
{
	public static IServiceProvider Services =>
		IPlatformApplication.Current?.Services
		?? throw new InvalidOperationException("The MAUI service provider is not available yet.");

	public static T GetRequiredService<T>() where T : notnull
		=> (T)Services.GetService(typeof(T))!;
}
