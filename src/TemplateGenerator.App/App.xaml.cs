namespace TemplateGenerator.App;

public partial class App : Application
{
	public App() => InitializeComponent();

	protected override Window CreateWindow(IActivationState? activationState)
		=> new(new AppShell())
		{
			Title = "Template Generator",
			Width = 1180,
			Height = 820,
		};
}
