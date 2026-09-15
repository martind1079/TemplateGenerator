namespace TemplateGenerator.App.Services;

public sealed class ShellNavigationService : INavigationService
{
	public Task GoToAsync(string route)
	{
		var shell = Shell.Current;
		if (shell is null || string.IsNullOrWhiteSpace(route))
		{
			return Task.CompletedTask;
		}

		shell.FlyoutIsPresented = false;
		return shell.GoToAsync(route);
	}

	public Task ShowAlertAsync(string title, string message, string cancel = "OK")
	{
		var page = Shell.Current?.CurrentPage;
		return page is null ? Task.CompletedTask : page.DisplayAlertAsync(title, message, cancel);
	}
}
