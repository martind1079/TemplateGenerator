using CommunityToolkit.Mvvm.ComponentModel;

namespace TemplateGenerator.App.Models.JobForms
{
    /// <summary>
    /// One page in a multi-page form, as shown in the step selector.
    ///
    /// A 16-page form needs a way to move around that is not just Next and Back —
    /// agents revisit pages as a conversation on a doorstep develops.
    /// </summary>
    public class FormStep : ObservableObject
    {
        private bool _isCurrent;
        private bool _isEnabled = true;
        private bool _isComplete;

        public string Title { get; init; } = string.Empty;

        public string Route { get; init; } = string.Empty;

        /// <summary>Short label for the selector strip.</summary>
        public string ShortTitle { get; init; } = string.Empty;

        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetProperty(ref _isCurrent, value);
        }

        /// <summary>False when the page is not reachable yet, e.g. no outcome chosen.</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>True once the agent has answered something on this page.</summary>
        public bool IsComplete
        {
            get => _isComplete;
            set => SetProperty(ref _isComplete, value);
        }
    }
}
