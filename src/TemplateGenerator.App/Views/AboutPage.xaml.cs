using TemplateGenerator.App.ViewModels;

namespace TemplateGenerator.App.Views;

public partial class AboutPage : ContentPage
{
	public AboutPage(AboutViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}
