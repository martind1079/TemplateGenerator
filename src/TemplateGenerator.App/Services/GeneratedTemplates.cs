using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace TemplateGenerator.App.Services;

/// <summary>One page of a converted template, as the generated routes class lists it.</summary>
public sealed record GeneratedPage(string Title, string Route);

/// <summary>A template that has been converted and compiled into this build.</summary>
public sealed record GeneratedTemplate
{
	public required string Id { get; init; }

	/// <summary>The template as the estate names it, e.g. "Occupancy Report V18".</summary>
	public required string Name { get; init; }

	/// <summary>Where the form starts: the template's first page.</summary>
	public required string EntryRoute { get; init; }

	public required IReadOnlyList<GeneratedPage> Pages { get; init; }
}

/// <summary>
/// Finds the templates the converter has emitted into this app.
///
/// By reflection rather than a table somebody maintains: converting a template already means
/// rebuilding, and a list that has to be edited by hand is a list that is one conversion out
/// of date. Every generated routes class states its own name, routes and registrations, so
/// this reads them and the app needs no knowledge of any individual template.
/// </summary>
public static class GeneratedTemplates
{
	private static readonly IReadOnlyList<Type> RoutesTypes = Discover();

	public static IReadOnlyList<GeneratedTemplate> All { get; } = RoutesTypes.Select(Describe).ToList();

	/// <summary>Registers each template's pages, view models and report store.</summary>
	public static IServiceCollection AddGeneratedTemplates(this IServiceCollection services)
	{
		foreach (var type in RoutesTypes)
			type.GetMethod("AddGeneratedPages", BindingFlags.Public | BindingFlags.Static)
				?.Invoke(null, [services]);

		return services;
	}

	/// <summary>
	/// Gives every generated page an absolute route. Called once, as the shell is built:
	/// the pages are hidden from the flyout, which lists templates rather than pages.
	/// </summary>
	public static void RegisterShellContent(Shell shell, IServiceProvider services)
	{
		foreach (var type in RoutesTypes)
			type.GetMethod("RegisterShellContent", BindingFlags.Public | BindingFlags.Static)
				?.Invoke(null, [shell, services]);
	}

	private static IReadOnlyList<Type> Discover()
		=> typeof(GeneratedTemplates).Assembly
			.GetTypes()
			.Where(t => t is { IsAbstract: true, IsSealed: true, IsPublic: true }
			            && t.Namespace?.EndsWith(".Views.Generated", StringComparison.Ordinal) == true
			            && t.Name.EndsWith("Routes", StringComparison.Ordinal)
			            && t.GetField("TemplateName", BindingFlags.Public | BindingFlags.Static) is not null)
			.OrderBy(t => t.Name, StringComparer.Ordinal)
			.ToList();

	private static GeneratedTemplate Describe(Type type) => new()
	{
		Id = type.Name[..^"Routes".Length],
		Name = Constant(type, "TemplateName") ?? type.Name,
		EntryRoute = Constant(type, "EntryRoute") ?? string.Empty,
		Pages = ReadPages(type),
	};

	private static string? Constant(Type type, string name)
		=> type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string;

	/// <summary>
	/// The routes class lists its pages as (Title, Route) tuples. Read through ITuple rather
	/// than by field name, because a ValueTuple's fields are Item1 and Item2 whatever the
	/// element names say.
	/// </summary>
	private static IReadOnlyList<GeneratedPage> ReadPages(Type type)
	{
		if (type.GetProperty("Pages", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is not IEnumerable pages)
			return [];

		return pages
			.Cast<ITuple>()
			.Select(t => new GeneratedPage((string)t[0]!, (string)t[1]!))
			.ToList();
	}
}
