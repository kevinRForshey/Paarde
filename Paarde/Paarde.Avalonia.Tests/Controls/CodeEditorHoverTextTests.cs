using Paarde.Avalonia.Controls;

namespace Paarde.Avalonia.Tests.Controls;

public class CodeEditorHoverTextTests
{
    [Fact]
    public void ToPlainHoverText_FencedSignature_DropsTheFenceLines()
    {
        var text = CodeEditor.ToPlainHoverText("```csharp\nclass Demo.Circle\n```");

        Assert.Equal("class Demo.Circle", text);
    }

    [Fact]
    public void ToPlainHoverText_SignatureThenDocumentation_KeepsBothWithCrLfNormalized()
    {
        var text = CodeEditor.ToPlainHoverText("```csharp\r\ndouble Circle.Area()\r\n```\r\n\r\nReturns the area.");

        Assert.Equal("double Circle.Area()\n\nReturns the area.", text);
    }

    [Fact]
    public void ToPlainHoverText_NoFences_ReturnsTheTextUnchanged()
    {
        Assert.Equal("int x", CodeEditor.ToPlainHoverText("int x"));
    }
}
