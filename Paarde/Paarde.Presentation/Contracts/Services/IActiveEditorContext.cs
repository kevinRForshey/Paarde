using Paarde.Core.Models;

namespace Paarde.Presentation.Contracts.Services;

// StatusBarViewModel is a DI singleton, not a page ViewModel, so
// it has no INavigationAware.OnNavigatedTo to read a focused CodeEditor's caret position from -
// this is the one place every CodeEditor host (ExplorerView, TutorialDetailControl) reports its own
// caret position to whenever its editor has focus, without StatusBarViewModel needing to know any
// of those hosts exist. Mirrors IWorkingDirectoryContext's shape exactly.
public interface IActiveEditorContext
{
    event Action<LspPosition?>? Changed;

    void SetCaretPosition(LspPosition? position);

    // The same "whichever CodeEditor host currently has focus
    // reports here" shape as Changed/SetCaretPosition above, so SymbolQuickPickViewModel (a DI
    // singleton, like StatusBarViewModel) can read the focused tab's own file/outline and jump
    // its caret without depending on the page-scoped ExplorerViewModel.
    event Action<string?, LspDocumentSymbol?>? DocumentChanged;

    void SetActiveDocument(string? filePath, LspDocumentSymbol? documentSymbols);

    event Action<LspPosition>? NavigationRequested;

    void RequestNavigation(LspPosition position);
}
