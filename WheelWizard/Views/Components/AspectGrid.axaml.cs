using Avalonia;
using Avalonia.Controls;

namespace WheelWizard.Views.Components;

public class AspectGrid : Grid
{
    public static readonly StyledProperty<double> AspectRatioProperty = AvaloniaProperty.Register<AspectGrid, double>(
        nameof(AspectRatio),
        1,
        validate: value => double.IsFinite(value) && value > 0
    );

    static AspectGrid() => AffectsMeasure<AspectGrid>(AspectRatioProperty);

    public double AspectRatio
    {
        get => GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var fitted = Fit(availableSize);
        var desired = base.MeasureOverride(fitted);
        if (double.IsFinite(fitted.Width) && double.IsFinite(fitted.Height))
            return fitted;
        var height = Math.Max(desired.Width / AspectRatio, desired.Height);
        return new Size(height * AspectRatio, height);
    }

    protected override Size ArrangeOverride(Size finalSize) => base.ArrangeOverride(Fit(finalSize));

    private Size Fit(Size size)
    {
        var height = Math.Min(size.Width / AspectRatio, size.Height);
        return new Size(height * AspectRatio, height);
    }
}
