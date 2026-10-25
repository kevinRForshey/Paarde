using System.Collections.ObjectModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Controls;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Contracts.Services;
using Paarde.Core.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class TutorialDetailControl : UserControl
{
    private readonly IPromptResultStore _promptResultStore;
    private readonly IFileChangeApplyService _fileChangeApplyService;
    private readonly ILlmRunner _llmRunner;
    private readonly INavigationService _navigationService;

    // T8.4.3: which step the Previous/Next buttons and progress indicator currently point at - reset
    // to the first step whenever Steps is rebuilt (new file selected), since there's no notion of a
    // "current step" that should survive switching files.
    private int _currentStepIndex;

    public ObservableCollection<TutorialStepViewModel> Steps { get; } = new();

    public TutorialDetailControl()
    {
        _promptResultStore = App.GetService<IPromptResultStore>();
        _fileChangeApplyService = App.GetService<IFileChangeApplyService>();
        _llmRunner = App.GetService<ILlmRunner>();
        _navigationService = App.GetService<INavigationService>();
        InitializeComponent();

        StepsItemsControl.ItemsSource = Steps;
        DataContextChanged += (_, _) => RebuildSteps();

        // T8.4.5: applied once this control is attached to TutorialView's visual tree, by which
        // point RebuildSteps (above) has already populated Steps for the jump's target file.
        Loaded += (_, _) => ApplyPendingStepTarget();

        // CanUndo/CanRedo are plain CLR properties on AvaloniaEdit's TextEditor, not
        // AvaloniaProperties, so they can't be bound from XAML - TextChanged (which also fires on
        // Undo()/Redo() themselves) is the hook used to keep the buttons' enabled state in sync.
        Editor.TextChanged += (_, _) => RefreshUndoRedoButtons();
        RefreshUndoRedoButtons();

        Editor.DiffHunkActionRequested += OnDiffHunkActionRequested;

        // Epic 15: Editor's own DataContext is a ProposedFileChangeViewModel, which deliberately has
        // no ILlmRunner, so the request is forwarded up to the page-level TutorialViewModel (the
        // same ancestor lookup ApplyPendingStepTarget already uses) and the result is assigned back
        // directly rather than through an AXAML binding, since that VM isn't Editor's DataContext.
        Editor.GhostTextRequested += async (_, e) =>
        {
            if (this.FindAncestorOfType<TutorialView>()?.ViewModel is not { } tutorialViewModel)
            {
                return;
            }

            Editor.GhostTextSuggestion = await tutorialViewModel.RequestGhostTextCompletionAsync(e.TextBeforeCaret, e.TextAfterCaret);
        };
    }

    // T13.1.3/T13.2.2: the gutter's Accept/Reject buttons are an alternate entry point into the
    // same per-step selection the checkboxes in StepsItemsControl below already drive - this just
    // forwards to it rather than introducing a second selection model.
    private void OnDiffHunkActionRequested(object? sender, DiffHunkAcceptRejectEventArgs e)
    {
        if (DataContext is ProposedFileChangeViewModel proposedFileChange)
        {
            proposedFileChange.SetDiffHunkSelection(e.Hunk, e.Accept);
        }
    }

    private void RefreshUndoRedoButtons()
    {
        UndoButton.IsEnabled = Editor.CanUndo;
        RedoButton.IsEnabled = Editor.CanRedo;
    }

    private void OnUndoButtonClick(object? sender, RoutedEventArgs e)
    {
        Editor.Undo();
        RefreshUndoRedoButtons();
    }

    private void OnRedoButtonClick(object? sender, RoutedEventArgs e)
    {
        Editor.Redo();
        RefreshUndoRedoButtons();
    }

    private void RebuildSteps()
    {
        Steps.Clear();
        _currentStepIndex = 0;

        if (DataContext is ProposedFileChangeViewModel proposedFileChange)
        {
            var hunks = proposedFileChange.Hunks;

            for (var i = 0; i < hunks.Count; i++)
            {
                Steps.Add(new TutorialStepViewModel(hunks[i], proposedFileChange, _promptResultStore.WorkingDirectoryPath, _promptResultStore.SessionId, _fileChangeApplyService, _llmRunner, _navigationService, stepNumber: i + 1, totalSteps: hunks.Count));
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
