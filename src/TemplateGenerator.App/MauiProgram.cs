using Microsoft.Extensions.Logging;
using TemplateGenerator.App.Handlers;
using TemplateGenerator.App.Services;
using TemplateGenerator.App.ViewModels;
using TemplateGenerator.App.Views;

namespace TemplateGenerator.App;

public static class MauiProgram
{
#if WINDOWS
	[System.Runtime.InteropServices.DllImport("kernel32.dll")]
	private static extern bool AllocConsole();
#endif

	public static MauiApp CreateMauiApp()
	{
#if WINDOWS
		// A MAUI Windows app has no console of its own, so Console.WriteLine (the Submit
		// button's report preview, for one) otherwise goes nowhere. This opens a real
		// console window alongside the app for the lifetime of the process.
		//
		// AllocConsole alone is not enough: Console.Out was already bound to this
		// process's (nonexistent) startup handles before the console existed, so without
		// re-pointing it, writes still silently go nowhere even once the window is on screen.
		AllocConsole();
		var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
		Console.SetOut(stdout);
#endif

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
