using System.Globalization;

using FluentAvalonia.UI.Controls;

using Paarde.Avalonia.Helpers;
using Paarde.Presentation.Models;

namespace Paarde.Avalonia.Tests.Helpers;

public class NotificationSeverityToInfoBarSeverityConverterTests
{
    [Fact]
    public void Enums_HaveIdenticalNamesInIdenticalOrder()
    {
        // The converter is a plain int cast - this keeps a FluentAvalonia upgrade from silently
        // reordering or renaming members underneath it.
        Assert.Equal(Enum.GetNames<InfoBarSeverity>(), Enum.GetNames<NotificationSeverity>());
        Assert.Equal(
            Enum.GetValues<InfoBarSeverity>().Select(value => (int)value),
            Enum.GetValues<NotificationSeverity>().Select(value => (int)value));
    }

    [Theory]
    [InlineData(NotificationSeverity.Informational, InfoBarSeverity.Informational)]
    [InlineData(NotificationSeverity.Success, InfoBarSeverity.Success)]
    [InlineData(NotificationSeverity.Warning, InfoBarSeverity.Warning)]
    [InlineData(NotificationSeverity.Error, InfoBarSeverity.Error)]
    public void Convert_MapsEachMemberToItsInfoBarCounterpart(NotificationSeverity input, InfoBarSeverity expected)
    {
        var converter = new NotificationSeverityToInfoBarSeverityConverter();

        var result = converter.Convert(input, typeof(InfoBarSeverity), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, result);
    }
}
