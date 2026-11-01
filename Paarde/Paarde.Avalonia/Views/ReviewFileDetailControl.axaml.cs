using System.Collections.ObjectModel;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Paarde.Avalonia.ViewModels;
using Paarde.Core.Contracts.Services;
using Paarde.Core.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class ReviewFileDetailControl : UserControl
{
    private readonly IPromptResultStore _promptResultStore;
    private readonly IFileChangeApplyService _fileChangeApplyService;
    private readonly ILlmRunner _llmRunner;

    // Epic 25: cached once this control is attached to its host's visual tree (DataContext is
    // typically assigned before that, so FindAncestorOfType in RebuildSteps itself would miss it on
    // this control's very first materialization) and reused by every later RebuildSteps - e.g. when
    // Selected switches to a different file and the DataTemplate recycles this same instance.
    private TutorialViewModel? _owner;

    // T8.4.3: which step the Previous/Next buttons and progress indicator currently point at - reset
    // to the first step whenever Steps is rebuilt (new file selected), since there's no notion of a
    // "current step" that should survive switching files.
    private int _currentStepIndex;

    public ObservableCollection<TutorialStepViewModel> Steps { get; } = new();

    public ReviewFileDetailControl()
    {
        _promptResultStore = App.GetService<IPromptResultStore>();
        _fileChangeApplyService = App.GetService<IFileChangeApplyService>();
        _llmRunner = App.GetService<ILlmRunner>();
        InitializeComponent();

        StepsItemsControl.ItemsSource = Steps;
        DataContextChanged += (_, _) => RebuildSteps();

        // Epic 25: AttachedToVisualTree (rather than Loaded) is the earliest point FindAncestorOfType
        // can succeed - re-running RebuildSteps here covers this control's first materialization,
        // where DataContextChanged above already fired while it was still unattached and _owner was
        // still null.
        AttachedToVisualTree += (_, _) =>
        {
            // T10: still looks up TutorialView, exactly as TutorialDetailControl did - T11 retargets
            // this to ReviewPanelControl once that control is what actually hosts this one.
            _owner ??= this.FindAncestorOfType<TutorialView>()?.ViewModel;
            RebuildSteps();
        };

        // T8.4.5: applied once this control is attached to its host's visual tree, by which point
        // RebuildSteps (above) has already populated Steps for the jump's target file.
        Loaded += (_, _) => ApplyPendingStepTarget();
    }

    private void RebuildSteps()
    {
        Steps.Clear();
        _currentStepIndex = 0;

        if (DataContext is ProposedFileChangeViewModel proposedFileChange && _owner is { } owner)
        {
            var hunks = proposedFileChange.Hunks;

            for (var i = 0; i < hunks.Count; i++)
            {
                Steps.Add(new TutorialStepViewModel(hunks[i], proposedFileChange, _promptResultStore.WorkingDirectoryPath, _promptResultStore.SessionId, _fileChangeApplyService, _llmRunner, owner, stepNumber: i + 1, totalSteps: hunks.Count));
            }
        }

        RefreshStepNavigation();
    }

    // T8.4.5: scrolls to the step a TutorialJumpRequest targeted, if any. Finding the step by
    // reference within the just-rebuilt Steps (rather than trusting an index) is robust to Steps'
    // order matching Hunk's order, which RebuildSteps above already guarantees.
    private void ApplyPendingStepTarget()
    {
        if (this.FindAncestorOfType<TutorialView>()?.ViewModel.TakePendingStepTarget() is not { } target)
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

    private void OnPreviousStepButtonClick(object? sender, RoutedEventArgs e) => GoToStep(_currentStepIndex - 1);

    private void OnNextStepButtonClick(object? sender, RoutedEventArgs e) => GoToStep(_currentStepIndex + 1);

    private void GoToStep(int index)
    {
        if (index < 0 || index >= Steps.Count)
        {
            return;
        }

        _currentStepIndex = index;
        RefreshStepNavigation();
        StepsItemsControl.ScrollIntoView(index);
    }

    // T8.4.3: no step-navigation UI at all for a file with no resolved steps (nothing to move
    // between); otherwise keeps the Previous/Next buttons' enabled state, the "Step X of Y" label,
    // and the progress bar in sync with _currentStepIndex.
    private void RefreshStepNavigation()
    {
        StepNavigationPanel.IsVisible = Steps.Count > 0;

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
