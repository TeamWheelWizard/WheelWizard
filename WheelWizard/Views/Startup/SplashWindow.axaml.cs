using System.Numerics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace WheelWizard.Views.Startup;

public partial class SplashWindow : Window
{
    private CompositionVisual? _wheelVisual;
    private CompositionAnimationGroup? _motion;

    public SplashWindow() => AvaloniaXamlLoader.Load(this);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_motion is not null)
            return;
        UpdateLayout();
        var wheel = this.FindControl<Image>("Wheel")!;
        _wheelVisual = ElementComposition.GetElementVisual(wheel);
        if (_wheelVisual is null)
            return;
        _wheelVisual.CenterPoint = new Vector3D(wheel.Bounds.Width / 2, wheel.Bounds.Height / 2, 0);
        var compositor = _wheelVisual.Compositor;
        var entranceDuration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEaseOut();
        var scale = compositor.CreateVector3DKeyFrameAnimation();
        scale.Target = "Scale";
        scale.Duration = entranceDuration;
        scale.InsertKeyFrame(0, new Vector3D(0.85, 0.85, 1));
        scale.InsertKeyFrame(1, new Vector3D(1, 1, 1), ease);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.Duration = entranceDuration;
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        var spin = compositor.CreateQuaternionKeyFrameAnimation();
        spin.Target = "Orientation";
        spin.Duration = TimeSpan.FromSeconds(1.5);
        spin.IterationBehavior = AnimationIterationBehavior.Forever;
        spin.InsertKeyFrame(0, Quaternion.Identity);
        spin.InsertKeyFrame(0.25f, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));
        spin.InsertKeyFrame(0.5f, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI));
        spin.InsertKeyFrame(0.75f, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 1.5f));
        spin.InsertKeyFrame(1, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Tau));
        _motion = compositor.CreateAnimationGroup();
        _motion.Add(scale);
        _motion.Add(fade);
        _motion.Add(spin);
        // One render-thread start: UI initialization cannot pause or restart this motion.
        _wheelVisual.StartAnimationGroup(_motion);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_motion is not null)
            _wheelVisual?.StopAnimationGroup(_motion);
        base.OnClosed(e);
    }
}
