using System.Globalization;

namespace TemplateGenerator.App.Converters;

/// <summary>Page titles as one line, so a template's shape is readable from the table.</summary>
public sealed class JoinTitlesConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> value is IEnumerable<string> titles ? string.Join(" · ", titles) : string.Empty;

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
