using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace WheelWizard.Views.Components;

public class MultiColoredIcon : TemplatedControl
{
    const int ColorCount = 12;

    #region Colors

    public static readonly StyledProperty<IBrush?> Color1Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color1));

    public IBrush? Color1
    {
        get => GetValue(Color1Property);
        set => SetValue(Color1Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color2Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color2));

    public IBrush? Color2
    {
        get => GetValue(Color2Property);
        set => SetValue(Color2Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color3Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color3));

    public IBrush? Color3
    {
        get => GetValue(Color3Property);
        set => SetValue(Color3Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color4Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color4));

    public IBrush? Color4
    {
        get => GetValue(Color4Property);
        set => SetValue(Color4Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color5Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color5));

    public IBrush? Color5
    {
        get => GetValue(Color5Property);
        set => SetValue(Color5Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color6Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color6));

    public IBrush? Color6
    {
        get => GetValue(Color6Property);
        set => SetValue(Color6Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color7Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color7));

    public IBrush? Color7
    {
        get => GetValue(Color7Property);
        set => SetValue(Color7Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color8Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color8));

    public IBrush? Color8
    {
        get => GetValue(Color8Property);
        set => SetValue(Color8Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color9Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color9));

    public IBrush? Color9
    {
        get => GetValue(Color9Property);
        set => SetValue(Color9Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color10Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color10));

    public IBrush? Color10
    {
        get => GetValue(Color10Property);
        set => SetValue(Color10Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color11Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color11));

    public IBrush? Color11
    {
        get => GetValue(Color11Property);
        set => SetValue(Color11Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color12Property = AvaloniaProperty.Register<MultiColoredIcon, IBrush?>(nameof(Color12));

    public IBrush? Color12
    {
        get => GetValue(Color12Property);
        set => SetValue(Color12Property, value);
    }

    #endregion

    public static readonly StyledProperty<DrawingImage> IconDataProperty = AvaloniaProperty.Register<MultiColoredIcon, DrawingImage>(
        nameof(IconData)
    );

    public DrawingImage IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public static readonly StyledProperty<DrawingImage> ProcessedIconDataProperty = AvaloniaProperty.Register<
        MultiColoredIcon,
        DrawingImage
    >(nameof(ProcessedIconData));

    public DrawingImage ProcessedIconData
    {
        get => GetValue(ProcessedIconDataProperty);
        private set => SetValue(ProcessedIconDataProperty, value);
    }

    public static readonly StyledProperty<bool> UndefinedColorsTransparentProperty = AvaloniaProperty.Register<MultiColoredIcon, bool>(
        nameof(UndefinedColorsTransparent)
    );

    public bool UndefinedColorsTransparent
    {
        get => GetValue(UndefinedColorsTransparentProperty);
        private set => SetValue(UndefinedColorsTransparentProperty, value);
    }

    private void UpdateDrawingColors()
    {
        var originalDrawing = IconData;
        if (originalDrawing?.Drawing is not DrawingGroup originalGroup)
        {
            ProcessedIconData = originalDrawing;
            return;
        }

        var processedGroup = new DrawingGroup { Children = { Capacity = originalGroup.Children.Count } };

        foreach (var drawing in originalGroup.Children)
        {
            if (drawing is not GeometryDrawing originalGeometryDrawing)
            {
                processedGroup.Children.Add(drawing);
                continue;
            }

            IPen? finalPen =
                originalGeometryDrawing.Pen == null
                    ? null
                    : new Pen
                    {
                        Thickness = originalGeometryDrawing.Pen.Thickness,
                        DashStyle = originalGeometryDrawing.Pen.DashStyle,
                        Brush = originalGeometryDrawing.Pen.Brush == null ? null : GetFinalBrush(originalGeometryDrawing.Pen.Brush),
                        LineCap = originalGeometryDrawing.Pen.LineCap,
                        LineJoin = originalGeometryDrawing.Pen.LineJoin,
                        MiterLimit = originalGeometryDrawing.Pen.MiterLimit,
                    };

            var processedGeometryDrawing = new GeometryDrawing
            {
                Geometry = originalGeometryDrawing.Geometry,
                Brush = originalGeometryDrawing.Brush == null ? null : GetFinalBrush(originalGeometryDrawing.Brush),
                Pen = finalPen,
            };
            processedGroup.Children.Add(processedGeometryDrawing);
        }

        ProcessedIconData = new() { Drawing = processedGroup };
    }

    private IBrush GetFinalBrush(IBrush originalBrush)
    {
        if (originalBrush is not ImmutableSolidColorBrush originalSolidBrush)
            return originalBrush;

        var originalColor = originalSolidBrush.Color;

        for (var i = 1; i <= ColorCount; i++)
        {
            var templateColor = (Color)Application.Current!.FindResource($"TemplateColor{i}")!;
            if (!originalColor.Equals(templateColor))
                continue;

            // From here we know that the color is indeed a template color
            var newColorProperty = GetType().GetProperty($"Color{i}");
            var propertyValue = newColorProperty?.GetValue(this);
            if (propertyValue == null)
                return UndefinedColorsTransparent ? Brushes.Transparent : originalBrush;

            return (IBrush)propertyValue;
        }
        return originalBrush;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != ProcessedIconDataProperty)
            UpdateDrawingColors();
    }
}
