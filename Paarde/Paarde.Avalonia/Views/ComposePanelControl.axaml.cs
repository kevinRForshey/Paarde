using Avalonia.Controls;
using Avalonia.Input;

using Paarde.Core.Helpers;
using Paarde.Presentation.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class ComposePanelControl : UserControl
{
    // Epic 25, T9: unlike PromptView (a navigated page that resolved its own PromptViewModel from
    // DI), this control's DataContext is supplied externally - AssistantPanelControl binds it to
    // AssistantPanel.Compose - so ViewModel reads DataContext rather than being assigned once in
    // the constructor.
    private PromptViewModel ViewModel => (PromptViewModel)DataContext!;

    // T14.1.1: which `@mention` (if any) the caret is currently inside, as of the last TextChanged -
    // null means the mention popup is closed. Kept here (not on the ViewModel) since it's purely
    // about this TextBox's own Text/CaretIndex, not application state.
    private MentionTrigger? _activeMentionTrigger;

    public ComposePanelControl()
    {
        InitializeComponent();
    }

    private void OnPromptTextBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Bug fix: switching away from the Compose tab tears down this control (TabControl
        // recreates its content per selection), which clears DataContext before the TextBox's own
        // final TextChanged fires - without this guard, ViewModel below is null and throws.
        if (DataContext is not PromptViewModel viewModel)
        {
            return;
        }

        _activeMentionTrigger = MentionTriggerDetector.FindActiveTrigger(PromptTextBox.Text ?? string.Empty, PromptTextBox.CaretIndex);

        if (_activeMentionTrigger is { } trigger)
        {
            viewModel.UpdateMentionSuggestions(trigger.Query);
        }
        else
        {
            viewModel.CloseMentionPopup();
        }
    }

    // T14.1.1/T14.1.2: Up/Down move the highlighted suggestion, Enter/Tab accept it (and must not
    // also insert their usual newline/tab into the AcceptsReturn TextBox - e.Handled = true
    // suppresses that), Escape just closes the popup without touching the typed text.
    private void OnPromptTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (!ViewModel.HasMentionSuggestions)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                ViewModel.CloseMentionPopup();
                e.Handled = true;
                break;

            case Key.Enter:
            case Key.Tab:
                var toInsert = MentionListBox.SelectedItem as string ?? ViewModel.MentionSuggestions.FirstOrDefault();
                if (toInsert is not null)
                {
                    InsertMention(toInsert);
                }

                e.Handled = true;
                break;

            case Key.Down:
                MoveMentionSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveMentionSelection(-1);
                e.Handled = true;
                break;
        }
    }

    private void OnMentionSuggestionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (MentionListBox.SelectedItem is string selected)
        {
            InsertMention(selected);
        }
    }

    // T14.2.3: unlike a mention pick, this doesn't touch the prompt text - it only attaches the
    // file, so the suggestion list can lose just this one entry rather than closing entirely.
    private void OnSuggestedRelevantFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SuggestedRelevantFilesListBox.SelectedItem is string selected)
        {
            ViewModel.AttachSuggestedFile(selected);
        }
    }

    private void MoveMentionSelection(int delta)
    {
        var count = ViewModel.MentionSuggestions.Count;
        if (count == 0)
        {
            return;
        }

        var currentIndex = MentionListBox.SelectedIndex < 0 ? -1 : MentionListBox.SelectedIndex;
        MentionListBox.SelectedIndex = ((currentIndex + delta) % count + count) % count;
    }

    // T14.1.2: replaces the typed "@query" with the full "@relative/path " token and attaches the
    // file through ViewModel.AttachMentionedFile - the same AddContextFilesAsync mechanism, just
    // reached from the popup instead of the file picker.
    private void InsertMention(string relativePath)
    {
        if (_activeMentionTrigger is not { } trigger)
        {
            return;
        }

        var text = PromptTextBox.Text ?? string.Empty;
        var caretIndex = Math.Clamp(PromptTextBox.CaretIndex, 0, text.Length);
        var insertion = $"@{relativePath} ";

        PromptTextBox.Text = text[..trigger.TriggerStart] + insertion + text[caretIndex..];
        PromptTextBox.CaretIndex = trigger.TriggerStart + insertion.Length;

        _activeMentionTrigger = null;
        ViewModel.AttachMentionedFile(relativePath);
    }
}
