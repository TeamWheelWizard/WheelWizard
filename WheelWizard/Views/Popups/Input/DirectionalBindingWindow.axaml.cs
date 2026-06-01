using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Services.Input;
using WheelWizard.Views.Popups.Base;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Popups.Input;

public partial class DirectionalBindingWindow : PopupContent
{
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Dictionary<string, string[]> _bindingSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["up"] = new string[2],
        ["down"] = new string[2],
        ["left"] = new string[2],
        ["right"] = new string[2],
    };

    private MarioKartInputProfile? _profile;
    private ControllerDeviceOption? _controller;
    private MarioKartInputAction _action;
    private DirectionalBindingSet _bindingSet = new();
    private (string Direction, int SlotIndex)? _listeningTarget;

    public DirectionalBindingWindow()
        : base(true, false, true, "Directional Controls")
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, CaptureKeyboardBinding_OnKeyDown, RoutingStrategies.Tunnel);
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
                ? "These four directions make up steering when you use a custom directional layout. Each direction can have a main and extra input."
                : "These four D-pad directions control tricks and wheelies. Each direction can have a main and extra input.";

        LoadBindingSlots();
        _listeningTarget = null;
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
        if (
            _listeningTarget is not { } listeningTarget
            || _controller is not { IsConnected: true, IsKeyboard: false } controller
            || _profile == null
        )
            return;

        if (!SdlControllerService.TryCaptureBinding(controller.InstanceId, MarioKartInputCaptureKind.SingleInput, out var binding))
            return;

        _bindingSlots[listeningTarget.Direction][listeningTarget.SlotIndex] = binding;
        SaveBindingSlots();

        _listeningTarget = null;
        UpdateStatusText(
            $"Saved {ToDirectionTitle(listeningTarget.Direction)} {ToSlotTitle(listeningTarget.SlotIndex)} as {DescribeDirectionBinding(binding)}."
        );
        UpdateDirectionRows();
    }

    private void ChangeDirection_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !TryParseSlotTag(button.Tag, out var directionKey, out var slotIndex))
            return;

        _listeningTarget = (directionKey, slotIndex);

        if (_controller is { IsConnected: true, IsKeyboard: false } controller)
            SdlControllerService.BeginCapture(controller.InstanceId);

        UpdateStatusText();
        UpdateDirectionRows();
    }

    private void ClearDirection_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !TryParseSlotTag(button.Tag, out var directionKey, out var slotIndex))
            return;

        if (string.IsNullOrWhiteSpace(_bindingSlots[directionKey][slotIndex]))
            return;

        _listeningTarget = null;
        _bindingSlots[directionKey][slotIndex] = string.Empty;
        SaveBindingSlots();
        UpdateStatusText($"{ToDirectionTitle(directionKey)} {ToSlotTitle(slotIndex)} cleared.");
        UpdateDirectionRows();
    }

    private void UpdateDirectionRows()
    {
        UpMainValueText.Text = DescribeDirectionBinding(_bindingSlots["up"][0]);
        UpExtraValueText.Text = DescribeDirectionBinding(_bindingSlots["up"][1]);
        DownMainValueText.Text = DescribeDirectionBinding(_bindingSlots["down"][0]);
        DownExtraValueText.Text = DescribeDirectionBinding(_bindingSlots["down"][1]);
        LeftMainValueText.Text = DescribeDirectionBinding(_bindingSlots["left"][0]);
        LeftExtraValueText.Text = DescribeDirectionBinding(_bindingSlots["left"][1]);
        RightMainValueText.Text = DescribeDirectionBinding(_bindingSlots["right"][0]);
        RightExtraValueText.Text = DescribeDirectionBinding(_bindingSlots["right"][1]);

        UpMainButton.Text = IsListening("up", 0) ? "Listening..." : "Change";
        UpExtraButton.Text = IsListening("up", 1) ? "Listening..." : "Change";
        DownMainButton.Text = IsListening("down", 0) ? "Listening..." : "Change";
        DownExtraButton.Text = IsListening("down", 1) ? "Listening..." : "Change";
        LeftMainButton.Text = IsListening("left", 0) ? "Listening..." : "Change";
        LeftExtraButton.Text = IsListening("left", 1) ? "Listening..." : "Change";
        RightMainButton.Text = IsListening("right", 0) ? "Listening..." : "Change";
        RightExtraButton.Text = IsListening("right", 1) ? "Listening..." : "Change";

        UpMainClearButton.IsEnabled = !string.IsNullOrWhiteSpace(_bindingSlots["up"][0]);
        UpExtraClearButton.IsEnabled = !string.IsNullOrWhiteSpace(_bindingSlots["up"][1]);
        DownMainClearButton.IsEnabled = !string.IsNullOrWhiteSpace(_bindingSlots["down"][0]);
        DownExtraClearButton.IsEnabled = !string.IsNullOrWhiteSpace(_bindingSlots["down"][1]);
        LeftMainClearButton.IsEnabled = !string.IsNullOrWhiteSpace(_bindingSlots["left"][0]);
        LeftExtraClearButton.IsEnabled = !string.IsNullOrWhiteSpace(_bindingSlots["left"][1]);
        RightMainClearButton.IsEnabled = !string.IsNullOrWhiteSpace(_bindingSlots["right"][0]);
        RightExtraClearButton.IsEnabled = !string.IsNullOrWhiteSpace(_bindingSlots["right"][1]);
    }

    private void UpdateStatusText(string? successText = null)
    {
        if (!string.IsNullOrWhiteSpace(successText))
        {
            StatusText.Text = successText;
            return;
        }

        if (_listeningTarget is { } listeningTarget)
        {
            StatusText.Text = _controller is { IsConnected: true, IsKeyboard: false }
                ? $"Press the controller input or keyboard key you want to use for {ToDirectionTitle(listeningTarget.Direction)} {ToSlotTitle(listeningTarget.SlotIndex)}."
                : $"Press the keyboard key you want to use for {ToDirectionTitle(listeningTarget.Direction)} {ToSlotTitle(listeningTarget.SlotIndex)}.";
            return;
        }

        StatusText.Text = _controller is { IsConnected: true, IsKeyboard: false }
            ? $"{_controller.DisplayName} is ready. Pick any main or extra slot and press a controller input or keyboard key."
            : "Pick any main or extra slot and press the keyboard key you want to use.";
    }

    private void LoadBindingSlots()
    {
        Array.Clear(_bindingSlots["up"]);
        Array.Clear(_bindingSlots["down"]);
        Array.Clear(_bindingSlots["left"]);
        Array.Clear(_bindingSlots["right"]);

        LoadDirectionSlots("up", _bindingSet.Up);
        LoadDirectionSlots("down", _bindingSet.Down);
        LoadDirectionSlots("left", _bindingSet.Left);
        LoadDirectionSlots("right", _bindingSet.Right);
    }

    private void LoadDirectionSlots(string direction, string binding)
    {
        var tokens = MarioKartInputConfigService.GetBindingTokens(binding);
        for (var index = 0; index < Math.Min(tokens.Count, _bindingSlots[direction].Length); index++)
            _bindingSlots[direction][index] = tokens[index];
    }

    private void SaveBindingSlots()
    {
        if (_profile == null)
            return;

        SetDirectionBinding("up", MarioKartInputConfigService.CreateCombinedBinding(_bindingSlots["up"]));
        SetDirectionBinding("down", MarioKartInputConfigService.CreateCombinedBinding(_bindingSlots["down"]));
        SetDirectionBinding("left", MarioKartInputConfigService.CreateCombinedBinding(_bindingSlots["left"]));
        SetDirectionBinding("right", MarioKartInputConfigService.CreateCombinedBinding(_bindingSlots["right"]));

        _profile.Bindings[_action] = MarioKartInputConfigService.CreateDirectionalBinding(_action, _bindingSet);
        MarioKartInputConfigService.SaveProfile(_profile);
        LoadBindingSlots();
    }

    private void CaptureKeyboardBinding_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (
            _listeningTarget is not { } listeningTarget
            || _profile == null
            || !KeyboardInputService.TryCreateBinding(e.Key, out var binding)
        )
            return;

        e.Handled = true;
        _bindingSlots[listeningTarget.Direction][listeningTarget.SlotIndex] = binding;
        SaveBindingSlots();

        _listeningTarget = null;
        UpdateStatusText(
            $"Saved {ToDirectionTitle(listeningTarget.Direction)} {ToSlotTitle(listeningTarget.SlotIndex)} as {DescribeDirectionBinding(binding)}."
        );
        UpdateDirectionRows();
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

    private bool IsListening(string direction, int slotIndex) =>
        _listeningTarget is { } listeningTarget
        && string.Equals(listeningTarget.Direction, direction, StringComparison.OrdinalIgnoreCase)
        && listeningTarget.SlotIndex == slotIndex;

    private static string ToDirectionTitle(string direction) =>
        direction switch
        {
            "up" => "Up",
            "down" => "Down",
            "left" => "Left",
            "right" => "Right",
            _ => "Direction",
        };

    private static string ToSlotTitle(int slotIndex) => slotIndex == 0 ? "main" : "extra";

    private static bool TryParseSlotTag(object? tag, out string direction, out int slotIndex)
    {
        direction = string.Empty;
        slotIndex = -1;

        if (tag is not string tagValue)
            return false;

        var parts = tagValue.Split(':', count: 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out slotIndex) || slotIndex is < 0 or > 1)
            return false;

        direction = parts[0];
        return direction is "up" or "down" or "left" or "right";
    }

    private void DoneButton_OnClick(object? sender, RoutedEventArgs e) => Close();
}
