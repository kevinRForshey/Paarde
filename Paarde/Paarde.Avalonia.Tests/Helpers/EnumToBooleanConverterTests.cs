using System.Globalization;

using Avalonia.Data;

using Paarde.Avalonia.Helpers;
using Paarde.Presentation.Models;

namespace Paarde.Avalonia.Tests.Helpers;

public class EnumToBooleanConverterTests
{
    private readonly EnumToBooleanConverter _converter = new();

    [Fact]
    public void Convert_MatchingMember_ReturnsTrue()
    {
        Assert.Equal(true, _converter.Convert(InterfaceMode.FlowWizard, typeof(bool?), "FlowWizard", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_OtherMember_ReturnsFalse()
    {
        Assert.Equal(false, _converter.Convert(InterfaceMode.FlowWizard, typeof(bool?), "EditorFocused", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_Checked_ReturnsTheButtonsMember()
    {
        Assert.Equal(InterfaceMode.FlowWizard, _converter.ConvertBack(true, typeof(InterfaceMode), "FlowWizard", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_Unchecked_LeavesThePropertyAlone()
    {
        Assert.Same(BindingOperations.DoNothing, _converter.ConvertBack(false, typeof(InterfaceMode), "EditorFocused", CultureInfo.InvariantCulture));
    }
}
