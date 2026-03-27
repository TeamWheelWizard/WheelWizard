using SDL3;

namespace WheelWizard.Services.Input;

public enum MarioKartInputAction
{
    Steering,
    Accelerate,
    BrakeReverse,
    UseItem,
    Drift,
    LookBehind,
    TrickWheelie,
    Pause,
}

public enum MarioKartInputCaptureKind
{
    SingleInput,
    DirectionalInput,
}

public sealed record MarioKartInputDefinition(
    MarioKartInputAction Action,
    string Title,
    string Description,
    MarioKartInputCaptureKind CaptureKind
);

public sealed class MarioKartInputProfile
{
    public string SourceSection { get; set; } = "GCPad1";
    public string DeviceExpression { get; set; } = string.Empty;
    public string MainStickCalibration { get; set; } = "100.00";
    public string CStickCalibration { get; set; } = "100.00";
    public string RumbleBinding { get; set; } = string.Empty;
    public Dictionary<MarioKartInputAction, string> Bindings { get; } = new();

    public MarioKartInputProfile Clone()
    {
        var clone = new MarioKartInputProfile
        {
            SourceSection = SourceSection,
            DeviceExpression = DeviceExpression,
            MainStickCalibration = MainStickCalibration,
            CStickCalibration = CStickCalibration,
            RumbleBinding = RumbleBinding,
        };

        foreach (var binding in Bindings)
            clone.Bindings[binding.Key] = binding.Value;

        return clone;
    }
}

public sealed record ControllerDeviceOption(
    uint InstanceId,
    int DolphinDeviceIndex,
    string DisplayName,
    string Subtitle,
    string DeviceExpression,
    SDL.GamepadType ControllerType,
    bool IsConnected,
    bool IsSavedMapping = false
)
{
    public override string ToString() => DisplayName;
}

public static class MarioKartInputCatalog
{
    public static IReadOnlyList<MarioKartInputDefinition> Definitions { get; } =
        [
            new(
                MarioKartInputAction.Steering,
                "Steering",
                "Steer and aim items with one stick or the D-pad.",
                MarioKartInputCaptureKind.DirectionalInput
            ),
            new(MarioKartInputAction.Accelerate, "Accelerate", "Hold this to drive forward.", MarioKartInputCaptureKind.SingleInput),
            new(
                MarioKartInputAction.BrakeReverse,
                "Brake / Reverse",
                "Slow down, reverse, and pivot in tight spots.",
                MarioKartInputCaptureKind.SingleInput
            ),
            new(MarioKartInputAction.UseItem, "Use Item", "Use and hold items.", MarioKartInputCaptureKind.SingleInput),
            new(
                MarioKartInputAction.Drift,
                "Drift / Hop",
                "Drift in manual mode and hop into turns.",
                MarioKartInputCaptureKind.SingleInput
            ),
            new(MarioKartInputAction.LookBehind, "Look Behind", "Check behind you during races.", MarioKartInputCaptureKind.SingleInput),
            new(
                MarioKartInputAction.TrickWheelie,
                "Trick / Wheelie",
                "Perform tricks and bike wheelies.",
                MarioKartInputCaptureKind.SingleInput
            ),
            new(MarioKartInputAction.Pause, "Pause", "Open the in-race pause menu.", MarioKartInputCaptureKind.SingleInput),
        ];

    public static MarioKartInputDefinition GetDefinition(MarioKartInputAction action) =>
        Definitions.First(definition => definition.Action == action);
}
