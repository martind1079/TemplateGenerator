namespace TemplateGenerator.App.Services;

public interface INavigationService
{
	Task GoToAsync(string route);

	Task ShowAlertAsync(string title, string message, string cancel = "OK");
}
