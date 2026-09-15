using TemplateGenerator.App.ViewModels;

namespace TemplateGenerator.App.Views;

public partial class TemplateListPage : ContentPage
{
	private readonly TemplateListViewModel _viewModel;

	public TemplateListPage(TemplateListViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _viewModel = viewModel;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.LoadAsync();
	}
}
