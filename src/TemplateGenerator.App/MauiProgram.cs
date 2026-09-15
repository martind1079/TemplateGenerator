using Microsoft.Extensions.Logging;
using TemplateGenerator.App.Handlers;
using TemplateGenerator.App.Services;
using TemplateGenerator.App.ViewModels;
using TemplateGenerator.App.Views;

namespace TemplateGenerator.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder
			.UseMauiApp<App>()
			.UseHouseInputAppearance()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<ITemplateCatalog, GeneratedTemplateCatalog>();
		builder.Services.AddSingleton<INavigationService, ShellNavigationService>();

		builder.Services.AddSingleton<FlyoutMenuViewModel>();
		builder.Services.AddSingleton<TemplateListViewModel>();
		builder.Services.AddSingleton<AboutViewModel>();

		builder.Services.AddSingleton<TemplateListPage>();
		builder.Services.AddSingleton<AboutPage>();

		// Every converted template registers its own pages, view models and report store.
		// Nothing here names an individual template.
		builder.Services.AddGeneratedTemplates();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
