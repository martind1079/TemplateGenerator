using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TemplateGenerator.App.Models;
using TemplateGenerator.App.Services;

namespace TemplateGenerator.App.ViewModels;

/// <summary>The detail-area table of every template converted into this build.</summary>
public partial class TemplateListViewModel : BaseViewModel
{
	private readonly ITemplateCatalog _catalog;
	private readonly INavigationService _navigation;

	public TemplateListViewModel(ITemplateCatalog catalog, INavigationService navigation)
	{
		_catalog = catalog;
		_navigation = navigation;
		Title = "Templates";
	}

	public ObservableCollection<FormTemplateInfo> Templates { get; } = [];

	public bool IsEmpty => Templates.Count == 0;

	[RelayCommand]
	public async Task LoadAsync()
	{
		if (Templates.Count > 0)
		{
			return;
		}

		IsBusy = true;

		try
		{
			foreach (var template in await _catalog.GetTemplatesAsync())
			{
				Templates.Add(template);
			}

			OnPropertyChanged(nameof(IsEmpty));
		}
		finally
		{
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task OpenTemplateAsync(FormTemplateInfo? template)
	{
		if (template is not null)
		{
			await _navigation.GoToAsync(template.EntryRoute);
		}
	}
}
