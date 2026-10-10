using Avalonia;
using Avalonia.Controls.Primitives;

namespace WheelWizard.Views.Components;

public class Spinner : TemplatedControl
{
    public static readonly StyledProperty<bool> IsSpinningProperty = AvaloniaProperty.Register<Spinner, bool>(nameof(IsSpinning), true);
    public bool IsSpinning
    {
        get => GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
    }
}
