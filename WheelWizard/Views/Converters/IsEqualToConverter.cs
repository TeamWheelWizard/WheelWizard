using Avalonia.Data.Converters;
using System.Globalization;

namespace WheelWizard.Views.Converters;

public class IsEqualToConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null && parameter == null)
        {
            return true;
        }
        if (value == null || parameter == null)
        {
            return false;
        }
        // Handle string comparison specifically
        if (value is string stringValue && parameter is string stringParameter)
        {
            return stringValue.Equals(stringParameter, StringComparison.OrdinalIgnoreCase);
        }
        // general object comparison, you might want to check types also to see if object is compareable
        return value.Equals(parameter);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException(); 
    }
}
