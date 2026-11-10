using System.Globalization;

using Avalonia.Data.Converters;

using FluentAvalonia.UI.Controls;

using Paarde.Presentation.Models;

namespace Paarde.Avalonia.Helpers;

// View models expose the UI-agnostic NotificationSeverity;
// InfoBar.Severity needs FluentAvalonia's InfoBarSeverity. Member names/order are kept identical
// (guarded by NotificationSeverityToInfoBarSeverityConverterTests), so this is a plain cast.
public class NotificationSeverityToInfoBarSeverityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is NotificationSeverity severity ? (InfoBarSeverity)(int)severity : InfoBarSeverity.Informational;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is InfoBarSeverity severity ? (NotificationSeverity)(int)severity : NotificationSeverity.Informational;
}
