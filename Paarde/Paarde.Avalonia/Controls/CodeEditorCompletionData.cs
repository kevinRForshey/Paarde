using Avalonia.Media;

using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

using Paarde.Core.Models;

namespace Paarde.Avalonia.Controls;

// specs/editor-autocomplete T3: thin ICompletionData adapter over an LspCompletionItem - no icon
// (R3's non-goal), Detail shown via Description (CompletionList's own item template renders it as
// the secondary text, R3.4). Complete replaces CompletionWindow's tracked completion segment with
// InsertText, falling back to Label per R4.1 - deliberately not resolved on LspCompletionItem
// itself, which stays a faithful mirror of the wire response.
public sealed class CodeEditorCompletionData(LspCompletionItem item) : ICompletionData
{
    public IImage? Image => null;

    public string Text => item.Label;

    public object Content => item.Label;

    public object? Description => item.Detail;

    public double Priority => 0;

    // R4.1's fallback rule, pulled out of Complete so it's testable without constructing a live
    // TextArea - TextArea's constructor reaches into AvaloniaEdit's CaretNavigationCommandHandler,
    // which needs Application.Current (HotkeyConfiguration.Keymap), so it can't be instantiated in
    // a plain xUnit test; Complete itself is covered by T8's manual verification instead.
    public string InsertionText => item.InsertText ?? item.Label;

    public void Complete(TextArea textArea, ISegment completionSegment, System.EventArgs insertionRequestEventArgs) =>
        textArea.Document.Replace(completionSegment, InsertionText);
}
