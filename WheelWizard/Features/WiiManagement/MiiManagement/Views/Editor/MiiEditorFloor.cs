using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using SkiaSharp;
using Svg.Skia;
using WheelWizard.MiiRendering.Services;

namespace WheelWizard.WiiManagement.MiiManagement.Views.Editor;

/// <summary>A tiled ground patch projected through the Mii's camera, behind the character.</summary>
public sealed class MiiEditorFloor : Control
{
    private const float Radius = 90;
    private const float PatternZoom = 1.25f;
    private static readonly SKSvg Pattern = LoadPattern();
    private static readonly IBrush Fade = new RadialGradientBrush
    {
        GradientStops = [new GradientStop(Colors.White, 0), new GradientStop(Colors.White, 0.45), new GradientStop(Colors.Transparent, 1)],
    };
    private readonly Func<MiiRealtimeFrameSetup?> _frame;

    public MiiEditorFloor(Func<MiiRealtimeFrameSetup?> frame)
    {
        _frame = frame;
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_frame() is not { } frame || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        if (!this.TryFindResource("Neutral900", out var resource) || resource is not Color tileColor)
            return;
        if (Pattern.Picture is not { CullRect: { Width: > 0, Height: > 0 } } picture)
            return;
        // Tilt around the ground origin so the pattern is readable while the feet stay planted.
        var matrix = Matrix4x4.CreateRotationX(20 * MathF.PI / 180) * frame.View * frame.Projection;
        var center = Project(Vector3.Zero, matrix);
        var left = Project(new Vector3(-Radius, 0, 0), matrix);
        var right = Project(new Vector3(Radius, 0, 0), matrix);
        var back = Project(new Vector3(0, 0, -Radius), matrix);
        var front = Project(new Vector3(0, 0, Radius), matrix);
        if (center is null || left is null || right is null || back is null || front is null)
            return;

        var width = Math.Abs(right.Value.X - left.Value.X);
        var height = Math.Abs(front.Value.Y - back.Value.Y);
        if (width < 1 || height < 1)
            return;
        var patch = new Rect(center.Value.X - width / 2, center.Value.Y - height / 2, width, height);
        if (!patch.Intersects(new Rect(Bounds.Size)))
            return;
        using (context.PushOpacityMask(Fade, patch))
        {
            // The SVG's 180 x 180 canvas represents the ground plane from -90 to +90.
            var projection = new SKMatrix
            {
                ScaleX = (float)(Bounds.Width / 2) * (matrix.M11 + matrix.M14),
                SkewX = (float)(Bounds.Width / 2) * (matrix.M31 + matrix.M34),
                TransX = (float)(Bounds.Width / 2) * (matrix.M41 + matrix.M44),
                SkewY = (float)(Bounds.Height / 2) * (matrix.M14 - matrix.M12),
                ScaleY = (float)(Bounds.Height / 2) * (matrix.M34 - matrix.M32),
                TransY = (float)(Bounds.Height / 2) * (matrix.M44 - matrix.M42),
                Persp0 = matrix.M14,
                Persp1 = matrix.M34,
                Persp2 = matrix.M44,
            };
            var source = picture.CullRect;
            var ground = SKMatrix.Concat(
                SKMatrix.CreateTranslation(-Radius, -Radius),
                SKMatrix.Concat(
                    SKMatrix.CreateScale(2 * Radius / source.Width, 2 * Radius / source.Height),
                    SKMatrix.CreateTranslation(-source.Left, -source.Top)
                )
            );
            ground = SKMatrix.Concat(SKMatrix.CreateScale(PatternZoom, PatternZoom), ground);
            context.Custom(new FloorDrawing(picture, SKMatrix.Concat(projection, ground), new Rect(Bounds.Size), tileColor));
        }
    }

    private Point? Project(Vector3 point, Matrix4x4 matrix)
    {
        var clip = Vector4.Transform(new Vector4(point, 1), matrix);
        if (clip.W <= 0.001f)
            return null;
        return new Point((clip.X / clip.W + 1) * Bounds.Width / 2, (1 - clip.Y / clip.W) * Bounds.Height / 2);
    }

    private static SKSvg LoadPattern()
    {
        using var stream = AssetLoader.Open(new Uri("avares://WheelWizard/Resources/Images/MiiEditorFloor.svg"));
        var svg = new SKSvg();
        svg.Load(stream);
        return svg;
    }

    private sealed class FloorDrawing(SKPicture picture, SKMatrix projection, Rect bounds, Color color) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            using var tint = SKColorFilter.CreateBlendMode(
                new SKColor(color.R, color.G, color.B, (byte)(color.A * 0.5)),
                SKBlendMode.SrcIn
            );
            using var paint = new SKPaint { ColorFilter = tint, IsAntialias = true };
            canvas.Save();
            canvas.Concat(projection);
            canvas.DrawPicture(picture, paint);
            canvas.Restore();
        }
    }
}
