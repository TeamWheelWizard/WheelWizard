using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using SkiaSharp;
using Svg.Skia;

namespace WheelWizard.UI.Test;

public class MiiEditorFloorTests
{
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
