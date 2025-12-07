using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WheelWizard.Views.Converters;

public class DoubleToThicknessConverters
{
    public static readonly IValueConverter DoubleToTop = new FuncValueConverter<double, Thickness?>(x => new Thickness(0, x, 0, 0));
    public static readonly IValueConverter DoubleToBottom = new FuncValueConverter<double, Thickness?>(x => new Thickness(0, 0, 0, x));
    public static readonly IValueConverter DoubleToLeft = new FuncValueConverter<double, Thickness?>(x => new Thickness(x, 0, 0, 0));
    public static readonly IValueConverter DoubleToRight = new FuncValueConverter<double, Thickness?>(x => new Thickness(0, 0, x, 0));
}
