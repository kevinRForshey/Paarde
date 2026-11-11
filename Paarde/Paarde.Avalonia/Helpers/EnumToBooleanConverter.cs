using System.Globalization;

using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Paarde.Avalonia.Helpers;

// Binds a RadioButton group to an enum property: ConverterParameter is the member name each
// button represents. Originally special-cased Avalonia's ThemeVariant (a class, not a CLR enum);
// specs/presentation-extraction replaced that with the AppTheme enum, so every use now goes
// through the generic Enum.Parse path.
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string name)
        {
            throw new ArgumentException("Converter parameter must be a member name.");
        }

        return value is Enum enumValue && Enum.Parse(enumValue.GetType(), name).Equals(enumValue);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string name)
        {
            throw new ArgumentException("Converter parameter must be a member name.");
        }

        // Only the button being checked writes back. The group unchecking the others must not:
        // that used to push the unchecked button's value into the property, so opening Settings with
        // Flow/Wizard (or the Anthropic API runner) saved reset it to the first option.
        return value is true ? Enum.Parse(targetType, name) : BindingOperations.DoNothing;
    }
}
