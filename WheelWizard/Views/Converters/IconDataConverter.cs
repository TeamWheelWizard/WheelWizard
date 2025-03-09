using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace WheelWizard.Views.Converters;

public class IconDataConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IImage imageData) return null;
        return imageData;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
