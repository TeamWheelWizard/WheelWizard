using WheelWizard.Views.Components;

namespace WheelWizard.Test.Views.Components;

public class ValueSliderTests
{
    [Fact]
    public void TickCountDefinesInclusiveSnapPointsAndTracksRangeChanges()
    {
        var slider = new ValueSlider
        {
            Minimum = 10,
            Maximum = 110,
            Value = 40,
        };

        slider.TickCount = 3;
        Assert.True(slider.IsSnapToTickEnabled);
        Assert.Equal(50, slider.TickFrequency);
        Assert.Equal(60, slider.Value);

        slider.Value = 91;
        Assert.Equal(110, slider.Value);

        slider.Maximum = 210;
        Assert.Equal(100, slider.TickFrequency);
        Assert.Equal(110, slider.Value);

        slider.TickCount = 1;
        Assert.False(slider.IsSnapToTickEnabled);
        slider.Value = 91;
        Assert.Equal(91, slider.Value);
        slider.TickCount = 0;
        Assert.Equal(1, slider.TickCount);

        slider.Minimum = 0;
        slider.Maximum = 90;
        slider.TickCount = 10;
        slider.Value = 44;
        Assert.Equal(10, slider.TickFrequency);
        Assert.Equal(40, slider.Value);
    }
}
