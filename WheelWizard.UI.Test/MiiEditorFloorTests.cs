using Avalonia.Headless.XUnit;

namespace WheelWizard.UI.Test;

public class MiiEditorFloorTests
{
    [AvaloniaFact]
    public void Floor_FollowsTheCharactersScreenShift_AndAllowsPageMarginOverflow()
    {
        var renderer = NSubstitute.Substitute.For<WheelWizard.MiiRendering.Services.IMiiNativeRenderer>();
        var scene = new WheelWizard.WiiManagement.MiiManagement.Views.Editor.MiiEditorScene(
            renderer,
            NSubstitute.Substitute.For<WheelWizard.MiiAnimations.Library.IMiiAnimationLibrary>(),
            NSubstitute.Substitute.For<WheelWizard.Shared.Calendar.ISeasonalCalendar>(),
            new Testably.Abstractions.RealRandomSystem().Random.Shared,
            false
        );
        scene.Measure(new Avalonia.Size(400, 380));
        scene.Arrange(new Avalonia.Rect(0, 0, 400, 380));
        var view = new WheelWizard.MiiImages.Views.MiiRealtimeView(renderer) { ScreenShiftX = -80 };
        var identity = System.Numerics.Matrix4x4.Identity;
        var frame = new WheelWizard.MiiRendering.Services.MiiRealtimeFrameSetup(
            false,
            true,
            default,
            identity,
            identity,
            identity,
            identity,
            default,
            default,
            default,
            default,
            default
        );
        typeof(WheelWizard.MiiImages.Views.MiiRealtimeView).GetProperty("LastCameraSetup")!.SetValue(view, frame);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(WheelWizard.WiiManagement.MiiManagement.Views.Editor.MiiEditorScene).GetField("_floorView", flags)!.SetValue(scene, view);
        var shifted = (WheelWizard.MiiRendering.Services.MiiRealtimeFrameSetup)
            typeof(WheelWizard.WiiManagement.MiiManagement.Views.Editor.MiiEditorScene)
                .GetMethod("FloorFrame", flags)!
                .Invoke(scene, null)!;
        Assert.Equal(-0.4f, shifted.Projection.M41, 5);
        Assert.Equal(frame.Projection.M42, shifted.Projection.M42);
        var floor = Assert.IsType<WheelWizard.WiiManagement.MiiManagement.Views.Editor.MiiEditorFloor>(scene.Children[0]);
        Assert.False(floor.ClipToBounds);
    }
}
