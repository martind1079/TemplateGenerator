namespace TemplateGenerator.App.Views.Controls;

/// <summary>
/// A date field that can be unanswered.
///
/// MAUI's DatePicker has no empty state: it always shows a date, so an untouched field
/// cannot be told from one answered today. Harmless while nothing leaves the device, and
/// not harmless in an outbound payload, where today's date arrives as though the agent had
/// entered it.
///
/// Tapping the empty state is the act of answering, and it starts at today because that is
/// the likeliest answer on a visit. The clear affordance puts it back to unanswered.
/// </summary>
public partial class NullableDatePicker : ContentView
{
	public static readonly BindableProperty DateProperty =
		BindableProperty.Create(nameof(Date), typeof(DateTime?), typeof(NullableDatePicker),
			defaultBindingMode: BindingMode.TwoWay, propertyChanged: OnDateChanged);

	/// <summary>
	/// Shown while the field is unanswered. The shape of the answer rather than an
	/// instruction, because the glyph beside it already says what the field is.
	/// </summary>
	public static readonly BindableProperty PlaceholderProperty =
		BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(NullableDatePicker), "DD/MM/YYYY");

	private static readonly BindablePropertyKey HasValueKey =
		BindableProperty.CreateReadOnly(nameof(HasValue), typeof(bool), typeof(NullableDatePicker), false);

	private static readonly BindablePropertyKey IsEmptyKey =
		BindableProperty.CreateReadOnly(nameof(IsEmpty), typeof(bool), typeof(NullableDatePicker), true);

	public static readonly BindableProperty HasValueProperty = HasValueKey.BindableProperty;

	public static readonly BindableProperty IsEmptyProperty = IsEmptyKey.BindableProperty;

	/// <summary>
	/// Raised when the inner picker loses focus, hiding <see cref="VisualElement.Unfocused"/>.
	///
	/// Focus does not bubble out of a wrapped control, and generated pages put
	/// <c>Unfocused="OnAnswerLeft"</c> on this element for any field the template watches with
	/// an OnBlur handler. Without this the handler would be wired to an event that never
	/// fires, and the rule would silently never run.
	/// </summary>
	public new event EventHandler<FocusEventArgs>? Unfocused;

#if WINDOWS
	/// <summary>
	/// What the inner picker shows once there is an answer.
	///
	/// WinUI's CalendarDatePicker.DateFormat is not a .NET format string: it takes one of a
	/// fixed set of template tokens, so the "dd/MM/yyyy" used on iOS/Mac throws here.
	/// </summary>
	private const string AnsweredFormat = "shortdate";

	/// <summary>
	/// And what it shows before then. The quoted-literal blank trick used on iOS/Mac is also
	/// not valid template syntax on Windows; it does not matter what shows here because the
	/// empty-state Label fully covers the picker while IsEmpty is true.
	/// </summary>
	private const string BlankFormat = "day";
#else
	/// <summary>What the inner picker shows once there is an answer.</summary>
	private const string AnsweredFormat = "dd/MM/yyyy";

	/// <summary>
	/// And what it shows before then: nothing. A quoted literal, because a format string of
	/// one character is read as a standard specifier and throws.
	/// </summary>
	private const string BlankFormat = "' '";
#endif

	public NullableDatePicker()
	{
		InitializeComponent();
		Picker.Format = BlankFormat;

#if WINDOWS
		// WinUI's CalendarDatePicker always renders a real day/month/year, so the placeholder
		// needs an opaque fill to actually cover it. AppThemeBinding is set here, not in XAML,
		// because OnPlatform cannot host an AppThemeBinding as its per-platform value.
		PlaceholderLabel.SetAppThemeColor(Label.BackgroundColorProperty,
			(Color)Application.Current!.Resources["PageBackground"],
			(Color)Application.Current!.Resources["SurfaceAltDark"]);
#endif
	}

	public DateTime? Date
	{
		get => (DateTime?)GetValue(DateProperty);
		set => SetValue(DateProperty, value);
	}

	public string Placeholder
	{
		get => (string)GetValue(PlaceholderProperty);
		set => SetValue(PlaceholderProperty, value);
	}

	public bool HasValue => (bool)GetValue(HasValueProperty);

	/// <summary>
	/// The inverse, as its own property rather than a converter: a control that needs a
	/// converter resource its host may not declare fails only when it is shown.
	/// </summary>
	public bool IsEmpty => (bool)GetValue(IsEmptyProperty);

	private static void OnDateChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not NullableDatePicker control) return;

		var date = newValue as DateTime?;
		control.SetValue(HasValueKey, date.HasValue);
		control.SetValue(IsEmptyKey, !date.HasValue);
		control.Picker.Format = date.HasValue ? AnsweredFormat : BlankFormat;

		// Keep the underlying picker in step without raising DateSelected back at us.
		if (date.HasValue && control.Picker.Date != date.Value)
			control.Picker.Date = date.Value;
	}

	private void OnSetRequested(object? sender, TappedEventArgs e)
	{
		Date = Picker.Date;
		Picker.Focus();
	}

	private void OnDateSelected(object? sender, DateChangedEventArgs e) => Date = e.NewDate;

	private void OnClear(object? sender, TappedEventArgs e) => Date = null;

	// Sender is this control, not the inner picker: the page's handler reads the field's
	// AutomationId to know which answer it holds, and that is set on this element.
	private void OnPickerUnfocused(object? sender, FocusEventArgs e) => Unfocused?.Invoke(this, e);
}
