using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WheelWizard.WheelWizardData.Views;

/// <summary>
/// Fills the area under a line graph with a color that fades out the further it gets from the line.
/// The points are stretched over the control the same way a Path with Stretch="Fill" would, so it lines up with the line.
/// </summary>
public class VrGraphAreaFill : Control
{
    public static readonly StyledProperty<IReadOnlyList<Point>?> PointsProperty = AvaloniaProperty.Register<
        VrGraphAreaFill,
        IReadOnlyList<Point>?
    >(nameof(Points));

    public static readonly StyledProperty<Color> ColorProperty = AvaloniaProperty.Register<VrGraphAreaFill, Color>(
        nameof(Color),
        Colors.White
    );

    public static readonly StyledProperty<double> MaxOpacityProperty = AvaloniaProperty.Register<VrGraphAreaFill, double>(
        nameof(MaxOpacity),
        0.35
    );

    /// <summary>How far (as a fraction of the width) the line gets averaged out for the fade.</summary>
    private const double SmoothingRadius = 0.04;

    private WriteableBitmap? _bitmap;
    private bool _isDirty = true;

    static VrGraphAreaFill()
    {
        AffectsRender<VrGraphAreaFill>(PointsProperty, ColorProperty, MaxOpacityProperty);
    }

    public IReadOnlyList<Point>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Opacity of the fill right under the line, it fades to 0 at the bottom of the control.</summary>
    public double MaxOpacity
    {
        get => GetValue(MaxOpacityProperty);
        set => SetValue(MaxOpacityProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PointsProperty || change.Property == ColorProperty || change.Property == MaxOpacityProperty)
            _isDirty = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
        _isDirty = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var width = (int)Math.Ceiling(Bounds.Width * scaling);
        var height = (int)Math.Ceiling(Bounds.Height * scaling);
        if (width <= 0 || height <= 0 || Points is not { Count: > 0 } points)
            return;

        if (_isDirty || _bitmap is not { } existing || existing.PixelSize.Width != width || existing.PixelSize.Height != height)
            RebuildBitmap(points, width, height);

        if (_bitmap != null)
            context.DrawImage(_bitmap, new Rect(Bounds.Size));
    }

    private void RebuildBitmap(IReadOnlyList<Point> points, int width, int height)
    {
        _isDirty = false;
        if (_bitmap == null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(width, height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        }

        var lineTops = GetLineTopPerColumn(points, width, height);
        // The fade follows a smoothed version of the line, otherwise every zigzag in the line shows up as a stripe.
        // The fill itself still stops at the real line.
        var fadeTops = Smooth(lineTops, Math.Max(1, (int)(width * SmoothingRadius)));
        var color = Color;
        var rgb = (color.R << 16) | (color.G << 8) | color.B;
        var maxAlpha = Math.Clamp(MaxOpacity, 0, 1) * (color.A / 255.0);
        var pixels = new int[width * height];

        for (var column = 0; column < width; column++)
        {
            var lineTop = lineTops[column];
            if (double.IsPositiveInfinity(lineTop))
                continue;

            for (var row = Math.Max(0, (int)Math.Floor(lineTop)); row < height; row++)
            {
                // The top pixel is only partly under the line, this keeps the edge anti-aliased.
                var coverage = Math.Clamp(row + 1 - lineTop, 0, 1);
                var distance = Math.Max(0, row + 0.5 - fadeTops[column]) / height;
                var fade = 1 - Math.Min(1, distance);
                var alpha = (int)Math.Round(maxAlpha * fade * fade * coverage * 255);
                if (alpha > 0)
                    pixels[row * width + column] = (alpha << 24) | rgb;
            }
        }

        using var locked = _bitmap.Lock();
        for (var row = 0; row < height; row++)
            Marshal.Copy(pixels, row * width, IntPtr.Add(locked.Address, row * locked.RowBytes), width);
    }

    /// <summary>
    /// Moving average over the columns, done three times so it ends up close to a gaussian blur.
    /// Columns without a value are skipped in the average.
    /// </summary>
    private static double[] Smooth(double[] values, int radius)
    {
        var result = values;
        for (var pass = 0; pass < 3; pass++)
        {
            var sums = new double[result.Length + 1];
            var counts = new int[result.Length + 1];
            for (var i = 0; i < result.Length; i++)
            {
                var hasValue = !double.IsPositiveInfinity(result[i]);
                sums[i + 1] = sums[i] + (hasValue ? result[i] : 0);
                counts[i + 1] = counts[i] + (hasValue ? 1 : 0);
            }

            var next = new double[result.Length];
            for (var i = 0; i < result.Length; i++)
            {
                var from = Math.Max(0, i - radius);
                var to = Math.Min(result.Length, i + radius + 1);
                var count = counts[to] - counts[from];
                next[i] = count > 0 ? (sums[to] - sums[from]) / count : double.PositiveInfinity;
            }

            result = next;
        }

        return result;
    }

    /// <summary>For every pixel column, the highest point (in device pixels) the line reaches in that column.</summary>
    private static double[] GetLineTopPerColumn(IReadOnlyList<Point> points, int width, int height)
    {
        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);

        // Same as Stretch="Fill": the bounds of the points cover the whole control.
        // A flat dimension doesn't get stretched, it just ends up at the start.
        var scaleX = maxX > minX ? width / (maxX - minX) : 0;
        var scaleY = maxY > minY ? height / (maxY - minY) : 0;
        var mapped = points.Select(p => new Point((p.X - minX) * scaleX, (p.Y - minY) * scaleY)).ToList();

        var tops = new double[width];
        Array.Fill(tops, double.PositiveInfinity);

        if (mapped.Count == 1)
        {
            tops[Math.Clamp((int)mapped[0].X, 0, width - 1)] = mapped[0].Y;
            return tops;
        }

        for (var i = 1; i < mapped.Count; i++)
        {
            var a = mapped[i - 1];
            var b = mapped[i];
            if (a.X > b.X)
                (a, b) = (b, a);

            var firstColumn = Math.Clamp((int)Math.Floor(a.X), 0, width - 1);
            var lastColumn = Math.Clamp((int)Math.Floor(b.X), 0, width - 1);
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                double top;
                if (b.X - a.X < 1e-9)
                    top = Math.Min(a.Y, b.Y);
                else
                {
                    var left = Math.Max(a.X, column);
                    var right = Math.Min(b.X, column + 1);
                    var yLeft = a.Y + (left - a.X) / (b.X - a.X) * (b.Y - a.Y);
                    var yRight = a.Y + (right - a.X) / (b.X - a.X) * (b.Y - a.Y);
                    top = Math.Min(yLeft, yRight);
                }

                tops[column] = Math.Min(tops[column], top);
            }
        }

        return tops;
    }
}
