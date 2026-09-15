using CommunityToolkit.Mvvm.ComponentModel;

namespace TemplateGenerator.App.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
	[ObservableProperty]
	private string _title = string.Empty;

	[ObservableProperty]
	private bool _isBusy;
}
