using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WheelWizard.Views.Converters;

// Note that this is static, which means you dont have to add it as a converter
public static class BrushColorConverters
{
    public static readonly IValueConverter TransparentColor = new FuncValueConverter<object, Color>(x =>
    {
        if (x is ISolidColorBrush brush)
            return new Color(0, brush.Color.R, brush.Color.G, brush.Color.B);
        if (x is Color c)
            return new Color(0, c.R, c.G, c.B);

        return (Colors.Transparent);
    });

    public static readonly IValueConverter BrushToColor = new FuncValueConverter<IBrush, Color>(x =>
    {
        if (x is ISolidColorBrush brush)
            return brush.Color;
        if (x is IGradientBrush gradientBrush)
            return gradientBrush.GradientStops[0].Color;

        return (Colors.Transparent);
    });
}
