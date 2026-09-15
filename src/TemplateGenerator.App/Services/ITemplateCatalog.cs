using TemplateGenerator.App.Models;

namespace TemplateGenerator.App.Services;

/// <summary>
/// Source of the template table shown in the side menu. The in-memory implementation stands in
/// for the generator's manifest until the real tool is wired in.
/// </summary>
public interface ITemplateCatalog
{
	Task<IReadOnlyList<FormTemplateInfo>> GetTemplatesAsync(CancellationToken cancellationToken = default);

	Task<FormTemplateInfo?> GetTemplateAsync(string id, CancellationToken cancellationToken = default);
}
