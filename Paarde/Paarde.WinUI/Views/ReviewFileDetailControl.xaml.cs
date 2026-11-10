using System.Collections.ObjectModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Paarde.Core.Contracts.Services;
using Paarde.Core.ViewModels;
using Paarde.Presentation.ViewModels;

namespace Paarde.WinUI.Views;

public sealed partial class ReviewFileDetailControl : UserControl
{
    private readonly IPromptResultStore _promptResultStore;
    private readonly IFileChangeApplyService _fileChangeApplyService;
    private readonly ILlmRunner _llmRunner;

    // The hosting ReviewPanelControl's TutorialViewModel, found once this control is in the
    // visual tree (DataContext is usually assigned before that, so the first RebuildSteps from
    // DataContextChanged can't see it yet) and reused when the template recycles this instance for
    // a different file.
    private TutorialViewModel? _owner;

    // Which step Previous/Next and the progress indicator point at - reset to the first step
    // whenever Steps is rebuilt (new file selected).
    private int _currentStepIndex;

    public ObservableCollection<TutorialStepViewModel> Steps { get; } = new();

    public ProposedFileChangeViewModel? ViewModel { get; private set; }

    public ReviewFileDetailControl()
    {
        _promptResultStore = App.GetService<IPromptResultStore>();
        _fileChangeApplyService = App.GetService<IFileChangeApplyService>();
        _llmRunner = App.GetService<ILlmRunner>();
        InitializeComponent();

        StepsList.ItemsSource = Steps;
        DataContextChanged += (_, _) =>
        {
            if (!ReferenceEquals(ViewModel, DataContext))
            {
                ViewModel = DataContext as ProposedFileChangeViewModel;
                Bindings.Update();
                RebuildSteps();
            }
        };

        // Loaded is WinUI's earliest point the ancestor walk can succeed (Avalonia used
        // AttachedToVisualTree). T8.4.5: the pending step jump is applied after RebuildSteps has
        // populated Steps for the jump's target file.
        Loaded += (_, _) =>
        {
            _owner ??= FindAncestor<ReviewPanelControl>()?.ViewModel;
            RebuildSteps();
            ApplyPendingStepTarget();
        };
    }

    private T? FindAncestor<T>()
        where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is T match)
            {
                return match;
            }
        }

        return null;
    }

    private void RebuildSteps()
    {
        Steps.Clear();
        _currentStepIndex = 0;

        if (ViewModel is { } proposedFileChange && _owner is { } owner)
        {
            var hunks = proposedFileChange.Hunks;

            for (var i = 0; i < hunks.Count; i++)
            {
                Steps.Add(new TutorialStepViewModel(hunks[i], proposedFileChange, _promptResultStore.WorkingDirectoryPath, _promptResultStore.SessionId, _fileChangeApplyService, _llmRunner, owner, stepNumber: i + 1, totalSteps: hunks.Count));
            }
        }

        RefreshStepNavigation();
    }

    // Scrolls to the step a TutorialJumpRequest targeted, if any - found by reference within
    // the just-rebuilt Steps rather than trusting an index.
    private void ApplyPendingStepTarget()
    {
        if (_owner?.TakePendingStepTarget() is not { } target)
        {
            return;
        }

        for (var i = 0; i < Steps.Count; i++)
        {
            if (Steps[i].Hunk == target)
            {
                GoToStep(i);
                return;
            }
        }
    }

    private void OnPreviousStepButtonClick(object sender, RoutedEventArgs e) => GoToStep(_currentStepIndex - 1);

    private void OnNextStepButtonClick(object sender, RoutedEventArgs e) => GoToStep(_currentStepIndex + 1);

    private void GoToStep(int index)
    {
        if (index < 0 || index >= Steps.Count)
        {
            return;
        }

        _currentStepIndex = index;
        RefreshStepNavigation();
        StepsList.ScrollIntoView(Steps[index], ScrollIntoViewAlignment.Leading);
    }

    // No step-navigation UI at all for a file with no resolved steps; otherwise keeps the
    // buttons' enabled state, the "Step X of Y" label and the progress bar in sync.
    private void RefreshStepNavigation()
    {
        StepNavigationPanel.Visibility = Steps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (Steps.Count == 0)
        {
            return;
        }

        PreviousStepButton.IsEnabled = _currentStepIndex > 0;
        NextStepButton.IsEnabled = _currentStepIndex < Steps.Count - 1;
        StepNavigationLabel.Text = $"Step {_currentStepIndex + 1} of {Steps.Count}";
        StepProgressBar.Maximum = Steps.Count;
        StepProgressBar.Value = _currentStepIndex + 1;
    }
}
