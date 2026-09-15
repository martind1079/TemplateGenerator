using TemplateGenerator.App.Services;

namespace TemplateGenerator.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Generated pages get their absolute routes here rather than in XAML: a template is
		// sixteen pages at the top end, and the set changes every time one is converted.
		GeneratedTemplates.RegisterShellContent(this, ServiceHelper.Services);
	}
}
