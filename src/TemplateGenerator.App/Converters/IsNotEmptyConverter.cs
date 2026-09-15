using System.Globalization;

namespace TemplateGenerator.App.Converters;

public sealed class IsNotEmptyConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> !string.IsNullOrWhiteSpace(value as string);

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
