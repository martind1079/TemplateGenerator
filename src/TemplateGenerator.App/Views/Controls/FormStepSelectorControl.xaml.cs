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
