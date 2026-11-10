using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace Paarde.WinUI.UiTests;

internal static class PageChecks
{
    // What Narrator announces for these comes from their UIA name, so each needs one.
    private static readonly ControlType[] NamedControlTypes =
    [
        ControlType.Button,
        ControlType.SplitButton,
        ControlType.CheckBox,
        ControlType.RadioButton,
        ControlType.ComboBox,
        ControlType.Hyperlink,
        ControlType.ListItem,
        ControlType.MenuItem,
        ControlType.TabItem,
        ControlType.TreeItem,
    ];

    public static void AssertHealthy(AppSession app)
    {
        Assert.False(app.HasExited, "Paarde.WinUI exited (crashed?).");
        Assert.Empty(app.LoggedErrors());
    }

    public static void AssertControlsHaveNames(AppSession app)
    {
        var problems = new List<string>();

        foreach (var controlType in NamedControlTypes)
        {
            foreach (var element in app.Window.FindAllDescendants(app.By.ByControlType(controlType)))
            {
                var name = element.Properties.Name.ValueOrDefault ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    problems.Add($"{Describe(element)} has no accessible name");
                }
                // A bound item without AutomationProperties.Name gets its model's ToString(), e.g.
                // "Paarde.Core.Models.FileTreeEntry" - not empty, but no use to a screen reader.
                else if (name.StartsWith("Paarde.", StringComparison.Ordinal) && !name.Contains(' '))
                {
                    problems.Add($"{Describe(element)} is named after its model type (\"{name}\")");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct()));
    }

    // Horizontal only: a page that scrolls vertically (Settings) legitimately has controls below
    // the window's bottom edge, but nothing should be cut off at the sides.
    public static void AssertControlsFitTheWindowWidth(AppSession app)
    {
        var window = app.Window.BoundingRectangle;
        var problems = new List<string>();

        foreach (var controlType in new[] { ControlType.Button, ControlType.ComboBox, ControlType.SplitButton })
        {
            foreach (var element in app.Window.FindAllDescendants(app.By.ByControlType(controlType)))
            {
                var bounds = element.BoundingRectangle;
                if (bounds.IsEmpty)
                {
                    continue;
                }

                if (bounds.Left < window.Left - 1 || bounds.Right > window.Right + 1)
                {
                    problems.Add($"{Describe(element)} spans x={bounds.Left}..{bounds.Right}, outside the window's {window.Left}..{window.Right}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // The element plus its nearest ancestors, so a failure says where the control is.
    private static string Describe(AutomationElement element)
    {
        var path = new List<string>();
        for (var current = element; current != null && path.Count < 4; current = current.Parent)
        {
            var automationId = current.Properties.AutomationId.ValueOrDefault;
            var name = current.Properties.Name.ValueOrDefault;

            path.Add($"{current.Properties.ControlType.ValueOrDefault}[{current.Properties.ClassName.ValueOrDefault}"
                + (string.IsNullOrEmpty(automationId) ? string.Empty : $" #{automationId}")
                + (string.IsNullOrEmpty(name) ? string.Empty : $" \"{name}\"")
                + "]");
        }

        // Icon-only buttons have nothing else to identify them by.
        var children = string.Join(", ", element.FindAllChildren().Take(3).Select(child =>
            $"{child.Properties.ControlType.ValueOrDefault} \"{child.Properties.Name.ValueOrDefault}\""));

        return string.Join(" < ", path) + $" at {element.BoundingRectangle}" + (children.Length == 0 ? string.Empty : $" containing {children}");
    }
}
