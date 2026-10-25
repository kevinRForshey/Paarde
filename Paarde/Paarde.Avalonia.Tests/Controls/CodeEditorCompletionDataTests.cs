using Paarde.Avalonia.Controls;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.Controls;

public class CodeEditorCompletionDataTests
{
    [Fact]
    public void InsertionText_ItemHasDistinctInsertText_ReturnsInsertText()
    {
        var data = new CodeEditorCompletionData(new LspCompletionItem("WriteLine", "void WriteLine()", "WriteLine()", Kind: 2));

        Assert.Equal("WriteLine()", data.InsertionText);
    }

    [Fact]
    public void InsertionText_ItemHasNoInsertText_FallsBackToLabel()
    {
        var data = new CodeEditorCompletionData(new LspCompletionItem("Length", "int", InsertText: null, Kind: 5));

        Assert.Equal("Length", data.InsertionText);
    }

    [Fact]
    public void Text_AlwaysReflectsLabel_RegardlessOfInsertText()
    {
        var data = new CodeEditorCompletionData(new LspCompletionItem("Length", "int", "LengthInsert", Kind: 5));

        Assert.Equal("Length", data.Text);
        Assert.Equal("Length", data.Content);
    }

    [Fact]
    public void Description_ReflectsDetail()
    {
        var withDetail = new CodeEditorCompletionData(new LspCompletionItem("Length", "int", null, Kind: 5));
        var withoutDetail = new CodeEditorCompletionData(new LspCompletionItem("Length", null, null, Kind: 5));

        Assert.Equal("int", withDetail.Description);
        Assert.Null(withoutDetail.Description);
    }
}
