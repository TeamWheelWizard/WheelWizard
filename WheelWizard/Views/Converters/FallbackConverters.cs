using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WheelWizard.Views.Converters;

// Note that this is static, which means you don't have to add it as a converter
public static class FallbackConverters
{
    // Todo I think this can be made more generic, just with <object, object?>
    public static readonly IMultiValueConverter Brushes = new FuncMultiValueConverter<IBrush, IBrush?>(x =>
    {
        foreach (var brush in x)
        {
            if (brush is not null)
                return brush;
        }
        return null;
    });
}
