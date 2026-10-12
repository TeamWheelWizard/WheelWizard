using Avalonia;

namespace WheelWizard.MiiImages.Views;

/// <summary>A 3D Mii; while <see cref="Interactive"/> it can be turned and zoomed with the mouse.</summary>
public sealed class Mii3DRender : MiiImageControl
{
    public static readonly StyledProperty<bool> InteractiveProperty = AvaloniaProperty.Register<Mii3DRender, bool>(
        nameof(Interactive),
        true
    );

    public bool Interactive
    {
        get => GetValue(InteractiveProperty);
        set => SetValue(InteractiveProperty, value);
    }
}
