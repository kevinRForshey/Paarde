using FlaUI.Core.Definitions;

namespace Paarde.WinUI.UiTests;

// FlowWizard mode adds its own pages ahead of the shared ones.
public class FlowWizardSmokeTests(FlowWizardApp app) : IClassFixture<FlowWizardApp>
{
    [Theory]
    [InlineData("Home")]
    [InlineData("Compose")]
    [InlineData("Review")]
    [InlineData("Explorer")]
    public void Page_Opens_WithNamedControlsThatFitTheWindow(string pageName)
    {
        app.NavigateTo(pageName);

        PageChecks.AssertControlsHaveNames(app);
        PageChecks.AssertControlsFitTheWindowWidth(app);
        PageChecks.AssertHealthy(app);
    }

    // The panel starts shut in this mode; it once "reopened" at its 0 px starting width.
    [Fact]
    public void Explorer_AssistantPanel_StartsShut_AndTheAssistantButtonOpensIt()
    {
        app.NavigateTo("Explorer");
        var findPresetBox = () => app.Window.FindFirstDescendant(app.By.ByControlType(ControlType.ComboBox).And(app.By.ByName("Preset")));
        var assistantButton = app.Window.FindFirstDescendant(app.By.ByControlType(ControlType.Button).And(app.By.ByName("Assistant")));
        Assert.Null(findPresetBox());

        assistantButton.Patterns.Invoke.Pattern.Invoke();

        var presetBox = AppSession.WaitFor(findPresetBox, "the Assistant panel's Preset box");
        Assert.True(presetBox.BoundingRectangle.Width > 0);
        PageChecks.AssertControlsFitTheWindowWidth(app);

        assistantButton.Patterns.Invoke.Pattern.Invoke();
        AppSession.WaitFor(() => findPresetBox() == null ? app.Window : null, "the Assistant panel to close");
        PageChecks.AssertHealthy(app);
    }
}
