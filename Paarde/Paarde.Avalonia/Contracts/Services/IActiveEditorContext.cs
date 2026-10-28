using Paarde.Core.Models;

namespace Paarde.Avalonia.Contracts.Services;

// specs/ide-workbench-shell, R3.5: StatusBarViewModel is a DI singleton, not a page ViewModel, so
// it has no INavigationAware.OnNavigatedTo to read a focused CodeEditor's caret position from -
// this is the one place every CodeEditor host (ExplorerView, TutorialDetailControl) reports its own
// caret position to whenever its editor has focus, without StatusBarViewModel needing to know any
// of those hosts exist. Mirrors IWorkingDirectoryContext's shape exactly.
public interface IActiveEditorContext
{
    event Action<LspPosition?>? Changed;

    void SetCaretPosition(LspPosition? position);
}
