namespace TemplateGenerator.App.Views.Controls;

/// <summary>
/// The message under a field that is not valid.
///
/// A control rather than a label plus a converter, so that a generated page depends on no
/// converter resource its host might not declare, and so an error's appearance is decided in
/// one place rather than under every field of every generated page.
///
/// It takes no space when there is nothing to say, and says nothing until the form has been
/// submitted: FormWorks ran its rules at a submission trigger, so a form does not open
/// covered in red. The message itself stays current either way, so a field clears as soon as
/// it is answered.
/// </summary>
public partial class FormFieldError : ContentView
{
	public static readonly BindableProperty MessageProperty =
		BindableProperty.Create(nameof(Message), typeof(string), typeof(FormFieldError),
			propertyChanged: OnStateChanged);

	/// <summary>Whether messages are being shown at all. False until the form is checked.</summary>
	public static readonly BindableProperty IsArmedProperty =
		BindableProperty.Create(nameof(IsArmed), typeof(bool), typeof(FormFieldError),
			propertyChanged: OnStateChanged);

	private static readonly BindablePropertyKey HasMessageKey =
		BindableProperty.CreateReadOnly(nameof(HasMessage), typeof(bool), typeof(FormFieldError), false);

	public static readonly BindableProperty HasMessageProperty = HasMessageKey.BindableProperty;

	public FormFieldError() => InitializeComponent();

	public string? Message
	{
		get => (string?)GetValue(MessageProperty);
		set => SetValue(MessageProperty, value);
	}

	public bool IsArmed
	{
		get => (bool)GetValue(IsArmedProperty);
		set => SetValue(IsArmedProperty, value);
	}

	public bool HasMessage => (bool)GetValue(HasMessageProperty);

	private static void OnStateChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not FormFieldError control) return;

		control.SetValue(HasMessageKey, control.IsArmed && !string.IsNullOrWhiteSpace(control.Message));
	}
}
