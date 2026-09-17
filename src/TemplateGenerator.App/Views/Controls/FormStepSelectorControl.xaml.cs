using TemplateGenerator.App.Models.JobForms;
using System.Collections;
using System.Windows.Input;

namespace TemplateGenerator.App.Views.Controls;

/// <summary>
/// Horizontal strip of form pages, showing which one the agent is on and letting
/// them jump between the pages that are reachable.
/// </summary>
public partial class FormStepSelectorControl : ContentView
{
    public static readonly BindableProperty StepsProperty =
        BindableProperty.Create(
            nameof(Steps),
            typeof(IEnumerable),
            typeof(FormStepSelectorControl),
            propertyChanged: OnStepsChanged);

    public static readonly BindableProperty StepCommandProperty =
        BindableProperty.Create(
            nameof(StepCommand),
            typeof(ICommand),
            typeof(FormStepSelectorControl));

    public static readonly BindableProperty ValidateCommandProperty =
        BindableProperty.Create(
            nameof(ValidateCommand),
            typeof(ICommand),
            typeof(FormStepSelectorControl),
            propertyChanged: OnValidateCommandChanged);

    private static readonly BindablePropertyKey HasValidateCommandKey =
        BindableProperty.CreateReadOnly(
            nameof(HasValidateCommand), typeof(bool), typeof(FormStepSelectorControl), false);

    public static readonly BindableProperty HasValidateCommandProperty = HasValidateCommandKey.BindableProperty;

    public IEnumerable? Steps
    {
        get => (IEnumerable?)GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    /// <summary>Receives the tapped <see cref="FormStep"/> as its parameter.</summary>
    public ICommand? StepCommand
    {
        get => (ICommand?)GetValue(StepCommandProperty);
        set => SetValue(StepCommandProperty, value);
    }

    /// <summary>
    /// Runs every page's rules and shows what is wrong with the report so far, without
    /// needing a submit flow to exist first. Optional: generated pages bind their view
    /// model's ValidateAllCommand here, and a hand-written page that binds nothing simply
    /// shows no button, rather than one wired to nothing.
    /// </summary>
    public ICommand? ValidateCommand
    {
        get => (ICommand?)GetValue(ValidateCommandProperty);
        set => SetValue(ValidateCommandProperty, value);
    }

    public bool HasValidateCommand => (bool)GetValue(HasValidateCommandProperty);

    public FormStepSelectorControl()
    {
        InitializeComponent();
    }

    // Assigned here rather than bound in XAML, because x:Reference back to this
    // control is the construct that does not survive a DataTemplate namescope.
    private static void OnStepsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is FormStepSelectorControl control)
            BindableLayout.SetItemsSource(control.StepsHost, newValue as IEnumerable);
    }

    private static void OnValidateCommandChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is FormStepSelectorControl control)
            control.SetValue(HasValidateCommandKey, newValue is not null);
    }

    // The step arrives as the button's own BindingContext, so the command needs no
    // CommandParameter and no reference across the template boundary.
    private void OnStepClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: FormStep step }
            && StepCommand?.CanExecute(step) == true)
        {
            StepCommand.Execute(step);
        }
    }
}
