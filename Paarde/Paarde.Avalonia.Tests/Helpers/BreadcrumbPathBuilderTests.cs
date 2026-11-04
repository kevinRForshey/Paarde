using Paarde.Avalonia.Helpers;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.Helpers;

public class BreadcrumbPathBuilderTests
{
    private static LspDocumentSymbol Symbol(string name, LspPosition start, LspPosition end, params LspDocumentSymbol[] children) =>
        new(name, LspSymbolKind.Class, new LspRange(start, end), new LspRange(start, start), children);

    [Fact]
    public void Build_RootIsNull_ReturnsEmpty()
    {
        var chain = BreadcrumbPathBuilder.Build(null, new LspPosition(0, 0));

        Assert.Empty(chain);
    }

    [Fact]
    public void Build_CaretPositionIsNull_ReturnsEmpty()
    {
        var root = Symbol("file.cs", new LspPosition(0, 0), new LspPosition(10, 0));

        var chain = BreadcrumbPathBuilder.Build(root, null);

        Assert.Empty(chain);
    }

    [Fact]
    public void Build_CaretOutsideEverySymbolsRange_ReturnsEmpty()
    {
        var method = Symbol("Bar", new LspPosition(2, 0), new LspPosition(4, 0));
        var root = Symbol("file.cs", new LspPosition(0, 0), new LspPosition(10, 0), method);

        var chain = BreadcrumbPathBuilder.Build(root, new LspPosition(20, 0));

        Assert.Empty(chain);
    }

    [Fact]
    public void Build_CaretInsideNestedSymbol_ReturnsFullEnclosingChain()
    {
        var method = Symbol("Bar", new LspPosition(2, 4), new LspPosition(4, 5));
        var type = Symbol("Foo", new LspPosition(0, 0), new LspPosition(10, 1), method);
        var root = Symbol("file.cs", new LspPosition(0, 0), new LspPosition(10, 1), type);

        var chain = BreadcrumbPathBuilder.Build(root, new LspPosition(3, 0));

        Assert.Equal(["Foo", "Bar"], chain.Select(symbol => symbol.Name));
    }

    [Fact]
    public void Build_CaretExactlyOnASymbolsOpeningLine_IsInsideThatSymbol()
    {
        var method = Symbol("Bar", new LspPosition(2, 4), new LspPosition(4, 5));
        var root = Symbol("file.cs", new LspPosition(0, 0), new LspPosition(10, 1), method);

        var chain = BreadcrumbPathBuilder.Build(root, new LspPosition(2, 4));

        Assert.Equal(["Bar"], chain.Select(symbol => symbol.Name));
    }

    [Fact]
    public void Build_CaretExactlyOnASymbolsClosingLine_IsInsideThatSymbol()
    {
        var method = Symbol("Bar", new LspPosition(2, 4), new LspPosition(4, 5));
        var root = Symbol("file.cs", new LspPosition(0, 0), new LspPosition(10, 1), method);

        var chain = BreadcrumbPathBuilder.Build(root, new LspPosition(4, 5));

        Assert.Equal(["Bar"], chain.Select(symbol => symbol.Name));
    }

    [Fact]
    public void Build_CaretJustPastASymbolsClosingPosition_IsOutsideThatSymbol()
    {
        var method = Symbol("Bar", new LspPosition(2, 4), new LspPosition(4, 5));
        var root = Symbol("file.cs", new LspPosition(0, 0), new LspPosition(10, 1), method);

        var chain = BreadcrumbPathBuilder.Build(root, new LspPosition(4, 6));

        Assert.Empty(chain);
    }

    [Fact]
    public void Build_CaretInsideOneSiblingButNotTheOther_PicksTheMatchingSibling()
    {
        var first = Symbol("First", new LspPosition(0, 0), new LspPosition(2, 0));
        var second = Symbol("Second", new LspPosition(3, 0), new LspPosition(5, 0));
        var root = Symbol("file.cs", new LspPosition(0, 0), new LspPosition(10, 1), first, second);

        var chain = BreadcrumbPathBuilder.Build(root, new LspPosition(4, 0));

        Assert.Equal(["Second"], chain.Select(symbol => symbol.Name));
    }
}
