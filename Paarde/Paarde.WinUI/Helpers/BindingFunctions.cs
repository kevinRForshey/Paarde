using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Paarde.Core.Models;
using Paarde.Presentation.Models;

using Windows.UI;

namespace Paarde.WinUI.Helpers;

// x:Bind function bindings standing in for Paarde.Avalonia's IValueConverters and its built-in
// StringConverters/ObjectConverters/`!` negation - compile-checked like the rest of x:Bind, and
// no converter resources to declare.
public static class BindingFunctions
{
    // Same values as Paarde.Avalonia's DiffLineKindToBackgroundConverter.
    private static readonly SolidColorBrush AddedLineBrush = new(Color.FromArgb(40, 46, 160, 67));
    private static readonly SolidColorBrush RemovedLineBrush = new(Color.FromArgb(40, 218, 54, 51));
    private static readonly SolidColorBrush UnchangedLineBrush = new(Colors.Transparent);

    public static bool IsNotEmpty(string? value) => !string.IsNullOrEmpty(value);

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfNotNull(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfNotEmpty(string? value) => VisibleIf(IsNotEmpty(value));

    public static bool IsEnumValue(Enum value, string name) => Enum.Parse(value.GetType(), name).Equals(value);

    // WinUI's TextBlock isn't a Control, so it has no IsEnabled to grey itself out with (Avalonia's
    // command palette binds TextBlock.IsEnabled); dimming is the closest equivalent.
    public static double OpacityIfEnabled(bool enabled) => enabled ? 1.0 : 0.4;

    // Same member names and order as InfoBarSeverity (asserted for FluentAvalonia's copy in
    // Paarde.Avalonia.Tests), so a plain cast.
    public static InfoBarSeverity ToInfoBarSeverity(NotificationSeverity severity) => (InfoBarSeverity)(int)severity;

    public static Brush DiffLineBackground(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Added => AddedLineBrush,
        DiffLineKind.Removed => RemovedLineBrush,
        _ => UnchangedLineBrush,
    };

    // Side-by-side cells have a null kind for padding rows (no line on that side).
    public static Brush DiffCellBackground(DiffLineKind? kind) => kind is { } k ? DiffLineBackground(k) : UnchangedLineBrush;
}
