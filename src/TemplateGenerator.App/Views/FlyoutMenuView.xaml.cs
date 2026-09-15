using TemplateGenerator.App.Services;
using TemplateGenerator.App.ViewModels;

namespace TemplateGenerator.App.Views;

/// <summary>
/// Content of the Shell flyout. Shell instantiates this from XAML, so the view model comes from the
/// service provider rather than the constructor.
/// </summary>
public partial class FlyoutMenuView : ContentView
{
	private readonly FlyoutMenuViewModel _viewModel;

	public FlyoutMenuView()
	{
		InitializeComponent();
		BindingContext = _viewModel = ServiceHelper.GetRequiredService<FlyoutMenuViewModel>();
		Loaded += async (_, _) => await _viewModel.LoadAsync();
	}
}
