using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Services.Input;
using WheelWizard.Views.Popups.Base;

namespace WheelWizard.Views.Popups.Input;

public partial class StickDeadzoneWindow : PopupContent
{
    private const double VisualizerSize = 180;
    private const double DotSize = 14;

    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    private MarioKartInputProfile? _profile;
    private ControllerDeviceOption? _controller;
    private string _stickBinding = "left-stick";

    public StickDeadzoneWindow()
        : base(true, false, true, "Stick Settings")
    {
        InitializeComponent();
        _previewTimer.Tick += PreviewTimer_OnTick;
    }

    public StickDeadzoneWindow SetProfile(MarioKartInputProfile profile)
    {
        _profile = profile;
        return this;
    }

    public StickDeadzoneWindow SetController(ControllerDeviceOption? controller)
    {
        _controller = controller;
        return this;
    }

    public StickDeadzoneWindow SetStickBinding(string stickBinding)
    {
        _stickBinding = stickBinding;
        return this;
    }

    protected override void BeforeOpen()
    {
        base.BeforeOpen();

        DeadZoneSlider.Value = _profile?.MainStickDeadZonePercent ?? 0;
        TitleText.Text = $"{MarioKartInputConfigService.GetStickBindingDisplayName(_stickBinding)} Deadzone";
        UpdateDeadZoneText();
        UpdatePreview();
        _previewTimer.Start();
    }

    protected override void BeforeClose()
    {
        _previewTimer.Stop();
        base.BeforeClose();
    }

    private void PreviewTimer_OnTick(object? sender, EventArgs e) => UpdatePreview();

    private void DeadZoneSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_profile == null)
            return;

        var percent = (int)Math.Round(e.NewValue);
        if (_profile.MainStickDeadZonePercent == percent)
            return;

        _profile.MainStickDeadZonePercent = percent;
        MarioKartInputConfigService.SaveProfile(_profile);
        UpdateDeadZoneText();
        UpdatePreview();
    }

    private void UpdateDeadZoneText()
    {
        DeadZoneValueText.Text = $"Deadzone: {(int)Math.Round(DeadZoneSlider.Value)}%";
        UpdateDeadZoneCircle();
    }

    private void UpdatePreview()
    {
        if (_controller is not { IsConnected: true })
        {
            ControllerStatusText.Text = "Connect the selected controller to see live stick movement here.";
            LiveInputText.Text = "No live preview yet.";
            CenterDots();
            return;
        }

        if (!SdlControllerService.TryGetStickPreview(_controller.InstanceId, out var preview))
        {
            ControllerStatusText.Text = "Wheel Wizard could not read live stick input right now.";
            LiveInputText.Text = "No live preview yet.";
            CenterDots();
            return;
        }

        var rawInput = _stickBinding == "right-stick" ? (preview.RightX, preview.RightY) : (preview.LeftX, preview.LeftY);
        var filteredInput = ApplyRadialDeadZone(rawInput.Item1, rawInput.Item2, DeadZoneSlider.Value / 100d);

        ControllerStatusText.Text = $"{_controller.DisplayName} is connected. Move the stick to tune the deadzone.";
        LiveInputText.Text =
            $"Raw X {FormatPercent(rawInput.Item1)}, Y {FormatPercent(-rawInput.Item2)}   Filtered X {FormatPercent(filteredInput.X)}, Y {FormatPercent(-filteredInput.Y)}";

        PositionDot(RawDot, rawInput.Item1, rawInput.Item2);
        PositionDot(FilteredDot, filteredInput.X, filteredInput.Y);
    }

    private void CenterDots()
    {
        PositionDot(RawDot, 0, 0);
        PositionDot(FilteredDot, 0, 0);
    }

    private static (double X, double Y) ApplyRadialDeadZone(double x, double y, double deadZonePercent)
    {
        var deadZone = Math.Clamp(deadZonePercent, 0d, 0.95d);
        var magnitude = Math.Sqrt((x * x) + (y * y));
        if (magnitude <= deadZone || magnitude <= 0.0001d)
            return (0, 0);

        var adjustedMagnitude = Math.Clamp((magnitude - deadZone) / (1d - deadZone), 0d, 1d);
        var scale = adjustedMagnitude / magnitude;
        return (x * scale, y * scale);
    }

    private static string FormatPercent(double value) => $"{Math.Round(value * 100):0}%";

    private void UpdateDeadZoneCircle()
    {
        var deadZoneFactor = Math.Clamp(DeadZoneSlider.Value / 100d, 0d, 0.95d);
        var diameter = VisualizerSize * deadZoneFactor;

        DeadZoneCircle.IsVisible = diameter > 0.5d;
        DeadZoneCircle.Width = diameter;
        DeadZoneCircle.Height = diameter;
        Canvas.SetLeft(DeadZoneCircle, (VisualizerSize - diameter) / 2d);
        Canvas.SetTop(DeadZoneCircle, (VisualizerSize - diameter) / 2d);
    }

    private static void PositionDot(Control dot, double x, double y)
    {
        var clampedX = Math.Clamp(x, -1d, 1d);
        var clampedY = Math.Clamp(y, -1d, 1d);
        var center = VisualizerSize / 2d;
        Canvas.SetLeft(dot, center + (clampedX * center) - (DotSize / 2d));
        Canvas.SetTop(dot, center + (clampedY * center) - (DotSize / 2d));
    }

    private void DoneButton_OnClick(object? sender, RoutedEventArgs e) => Close();
}
