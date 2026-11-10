using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using WheelWizard.Views.Components;

namespace WheelWizard.UI.Test;

public class ValueSliderTests
{
    [AvaloniaFact]
    public void BothOrientationsKeepAWorkingTrack()
    {
        var slider = new ValueSlider
        {
            Width = 240,
            Minimum = 0,
            Maximum = 127,
            Value = 64,
        };
        using var window = new UiTestWindow
        {
            Width = 300,
            Height = 300,
            Content = slider,
        };
        window.Show();
        window.UpdateLayout();
        var track = Assert.Single(slider.GetVisualDescendants().OfType<Track>());
        Assert.Equal(Orientation.Horizontal, track.Orientation);
        track.Value = 80;
        Assert.Equal(80, slider.Value);

        slider.Orientation = Orientation.Vertical;
        slider.Height = 220;
        window.UpdateLayout();
        track = Assert.Single(slider.GetVisualDescendants().OfType<Track>());
        Assert.Equal(Orientation.Vertical, track.Orientation);
        track.Value = 100;
        Assert.Equal(100, slider.Value);
    }
}
