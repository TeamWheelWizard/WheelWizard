using Avalonia;
using Avalonia.Controls;

namespace WheelWizard.Views.Shell.Couch;

/// <summary>Inherited presentation state; feature behavior and data remain shared between views.</summary>
public sealed class CouchMode : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsEnabledProperty = AvaloniaProperty.RegisterAttached<CouchMode, Control, bool>(
        "IsEnabled",
        false,
        inherits: true
    );

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);
}
