namespace TemplateGenerator.App.ViewModels;

public partial class AboutViewModel : BaseViewModel
{
	public AboutViewModel() => Title = "About";

	public string Summary =>
		"This app showcases a converter that turns a legacy Formworks JSON template into a native .NET MAUI form — " +
		"XAML, view model and supporting files — rather than interpreting the JSON at runtime.";

	public IReadOnlyList<string> Highlights { get; } =
	[
		"Generated forms are ordinary compiled MAUI pages, so they get native controls and full tooling support.",
		"The app knows no individual template: it reads what the converter emitted and lists it.",
		"Six house controls and three style keys are all a host app has to provide.",
		"Nothing is written unless every page verifies, so a template never half-converts.",
	];
}
