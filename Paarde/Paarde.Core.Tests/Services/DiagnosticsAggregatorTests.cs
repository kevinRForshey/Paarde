using Paarde.Core.Models;
using Paarde.Core.Services;
using Paarde.Core.Tests.Services.Fakes;

namespace Paarde.Core.Tests.Services;

public class DiagnosticsAggregatorTests
{
    private static LspDiagnostic MakeDiagnostic(string message = "error") =>
        new(new LspPosition(0, 0), new LspPosition(0, 1), message, LspDiagnosticSeverity.Error);

    [Fact]
    public void OnDiagnosticsPublished_NewFileWithDiagnostics_AddsIt()
    {
        var sessionManager = new FakeLanguageServerSessionManager();
        var sut = new DiagnosticsAggregator(sessionManager);

        sessionManager.RaiseDiagnosticsPublished("/repo/a.cs", new[] { MakeDiagnostic() });

        Assert.True(sut.Diagnostics.ContainsKey("/repo/a.cs"));
        Assert.Single(sut.Diagnostics["/repo/a.cs"]);
    }

    [Fact]
    public void OnDiagnosticsPublished_EmptyListForKnownFile_RemovesIt()
    {
        var sessionManager = new FakeLanguageServerSessionManager();
        var sut = new DiagnosticsAggregator(sessionManager);

        sessionManager.RaiseDiagnosticsPublished("/repo/a.cs", new[] { MakeDiagnostic() });
        sessionManager.RaiseDiagnosticsPublished("/repo/a.cs", Array.Empty<LspDiagnostic>());

        Assert.False(sut.Diagnostics.ContainsKey("/repo/a.cs"));
    }

    [Fact]
    public void OnDiagnosticsPublished_TwoDifferentFiles_KeepsBothIndependently()
    {
        var sessionManager = new FakeLanguageServerSessionManager();
        var sut = new DiagnosticsAggregator(sessionManager);

        sessionManager.RaiseDiagnosticsPublished("/repo/a.cs", new[] { MakeDiagnostic("a-error") });
        sessionManager.RaiseDiagnosticsPublished("/repo/b.cs", new[] { MakeDiagnostic("b-error") });

        Assert.Equal("a-error", sut.Diagnostics["/repo/a.cs"].Single().Message);
        Assert.Equal("b-error", sut.Diagnostics["/repo/b.cs"].Single().Message);
    }

    [Fact]
    public void OnDiagnosticsPublished_RepublishForSameFile_ReplacesRatherThanAppends()
    {
        var sessionManager = new FakeLanguageServerSessionManager();
        var sut = new DiagnosticsAggregator(sessionManager);

        sessionManager.RaiseDiagnosticsPublished("/repo/a.cs", new[] { MakeDiagnostic("first") });
        sessionManager.RaiseDiagnosticsPublished("/repo/a.cs", new[] { MakeDiagnostic("second") });

        Assert.Equal("second", sut.Diagnostics["/repo/a.cs"].Single().Message);
    }

    [Fact]
    public void OnDiagnosticsPublished_RaisesChangedExactlyOncePerPublish()
    {
        var sessionManager = new FakeLanguageServerSessionManager();
        var sut = new DiagnosticsAggregator(sessionManager);
        var changedCount = 0;
        sut.Changed += () => changedCount++;

        sessionManager.RaiseDiagnosticsPublished("/repo/a.cs", new[] { MakeDiagnostic() });

        Assert.Equal(1, changedCount);
    }
}
