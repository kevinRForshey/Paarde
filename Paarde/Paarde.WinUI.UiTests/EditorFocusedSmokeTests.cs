using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Paarde.WinUI.UiTests;

public class EditorFocusedSmokeTests(EditorFocusedApp app) : IClassFixture<EditorFocusedApp>
{
    [Fact]
    public void MainWindow_IsTitledPaarde()
    {
        Assert.Equal("Paarde", app.Window.Title);
        PageChecks.AssertHealthy(app);
    }

    [Theory]
    [InlineData("Explorer")]
    [InlineData("Terminal")]
    [InlineData("History")]
    [InlineData("Settings")]
    public void Page_Opens_WithNamedControlsThatFitTheWindow(string pageName)
    {
        app.NavigateTo(pageName);

        PageChecks.AssertControlsHaveNames(app);
        PageChecks.AssertControlsFitTheWindowWidth(app);
        PageChecks.AssertHealthy(app);
    }

    [Fact]
    public void Explorer_ListsTheWorkingDirectory_AndOpensAFileInATab()
    {
        app.NavigateTo("Explorer");

        var file = AppSession.WaitFor(
            () => app.Window.FindFirstDescendant(app.By.ByControlType(ControlType.TreeItem).And(app.By.ByName("Sample.cs"))),
            "Sample.cs in the file tree");
        Assert.NotNull(app.Window.FindFirstDescendant(app.By.ByControlType(ControlType.TreeItem).And(app.By.ByName("notes.md"))));

        app.BringToFront();
        file.Click();

        AppSession.WaitFor(
            () => app.Window.FindAllDescendants(app.By.ByControlType(ControlType.TabItem))
                .FirstOrDefault(tab => tab.Properties.Name.ValueOrDefault?.Contains("Sample.cs") == true),
            () => "a Sample.cs editor tab; tabs: "
                + string.Join(", ", app.Window.FindAllDescendants(app.By.ByControlType(ControlType.TabItem)).Select(tab => $"\"{tab.Properties.Name.ValueOrDefault}\"")));
        PageChecks.AssertControlsHaveNames(app);
        PageChecks.AssertHealthy(app);
    }

    [Fact]
    public void CtrlP_OpensQuickOpen_AndEscapeClosesIt()
    {
        app.NavigateTo("History");
        app.BringToFront();

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_P);
        AppSession.WaitFor(() => app.Window.FindFirstDescendant(app.By.ByAutomationId("QuickOpenQueryBox")), "the Quick Open box");

        app.BringToFront();
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        AppSession.WaitFor(
            () => app.Window.FindFirstDescendant(app.By.ByAutomationId("QuickOpenQueryBox")) == null ? app.Window : null,
            "the Quick Open box to close");

        PageChecks.AssertHealthy(app);
    }
}
