using Avalonia.Data.Converters;

namespace WheelWizard.Views.Converters;

// Note that this is static, which means you dont have to add it as a converter
public static class NumberConverters
{
    public static readonly IMultiValueConverter MultiplyDouble = new FuncMultiValueConverter<double, double>(x =>
    {
        double result = 1;
        foreach (var brush in x)
        {
            result *= brush;
        }
        return result;
    });

    public static readonly IValueConverter GreaterThan0 = new FuncValueConverter<double, bool>(x => x > 0);
}
