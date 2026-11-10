using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Paarde.Core.Helpers;
using Paarde.Presentation.ViewModels;

using Windows.System;

namespace Paarde.WinUI.Views;

public sealed partial class ComposePanelControl : UserControl
{
    // DataContext is supplied externally (PromptView, later the Assistant panel), so
    // ViewModel just mirrors it for x:Bind rather than resolving from DI.
    public PromptViewModel? ViewModel { get; private set; }

    // Which `@mention` (if any) the caret is currently inside, as of the last TextChanged -
    // null means the mention popup is closed. Kept here (not on the ViewModel) since it's purely
    // about this TextBox's own Text/caret, not application state.
    private MentionTrigger? _activeMentionTrigger;

    public ComposePanelControl()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (!ReferenceEquals(ViewModel, DataContext))
            {
                ViewModel = DataContext as PromptViewModel;
                Bindings.Update();
            }
        };
    }

    // Inside the ScrollViewer the grid is measured with unlimited height, so its * row would only
    // ever get the prompt box's MinHeight; a minimum of the viewport's height (less RootGrid's
    // margin) gives that row the spare space back whenever everything fits.
    private void OnRootScrollViewerSizeChanged(object sender, SizeChangedEventArgs e) =>
        RootGrid.MinHeight = Math.Max(0, e.NewSize.Height - RootGrid.Margin.Top - RootGrid.Margin.Bottom);

    private void OnPromptTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        // Avalonia's bug fix carried over: the host can clear DataContext before the TextBox's
        // final TextChanged fires.
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        _activeMentionTrigger = MentionTriggerDetector.FindActiveTrigger(PromptTextBox.Text, PromptTextBox.SelectionStart);

        if (_activeMentionTrigger is { } trigger)
        {
            viewModel.UpdateMentionSuggestions(trigger.Query);
        }
        else
        {
            viewModel.CloseMentionPopup();
        }
    }

    // Up/Down move the highlighted suggestion, Enter/Tab accept it (and must not
    // also insert their usual newline/tab into the AcceptsReturn TextBox - e.Handled = true
    // suppresses that), Escape just closes the popup without touching the typed text.
    private void OnPromptTextBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel is not { HasMentionSuggestions: true } viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                viewModel.CloseMentionPopup();
                e.Handled = true;
                break;

            case VirtualKey.Enter:
            case VirtualKey.Tab:
                var toInsert = MentionList.SelectedItem as string ?? viewModel.MentionSuggestions.FirstOrDefault();
                if (toInsert is not null)
                {
                    InsertMention(toInsert);
                }

                e.Handled = true;
                break;

            case VirtualKey.Down:
                MoveMentionSelection(1);
                e.Handled = true;
                break;

            case VirtualKey.Up:
                MoveMentionSelection(-1);
                e.Handled = true;
                break;
        }
    }

    private void OnMentionSuggestionDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (MentionList.SelectedItem is string selected)
        {
            InsertMention(selected);
        }
    }

    // Unlike a mention pick, this doesn't touch the prompt text - it only attaches the
    // file, so the suggestion list can lose just this one entry rather than closing entirely.
    private void OnSuggestedRelevantFileDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SuggestedRelevantFilesList.SelectedItem is string selected)
        {
            ViewModel?.AttachSuggestedFile(selected);
        }
    }

    // The view model switches to the picked directory; clearing the selection is deferred until
    // after this SelectionChanged so the ComboBox actually returns to its placeholder.
    private void OnRecentWorkingDirectorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } viewModel && RecentWorkingDirectoryComboBox.SelectedItem is string selected)
        {
            viewModel.SelectedRecentWorkingDirectory = selected;
            DispatcherQueue.TryEnqueue(() => RecentWorkingDirectoryComboBox.SelectedIndex = -1);
        }
    }

    // NumberBox reports NaN when the box is cleared; keep the last valid value instead.
    private void OnTimeoutValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (ViewModel is { } viewModel && !double.IsNaN(args.NewValue))
        {
            viewModel.TimeoutSeconds = (int)args.NewValue;
        }
    }

    private void MoveMentionSelection(int delta)
    {
        var count = ViewModel?.MentionSuggestions.Count ?? 0;
        if (count == 0)
        {
            return;
        }

        var currentIndex = MentionList.SelectedIndex < 0 ? -1 : MentionList.SelectedIndex;
        MentionList.SelectedIndex = ((currentIndex + delta) % count + count) % count;
        MentionList.ScrollIntoView(MentionList.SelectedItem);
    }

    // Replaces the typed "@query" with the full "@relative/path " token and attaches the
    // file through ViewModel.AttachMentionedFile - the same AddContextFilesAsync mechanism, just
    // reached from the popup instead of the file picker.
    private void InsertMention(string relativePath)
    {
        if (_activeMentionTrigger is not { } trigger || ViewModel is not { } viewModel)
        {
            return;
        }

        var text = PromptTextBox.Text;
        var caretIndex = Math.Clamp(PromptTextBox.SelectionStart, 0, text.Length);
        var insertion = $"@{relativePath} ";

        PromptTextBox.Text = text[..trigger.TriggerStart] + insertion + text[caretIndex..];
        PromptTextBox.SelectionStart = trigger.TriggerStart + insertion.Length;

        _activeMentionTrigger = null;
        viewModel.AttachMentionedFile(relativePath);
    }
}
