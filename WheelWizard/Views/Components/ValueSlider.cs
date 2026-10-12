using Avalonia;
using Avalonia.Controls;

namespace WheelWizard.Views.Components;

public class ValueSlider : Slider
{
    public static readonly StyledProperty<int> TickCountProperty = AvaloniaProperty.Register<ValueSlider, int>(
        nameof(TickCount),
        1,
        coerce: (_, count) => Math.Max(1, count)
    );

    public static readonly StyledProperty<bool> FillTrackProperty = AvaloniaProperty.Register<ValueSlider, bool>(nameof(FillTrack), true);

    public int TickCount
    {
        get => GetValue(TickCountProperty);
        set => SetValue(TickCountProperty, value);
    }

    public bool FillTrack
    {
        get => GetValue(FillTrackProperty);
        set => SetValue(FillTrackProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TickCountProperty || change.Property == MinimumProperty || change.Property == MaximumProperty)
        {
            var range = Maximum - Minimum;
            SetCurrentValue(TickFrequencyProperty, TickCount > 1 && range > 0 ? range / (TickCount - 1) : 1);
            SetCurrentValue(IsSnapToTickEnabledProperty, TickCount > 1);
        }

        if (
            TickCount > 1
            && (
                change.Property == ValueProperty
                || change.Property == TickCountProperty
                || change.Property == MinimumProperty
                || change.Property == MaximumProperty
            )
        )
        {
            var step = TickFrequency;
            if (step > 0 && double.IsFinite(step))
            {
                var snapped = Math.Clamp(
                    Minimum + Math.Round((Value - Minimum) / step, MidpointRounding.AwayFromZero) * step,
                    Minimum,
                    Maximum
                );
                if (Math.Abs(Value - snapped) > 0.000001)
                    SetCurrentValue(ValueProperty, snapped);
            }
        }
    }
}
