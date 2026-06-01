using Avalonia.Input;
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
        AddHandler(KeyDownEvent, CaptureKeyboardBinding_OnKeyDown, RoutingStrategies.Tunnel);
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

        TitleText.Text = GetWindowTitle();
        SummaryText.Text = GetSummaryText();

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
        if (
            _listeningIndex is not int listeningIndex
            || _controller is not { IsConnected: true, IsKeyboard: false } controller
            || _profile == null
        )
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

        _listeningIndex = slotIndex;

        if (_controller is { IsConnected: true, IsKeyboard: false } controller)
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
            StatusText.Text = _controller is { IsConnected: true, IsKeyboard: false }
                ? $"Press the controller input or keyboard key you want to use for {ToSlotTitle(listeningIndex)}."
                : $"Press the keyboard key you want to use for {ToSlotTitle(listeningIndex)}.";
            return;
        }

        StatusText.Text = _controller is { IsConnected: true, IsKeyboard: false }
            ? $"{_controller.DisplayName} is ready. Pick a slot and press a controller input or keyboard key."
            : "Pick a slot and press the keyboard key you want to use.";
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

    private void CaptureKeyboardBinding_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_listeningIndex is not int listeningIndex || _profile == null || !KeyboardInputService.TryCreateBinding(e.Key, out var binding))
            return;

        e.Handled = true;
        _bindingSlots[listeningIndex] = binding;
        SaveBindingSlots();

        _listeningIndex = null;
        UpdateStatusText($"Saved {ToSlotTitle(listeningIndex)} as {MarioKartInputConfigService.DescribeSingleInputBinding(binding)}.");
        UpdateRows();
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

    private string GetWindowTitle()
    {
        var definition = MarioKartInputCatalog.GetDefinition(_action);
        return $"{definition.Title} Buttons";
    }

    private string GetSummaryText()
    {
        var definition = MarioKartInputCatalog.GetDefinition(_action);
        return $"Set up to two inputs for {definition.Title.ToLowerInvariant()}. Either input will trigger the action, so you can leave the extra slot empty if you only want one.";
    }

    private static string ToSlotTitle(int slotIndex) => slotIndex == 0 ? "Main" : "Extra";

    private void DoneButton_OnClick(object? sender, RoutedEventArgs e) => Close();
}
