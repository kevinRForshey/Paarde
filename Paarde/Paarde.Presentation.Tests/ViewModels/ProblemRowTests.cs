using Paarde.Core.Models;
using Paarde.Presentation.ViewModels;

namespace Paarde.Presentation.Tests.ViewModels;

public class ProblemRowTests
{
    [Fact]
    public void Summary_ShowsFileNameOneBasedPositionSeverityAndMessage()
    {
        var row = new ProblemRow(Path.Combine("C:", "work", "project", "Broken.cs"), new LspPosition(Line: 4, Character: 26), LspDiagnosticSeverity.Error, "Cannot convert string to int.");

        Assert.Equal("Broken.cs:5:27 [Error] Cannot convert string to int.", row.Summary);
    }
}
