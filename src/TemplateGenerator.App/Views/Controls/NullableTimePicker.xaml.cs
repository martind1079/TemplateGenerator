namespace TemplateGenerator.App.Views.Controls;

/// <summary>
/// A time field that can be unanswered.
///
/// MAUI's TimePicker defaults to midnight and has no empty state, so an untouched field
/// reads as an answer of 00:00. The same problem as <see cref="NullableDatePicker"/>, and
/// the same shape of answer: the empty state covers the control, and tapping it answers.
///
/// TimePicker raises no selection event of its own, so the time is read from a property
/// change on the inner control.
/// </summary>
public partial class NullableTimePicker : ContentView
{
	public static readonly BindableProperty TimeProperty =
		BindableProperty.Create(nameof(Time), typeof(TimeSpan?), typeof(NullableTimePicker),
			defaultBindingMode: BindingMode.TwoWay, propertyChanged: OnTimeChanged);

	/// <summary>
	/// Shown while the field is unanswered. The shape of the answer rather than an
	/// instruction, because the glyph beside it already says what the field is.
	/// </summary>
	public static readonly BindableProperty PlaceholderProperty =
		BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(NullableTimePicker), "HH:MM");

	private static readonly BindablePropertyKey HasValueKey =
		BindableProperty.CreateReadOnly(nameof(HasValue), typeof(bool), typeof(NullableTimePicker), false);

	private static readonly BindablePropertyKey IsEmptyKey =
		BindableProperty.CreateReadOnly(nameof(IsEmpty), typeof(bool), typeof(NullableTimePicker), true);

	public static readonly BindableProperty HasValueProperty = HasValueKey.BindableProperty;

	public static readonly BindableProperty IsEmptyProperty = IsEmptyKey.BindableProperty;

	/// <summary>
	/// Raised when the inner picker loses focus, hiding <see cref="VisualElement.Unfocused"/>.
	/// See the note on <see cref="NullableDatePicker.Unfocused"/>.
	/// </summary>
	public new event EventHandler<FocusEventArgs>? Unfocused;

	/// <summary>What the inner picker shows once there is an answer.</summary>
	private const string AnsweredFormat = "HH:mm";

	/// <summary>
	/// And what it shows before then: nothing. A quoted literal, because a format string of
	/// one character is read as a standard specifier and throws.
	/// </summary>
	private const string BlankFormat = "' '";

	public NullableTimePicker()
	{
		InitializeComponent();
		Picker.Format = BlankFormat;

#if WINDOWS
		// See the note in NullableDatePicker's constructor: WinUI's TimePicker always renders
		// a real hour/minute/AM-PM, so the placeholder needs an opaque fill to cover it.
		PlaceholderLabel.SetAppThemeColor(Label.BackgroundColorProperty,
			(Color)Application.Current!.Resources["PageBackground"],
			(Color)Application.Current!.Resources["SurfaceAltDark"]);
#endif
	}

	public TimeSpan? Time
	{
		get => (TimeSpan?)GetValue(TimeProperty);
		set => SetValue(TimeProperty, value);
	}

	public string Placeholder
	{
		get => (string)GetValue(PlaceholderProperty);
		set => SetValue(PlaceholderProperty, value);
	}

	public bool HasValue => (bool)GetValue(HasValueProperty);

	public bool IsEmpty => (bool)GetValue(IsEmptyProperty);

	private static void OnTimeChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not NullableTimePicker control) return;

		var time = newValue as TimeSpan?;
		control.SetValue(HasValueKey, time.HasValue);
		control.SetValue(IsEmptyKey, !time.HasValue);
		control.Picker.Format = time.HasValue ? AnsweredFormat : BlankFormat;

		if (time.HasValue && control.Picker.Time != time.Value)
			control.Picker.Time = time.Value;
	}

	private void OnSetRequested(object? sender, TappedEventArgs e)
	{
		Time = Picker.Time;
		Picker.Focus();
	}

	// Only once the field has been answered: before that the inner control is covered, and
	// its default of midnight is not an answer.
	private void OnPickerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == TimePicker.TimeProperty.PropertyName && HasValue)
			Time = Picker.Time;
	}

	private void OnClear(object? sender, TappedEventArgs e) => Time = null;

	private void OnPickerUnfocused(object? sender, FocusEventArgs e) => Unfocused?.Invoke(this, e);
}
