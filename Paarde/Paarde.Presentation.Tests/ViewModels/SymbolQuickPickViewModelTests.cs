using Paarde.Presentation.Tests.Fakes;
using Paarde.Core.Models;
using Paarde.Presentation.ViewModels;

namespace Paarde.Presentation.Tests.ViewModels;

public class SymbolQuickPickViewModelTests
{
    private readonly FakeActiveEditorContext _activeEditorContext = new();

    private SymbolQuickPickViewModel CreateSut() => new(_activeEditorContext);

    private static LspDocumentSymbol Symbol(string name, LspPosition selectionStart, params LspDocumentSymbol[] children) =>
        new(name, LspSymbolKind.Method, new LspRange(selectionStart, selectionStart), new LspRange(selectionStart, selectionStart), children);

    private static LspDocumentSymbol MakeDocument() =>
        new("file.cs", LspSymbolKind.File, new LspRange(new LspPosition(0, 0), new LspPosition(0, 0)), new LspRange(new LspPosition(0, 0), new LspPosition(0, 0)),
        [
            Symbol("Foo", new LspPosition(0, 6), Symbol("Bar", new LspPosition(2, 8))),
            Symbol("Baz", new LspPosition(10, 6)),
        ]);

    [Fact]
    public void Open_NoActiveDocument_PopulatesEmptyStateMessage()
    {
        var sut = CreateSut();

        sut.OpenCommand.Execute(null);

        Assert.True(sut.IsOpen);
        Assert.Empty(sut.Results);
        Assert.NotNull(sut.EmptyStateMessage);
    }

    [Fact]
    public void Open_ActiveDocumentSet_PopulatesEveryFlattenedSymbol()
    {
        var sut = CreateSut();
        _activeEditorContext.SetActiveDocument("/repo/file.cs", MakeDocument());

        sut.OpenCommand.Execute(null);

        Assert.Equal(["Foo", "Bar", "Baz"], sut.Results.Select(s => s.Name));
        Assert.Null(sut.EmptyStateMessage);
    }

    [Fact]
    public void QueryChanged_FiltersFlattenedListByNameSubstring_CaseInsensitive()
    {
        var sut = CreateSut();
        _activeEditorContext.SetActiveDocument("/repo/file.cs", MakeDocument());
        sut.OpenCommand.Execute(null);

        sut.Query = "ba";

        Assert.Equal(["Bar", "Baz"], sut.Results.Select(s => s.Name));
    }

    [Fact]
    public void QueryChanged_NoMatches_PopulatesEmptyStateMessage()
    {
        var sut = CreateSut();
        _activeEditorContext.SetActiveDocument("/repo/file.cs", MakeDocument());
        sut.OpenCommand.Execute(null);

        sut.Query = "nonexistent";

        Assert.Empty(sut.Results);
        Assert.NotNull(sut.EmptyStateMessage);
    }

    [Fact]
    public void ActivateSelected_RequestsNavigationToTheSelectedSymbolsSelectionRangeStart_AndCloses()
    {
        var sut = CreateSut();
        _activeEditorContext.SetActiveDocument("/repo/file.cs", MakeDocument());
        sut.OpenCommand.Execute(null);
        sut.SelectedResult = sut.Results.Single(s => s.Name == "Bar");

        sut.ActivateSelectedCommand.Execute(null);

        var requested = Assert.Single(_activeEditorContext.RequestNavigationCalls);
        Assert.Equal(new LspPosition(2, 8), requested);
        Assert.False(sut.IsOpen);
    }

    [Fact]
    public void ActivateSelected_NothingSelected_DoesNothing()
    {
        var sut = CreateSut();
        _activeEditorContext.SetActiveDocument("/repo/file.cs", MakeDocument());
        sut.OpenCommand.Execute(null);
        sut.SelectedResult = null;

        sut.ActivateSelectedCommand.Execute(null);

        Assert.Empty(_activeEditorContext.RequestNavigationCalls);
    }

    [Fact]
    public void Close_SetsIsOpenFalse()
    {
        var sut = CreateSut();
        sut.OpenCommand.Execute(null);

        sut.CloseCommand.Execute(null);

        Assert.False(sut.IsOpen);
    }

    [Fact]
    public void ActiveDocumentChangesWhileClosed_ReflectsOnTheNextOpen()
    {
        var sut = CreateSut();
        _activeEditorContext.SetActiveDocument("/repo/file.cs", MakeDocument());

        sut.OpenCommand.Execute(null);

        Assert.Equal(["Foo", "Bar", "Baz"], sut.Results.Select(s => s.Name));
    }
}
