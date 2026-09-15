using TemplateGenerator.App.Models;

namespace TemplateGenerator.App.Services;

/// <summary>
/// The template table, read from what the converter has emitted into this build.
///
/// There is no hand-written catalogue: a template appears here because its generated routes
/// class is compiled in, and disappears when it is deleted.
/// </summary>
public sealed class GeneratedTemplateCatalog : ITemplateCatalog
{
	private readonly IReadOnlyList<FormTemplateInfo> _templates = GeneratedTemplates.All
		.Select(t => new FormTemplateInfo
		{
			Id = t.Id,
			Name = t.Name,
			EntryRoute = t.EntryRoute,
			PageTitles = t.Pages.Select(p => p.Title).ToList(),
		})
		.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
		.ToList();

	public Task<IReadOnlyList<FormTemplateInfo>> GetTemplatesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(_templates);

	public Task<FormTemplateInfo?> GetTemplateAsync(string id, CancellationToken cancellationToken = default)
		=> Task.FromResult(_templates.FirstOrDefault(t => t.Id == id));
}
