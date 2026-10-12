using WheelWizard.Views.Components;
using WheelWizard.Views.Shell;

namespace WheelWizard.Settings.Views;

// This setting previews the animation mode being enabled, rather than the previous mode.
public class AnimationSettingToggle : ToggleCheckBox
{
    protected override Type StyleKeyOverride => typeof(ToggleCheckBox);

    protected override void Toggle()
    {
        Resources[WindowAppearance.ControlAnimationDurationResourceKey] =
            IsChecked == true ? TimeSpan.Zero : TimeSpan.FromMilliseconds(120);
        base.Toggle();
    }
}
