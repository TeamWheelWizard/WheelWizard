using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Services.Input;
using WheelWizard.Views.Popups.Base;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Popups.Input;

public partial class MultiBindingWindow : PopupContent
{
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private MarioKartInputProfile? _profile;
    private ControllerDeviceOption? _controller;
    private MarioKartInputAction _action;
    private readonly string[] _bindingSlots = new string[2];
    private int? _listeningIndex;

    public MultiBindingWindow()
        : base(true, false, true, "Custom Bindings")
    {
        InitializeComponent();
        _captureTimer.Tick += CaptureTimer_OnTick;
    }

    public MultiBindingWindow SetProfile(MarioKartInputProfile profile)
    {
        _profile = profile;
        return this;
    }

    public MultiBindingWindow SetController(ControllerDeviceOption? controller)
    {
        _controller = controller;
        return this;
    }

    public MultiBindingWindow SetAction(MarioKartInputAction action)
    {
        _action = action;
        return this;
    }

    protected override void BeforeOpen()
    {
        base.BeforeOpen();

        TitleText.Text = _action == MarioKartInputAction.LookBehind ? "Look Behind Buttons" : "Custom Bindings";
        SummaryText.Text =
            _action == MarioKartInputAction.LookBehind
                ? "Set up to two inputs for looking behind. If both are set, either one can trigger the action."
                : "Set up to two inputs for this action.";

        LoadBindingSlots();
        _listeningIndex = null;
        UpdateStatusText();
        UpdateRows();
        _captureTimer.Start();
    }

    protected override void BeforeClose()
    {
        _captureTimer.Stop();
        base.BeforeClose();
    }

    private void CaptureTimer_OnTick(object? sender, EventArgs e)
    {
        if (_listeningIndex is not int listeningIndex || _controller is not { IsConnected: true } controller || _profile == null)
            return;

        if (!SdlControllerService.TryCaptureBinding(controller.InstanceId, MarioKartInputCaptureKind.SingleInput, out var binding))
            return;

        _bindingSlots[listeningIndex] = binding;
        SaveBindingSlots();

        _listeningIndex = null;
        UpdateStatusText($"Saved {ToSlotTitle(listeningIndex)} as {MarioKartInputConfigService.DescribeSingleInputBinding(binding)}.");
        UpdateRows();
    }

    private void ChangeBinding_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !TryGetSlotIndex(button.Tag, out var slotIndex))
            return;

        if (_controller is not { IsConnected: true } controller)
        {
            _listeningIndex = null;
            UpdateStatusText("Connect the selected controller before changing these bindings.");
            UpdateRows();
            return;
        }

        _listeningIndex = slotIndex;
        SdlControllerService.BeginCapture(controller.InstanceId);
        UpdateStatusText();
        UpdateRows();
    }

    private void ClearBinding_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !TryGetSlotIndex(button.Tag, out var slotIndex))
            return;

        if (string.IsNullOrWhiteSpace(_bindingSlots[slotIndex]))
            return;

        _listeningIndex = null;
        _bindingSlots[slotIndex] = string.Empty;
        SaveBindingSlots();
        UpdateStatusText($"{ToSlotTitle(slotIndex)} cleared.");
        UpdateRows();
    }

    private void UpdateRows()
    {
        PrimaryValueText.Text = MarioKartInputConfigService.DescribeSingleInputBinding(_bindingSlots[0]);
        SecondaryValueText.Text = MarioKartInputConfigService.DescribeSingleInputBinding(_bindingSlots[1]);

        PrimaryButton.Text = _listeningIndex == 0 ? "Listening..." : "Change";
        SecondaryButton.Text = _listeningIndex == 1 ? "Listening..." : "Change";

        PrimaryClearButton.IsEnabled = !string.IsNullOrWhiteSpace(_bindingSlots[0]);
        SecondaryClearButton.IsEnabled = !string.IsNullOrWhiteSpace(_bindingSlots[1]);
    }

    private void UpdateStatusText(string? message = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText.Text = message;
            return;
        }

        if (_listeningIndex is int listeningIndex)
        {
            StatusText.Text = _controller is { IsConnected: true }
                ? $"Press the input you want to use for {ToSlotTitle(listeningIndex)}."
                : "Connect the selected controller before changing these bindings.";
            return;
        }

        StatusText.Text = _controller is { IsConnected: true }
            ? $"{_controller.DisplayName} is ready. Pick a slot and press the input you want to use."
            : "You can review the current bindings here. Connect the selected controller to change them.";
    }

    private void LoadBindingSlots()
    {
        Array.Clear(_bindingSlots);

        if (_profile == null)
            return;

        var tokens = MarioKartInputConfigService.GetBindingTokens(_profile.Bindings.GetValueOrDefault(_action, string.Empty));
        for (var index = 0; index < Math.Min(tokens.Count, _bindingSlots.Length); index++)
            _bindingSlots[index] = tokens[index];
    }

    private void SaveBindingSlots()
    {
        if (_profile == null)
            return;

        _profile.Bindings[_action] = MarioKartInputConfigService.CreateCombinedBinding(_bindingSlots);
        MarioKartInputConfigService.SaveProfile(_profile);
        LoadBindingSlots();
    }

    private static bool TryGetSlotIndex(object? tag, out int slotIndex)
    {
        slotIndex = -1;

        return tag switch
        {
            int intValue when intValue is 0 or 1 => (slotIndex = intValue) >= 0,
            string stringValue when int.TryParse(stringValue, out var parsedIndex) && parsedIndex is 0 or 1 => (slotIndex = parsedIndex)
                >= 0,
            _ => false,
        };
    }

    private static string ToSlotTitle(int slotIndex) => slotIndex == 0 ? "Primary" : "Secondary";

    private void DoneButton_OnClick(object? sender, RoutedEventArgs e) => Close();
}
