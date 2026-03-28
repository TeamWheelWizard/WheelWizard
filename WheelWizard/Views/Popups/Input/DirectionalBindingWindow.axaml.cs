using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Services.Input;
using WheelWizard.Views.Popups.Base;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Popups.Input;

public partial class DirectionalBindingWindow : PopupContent
{
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private MarioKartInputProfile? _profile;
    private ControllerDeviceOption? _controller;
    private MarioKartInputAction _action;
    private DirectionalBindingSet _bindingSet = new();
    private string? _listeningDirection;

    public DirectionalBindingWindow()
        : base(true, false, true, "Directional Controls")
    {
        InitializeComponent();
        _captureTimer.Tick += CaptureTimer_OnTick;
    }

    public DirectionalBindingWindow SetProfile(MarioKartInputProfile profile)
    {
        _profile = profile;
        return this;
    }

    public DirectionalBindingWindow SetController(ControllerDeviceOption? controller)
    {
        _controller = controller;
        return this;
    }

    public DirectionalBindingWindow SetAction(MarioKartInputAction action)
    {
        _action = action;
        return this;
    }

    protected override void BeforeOpen()
    {
        base.BeforeOpen();

        if (_profile != null)
            _bindingSet = MarioKartInputConfigService.GetDirectionalBindingSet(
                _action,
                _profile.Bindings.GetValueOrDefault(_action, string.Empty)
            );

        TitleText.Text = _action == MarioKartInputAction.Steering ? "Directional Steering" : "Directional D-Pad";
        SummaryText.Text =
            _action == MarioKartInputAction.Steering
                ? "These four inputs make up steering when you use a custom directional layout."
                : "These four inputs make up the D-pad directions for tricking and wheelies.";

        _listeningDirection = null;
        UpdateStatusText();
        UpdateDirectionRows();
        _captureTimer.Start();
    }

    protected override void BeforeClose()
    {
        _captureTimer.Stop();
        base.BeforeClose();
    }

    private void CaptureTimer_OnTick(object? sender, EventArgs e)
    {
        if (_listeningDirection == null || _controller is not { IsConnected: true } controller || _profile == null)
            return;

        if (!SdlControllerService.TryCaptureBinding(controller.InstanceId, MarioKartInputCaptureKind.SingleInput, out var binding))
            return;

        var direction = _listeningDirection;
        SetDirectionBinding(direction, binding);
        _profile.Bindings[_action] = MarioKartInputConfigService.CreateDirectionalBinding(_action, _bindingSet);
        MarioKartInputConfigService.SaveProfile(_profile);

        _listeningDirection = null;
        UpdateStatusText($"Saved {ToDirectionTitle(direction)} as {DescribeDirectionBinding(binding)}.");
        UpdateDirectionRows();
    }

    private void ChangeDirection_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string directionKey)
            return;

        if (_controller is not { IsConnected: true } controller)
        {
            _listeningDirection = null;
            UpdateStatusText("Connect the selected controller before changing a direction.");
            UpdateDirectionRows();
            return;
        }

        _listeningDirection = directionKey;
        SdlControllerService.BeginCapture(controller.InstanceId);
        UpdateStatusText();
        UpdateDirectionRows();
    }

    private void UpdateDirectionRows()
    {
        UpValueText.Text = DescribeDirectionBinding(_bindingSet.Up);
        DownValueText.Text = DescribeDirectionBinding(_bindingSet.Down);
        LeftValueText.Text = DescribeDirectionBinding(_bindingSet.Left);
        RightValueText.Text = DescribeDirectionBinding(_bindingSet.Right);

        UpButton.Text = _listeningDirection == "up" ? "Listening..." : "Change";
        DownButton.Text = _listeningDirection == "down" ? "Listening..." : "Change";
        LeftButton.Text = _listeningDirection == "left" ? "Listening..." : "Change";
        RightButton.Text = _listeningDirection == "right" ? "Listening..." : "Change";
    }

    private void UpdateStatusText(string? successText = null)
    {
        if (!string.IsNullOrWhiteSpace(successText))
        {
            StatusText.Text = successText;
            return;
        }

        if (_listeningDirection != null)
        {
            StatusText.Text = _controller is { IsConnected: true }
                ? $"Press the input you want to use for {ToDirectionTitle(_listeningDirection)}."
                : "Connect the selected controller before changing a direction.";
            return;
        }

        StatusText.Text = _controller is { IsConnected: true }
            ? $"{_controller.DisplayName} is ready. Pick a direction and press the input you want to use."
            : "You can review the current directions here. Connect the selected controller to change them.";
    }

    private void SetDirectionBinding(string direction, string binding)
    {
        switch (direction)
        {
            case "up":
                _bindingSet.Up = binding;
                break;
            case "down":
                _bindingSet.Down = binding;
                break;
            case "left":
                _bindingSet.Left = binding;
                break;
            case "right":
                _bindingSet.Right = binding;
                break;
        }
    }

    private static string DescribeDirectionBinding(string binding) =>
        MarioKartInputConfigService.DescribeBinding(MarioKartInputAction.Accelerate, binding);

    private static string ToDirectionTitle(string direction) =>
        direction switch
        {
            "up" => "Up",
            "down" => "Down",
            "left" => "Left",
            "right" => "Right",
            _ => "Direction",
        };

    private void DoneButton_OnClick(object? sender, RoutedEventArgs e) => Close();
}
