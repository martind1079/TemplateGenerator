namespace TemplateGenerator.App.Views.Controls;

/// <summary>
/// A titled panel around one section of a generated form.
///
/// The content is a bindable property rather than <see cref="ContentView.Content"/>, which
/// this control uses for its own chrome. <see cref="ContentPropertyAttribute"/> points XAML
/// at it, because generated markup nests the section's fields directly inside the element
/// rather than naming a property.
/// </summary>
[ContentProperty(nameof(SubSectionContent))]
public partial class SubSectionControl : ContentView
{
	public static readonly BindableProperty TitleProperty =
		BindableProperty.Create(nameof(Title), typeof(string), typeof(SubSectionControl), string.Empty);

	public static readonly BindableProperty SubSectionContentProperty =
		BindableProperty.Create(nameof(SubSectionContent), typeof(View), typeof(SubSectionControl));

	public SubSectionControl() => InitializeComponent();

	public string Title
	{
		get => (string)GetValue(TitleProperty);
		set => SetValue(TitleProperty, value);
	}

	public View? SubSectionContent
	{
		get => (View?)GetValue(SubSectionContentProperty);
		set => SetValue(SubSectionContentProperty, value);
	}
}
