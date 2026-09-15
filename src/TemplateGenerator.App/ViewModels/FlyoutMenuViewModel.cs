using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TemplateGenerator.App.Models;
using TemplateGenerator.App.Services;

namespace TemplateGenerator.App.ViewModels;

/// <summary>
/// Drives the retractable side menu: the converted templates at the top and the fixed
/// destinations underneath them.
/// </summary>
public partial class FlyoutMenuViewModel : BaseViewModel
{
	private readonly ITemplateCatalog _catalog;
	private readonly INavigationService _navigation;

	[ObservableProperty]
	private FormTemplateInfo? _selectedTemplate;

	public FlyoutMenuViewModel(ITemplateCatalog catalog, INavigationService navigation)
	{
		_catalog = catalog;
		_navigation = navigation;
		Title = "Template Generator";
	}

	public ObservableCollection<FormTemplateInfo> Templates { get; } = [];

	public bool HasTemplates => Templates.Count > 0;

	public IReadOnlyList<MenuEntry> MenuEntries { get; } =
	[
		new MenuEntry { Title = "Templates", Subtitle = "What has been converted", Route = $"//{Routes.Templates}" },
		new MenuEntry { Title = "About", Subtitle = "What this demo shows", Route = $"//{Routes.About}" },
	];

	[RelayCommand]
	public async Task LoadAsync()
	{
		if (Templates.Count > 0)
		{
			return;
		}

		foreach (var template in await _catalog.GetTemplatesAsync())
		{
			Templates.Add(template);
		}

		OnPropertyChanged(nameof(HasTemplates));
	}

	[RelayCommand]
	private async Task OpenTemplateAsync(FormTemplateInfo? template)
	{
		if (template is null)
		{
			return;
		}

		SelectedTemplate = template;
		await _navigation.GoToAsync(template.EntryRoute);
	}

	[RelayCommand]
	private async Task OpenMenuEntryAsync(MenuEntry? entry)
	{
		if (entry is null)
		{
			return;
		}

		SelectedTemplate = null;
		await _navigation.GoToAsync(entry.Route);
	}
}
