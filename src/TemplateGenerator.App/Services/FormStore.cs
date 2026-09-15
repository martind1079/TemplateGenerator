namespace TemplateGenerator.App.Services;

/// <summary>
/// Holds the report a converted form is filling in.
///
/// Generated view models are transient — a new one is built every time the agent navigates —
/// so anything held on them is lost between pages. The report is a singleton and outlives
/// them, which is what lets a sixteen-page form keep its answers.
///
/// Generic and written once: only the report classes are generated, one per template, so
/// converting another template adds no plumbing here.
/// </summary>
public interface IFormStore<out TReport> where TReport : class, new()
{
	TReport Current { get; }

	/// <summary>Starts a fresh report, discarding whatever was being filled in.</summary>
	void Reset();
}

public sealed class FormStore<TReport> : IFormStore<TReport> where TReport : class, new()
{
	public TReport Current { get; private set; } = new();

	public void Reset() => Current = new();
}
