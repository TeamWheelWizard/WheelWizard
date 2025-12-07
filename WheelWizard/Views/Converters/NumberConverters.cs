using Avalonia.Data.Converters;

namespace WheelWizard.Views.Converters;

// Note that this is static, which means you dont have to add it as a converter
public static class NumberConverters
{
    public static readonly IValueConverter DoubleToInt = new FuncValueConverter<double, int>(x => (int)x);
    public static readonly IValueConverter FloatToInt = new FuncValueConverter<float, int>(x => (int)x);

    public static readonly IMultiValueConverter MultiplyDouble = new FuncMultiValueConverter<double, double>(x =>
    {
        double result = 1;
        foreach (var brush in x)
        {
            result *= brush;
        }
        return result;
    });

    // TODO, find out if you can have a third parameter so that its not always 0, but instead that you can specify the value

    public static readonly IValueConverter Equals0 = new FuncValueConverter<double, bool>(x => x == 0);
    public static readonly IValueConverter GreaterThan0 = new FuncValueConverter<double, bool>(x => x > 0);
    public static readonly IValueConverter SmallerThan0 = new FuncValueConverter<double, bool>(x => x < 0);
    public static readonly IValueConverter GreaterThanOrEqual0 = new FuncValueConverter<double, bool>(x => x >= 0);
    public static readonly IValueConverter SmallerThanOrEqual0 = new FuncValueConverter<double, bool>(x => x <= 0);
}
