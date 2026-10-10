using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using SkiaSharp;
using Svg.Skia;

namespace WheelWizard.UI.Test;

public class MiiEditorFloorTests
{
    [AvaloniaTheory]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(90f)]
    [InlineData(-135f)]
    public void FloorTexture_FollowsCharacterYawWithinTheGroundPlane(float degrees)
    {
        var rotation = System.Numerics.Matrix4x4.CreateRotationY(degrees * MathF.PI / 180);
        var method = typeof(WheelWizard.WiiManagement.MiiManagement.Views.Editor.MiiEditorFloor).GetMethod(
            "TextureRotation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        )!;
        var texture = (SKMatrix)method.Invoke(null, [rotation])!;
        var point = texture.MapPoint(30, 20);
        var expected = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(30, 0, 20), rotation);
        Assert.Equal(expected.X, point.X, 4);
        Assert.Equal(expected.Z, point.Y, 4);
        Assert.Equal(0, texture.TransX);
        Assert.Equal(0, texture.TransY);
        Assert.Equal(0, texture.Persp0);
        Assert.Equal(0, texture.Persp1);
    }

    [AvaloniaFact]
    public void FloorSvg_LoadsAsAnEditableVectorPattern_WithTransparentGaps()
    {
        using var stream = AssetLoader.Open(new Uri("avares://WheelWizard/Resources/Images/MiiEditorFloor.svg"));
        using var svg = new SKSvg();
        var picture = svg.Load(stream);
        Assert.NotNull(picture);
        Assert.Equal(new SKRect(0, 0, 180, 180), picture.CullRect);

        using var bitmap = new SKBitmap(180, 180);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawPicture(picture);
        }

        Assert.Equal(0, bitmap.GetPixel(90, 90).Alpha);
        Assert.Equal(255, bitmap.GetPixel(104, 104).Alpha);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }
}
