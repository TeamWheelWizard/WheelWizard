using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SDL3;
using WheelWizard.Services.Input;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Pages;

public partial class InputPage : UserControlBase
{
    private readonly DispatcherTimer _controllerTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly List<InputBindingRow> _bindingRows;

    private MarioKartInputProfile _profile = new();
    private MarioKartInputAction? _listeningAction;
    private ControllerDeviceOption? _selectedController;
    private bool _suppressControllerChange;

    public InputPage()
    {
        InitializeComponent();

        _bindingRows = MarioKartInputCatalog
            .Definitions.Select(definition => new InputBindingRow(definition.Action, definition.Title))
            .ToList();

        BindingsList.ItemsSource = _bindingRows;
        _controllerTimer.Tick += ControllerTimer_OnTick;

        LoadProfileFromDisk();
        SetFeedback("Choose a controller, click a control, then press the button you want to use.", FeedbackVariant.Info);

        _controllerTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _controllerTimer.Stop();
    }

    private void ControllerTimer_OnTick(object? sender, EventArgs e)
    {
        RefreshControllerOptions();

        if (_listeningAction == null || _selectedController is not { IsConnected: true })
            return;

        var definition = MarioKartInputCatalog.GetDefinition(_listeningAction.Value);
        if (!SdlControllerService.TryCaptureBinding(_selectedController.InstanceId, definition.CaptureKind, out var binding))
            return;

        _profile.DeviceExpression = _selectedController.DeviceExpression;
        _profile.Bindings[_listeningAction.Value] = binding;
        MarioKartInputConfigService.SaveProfile(_profile);

        var actionTitle = definition.Title;
        var bindingDescription = MarioKartInputConfigService.DescribeBinding(_listeningAction.Value, binding);

        _listeningAction = null;
        UpdateBindingRows();
        SetFeedback($"{actionTitle} is now set to {bindingDescription}.", FeedbackVariant.Success);
    }

    private void LoadProfileFromDisk()
    {
        _profile = MarioKartInputConfigService.LoadProfile();
        foreach (var definition in MarioKartInputCatalog.Definitions)
            _profile.Bindings.TryAdd(definition.Action, string.Empty);

        UpdateBindingRows();
        RefreshControllerOptions();
    }

    private void RefreshControllerOptions()
    {
        var connectedControllers = SdlControllerService.GetControllers().ToList();
        var options = new List<ControllerDeviceOption>(connectedControllers);

        if (
            !string.IsNullOrWhiteSpace(_profile.DeviceExpression)
            && options.All(option => !string.Equals(option.DeviceExpression, _profile.DeviceExpression, StringComparison.Ordinal))
        )
        {
            options.Insert(
                0,
                new ControllerDeviceOption(
                    0,
                    ParseSavedDeviceIndex(_profile.DeviceExpression),
                    MarioKartInputConfigService.GetSavedDeviceDisplayName(_profile.DeviceExpression),
                    "Saved mapping, controller not connected right now.",
                    _profile.DeviceExpression,
                    SDL.GamepadType.Unknown,
                    IsConnected: false,
                    IsSavedMapping: true
                )
            );
        }

        _suppressControllerChange = true;
        ControllerDropdown.ItemsSource = options;

        var preferredDeviceExpression = _selectedController?.DeviceExpression ?? _profile.DeviceExpression;
        _selectedController =
            options.FirstOrDefault(option => option.DeviceExpression == preferredDeviceExpression) ?? options.FirstOrDefault();
        ControllerDropdown.SelectedItem = _selectedController;
        _suppressControllerChange = false;

        UpdateControllerState();
    }

    private void UpdateControllerState()
    {
        if (!string.IsNullOrWhiteSpace(SdlControllerService.InitializationError))
        {
            ControllerStatusText.Text = $"Controller support could not start: {SdlControllerService.InitializationError}";
            AutoMapButton.IsEnabled = false;
            TestRumbleButton.IsEnabled = false;
            return;
        }

        if (_selectedController == null)
        {
            ControllerStatusText.Text = "No controller detected yet. Plug one in to remap controls or test rumble.";
            AutoMapButton.IsEnabled = false;
            TestRumbleButton.IsEnabled = false;
            return;
        }

        if (_selectedController.IsConnected)
        {
            ControllerStatusText.Text =
                $"{_selectedController.DisplayName} is connected. Click Change on any action, then press the control you want to use.";
            AutoMapButton.IsEnabled = true;
            TestRumbleButton.IsEnabled = true;
            return;
        }

        ControllerStatusText.Text =
            $"{_selectedController.DisplayName} is saved in Dolphin, but it is not connected right now. Connect it to remap controls or test rumble.";
        AutoMapButton.IsEnabled = false;
        TestRumbleButton.IsEnabled = false;
    }

    private void UpdateBindingRows()
    {
        foreach (var row in _bindingRows)
        {
            var binding = _profile.Bindings.GetValueOrDefault(row.Action, string.Empty);
            row.Value = MarioKartInputConfigService.DescribeBinding(row.Action, binding);
            row.ActionButtonText = _listeningAction == row.Action ? "Listening..." : "Change";
        }
    }

    private void ControllerDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressControllerChange || ControllerDropdown.SelectedItem is not ControllerDeviceOption controller)
            return;

        _selectedController = controller;
        UpdateControllerState();

        if (!controller.IsConnected || string.Equals(_profile.DeviceExpression, controller.DeviceExpression, StringComparison.Ordinal))
            return;

        _profile.DeviceExpression = controller.DeviceExpression;
        MarioKartInputConfigService.SaveProfile(_profile);
        SetFeedback($"{controller.DisplayName} is now the active Mario Kart Wii controller.", FeedbackVariant.Success);
    }

    private void ChangeBinding_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button actionButton || actionButton.Tag is not MarioKartInputAction action)
            return;

        if (_selectedController is not { IsConnected: true } controller)
        {
            SetFeedback("Connect and select a controller before changing controls.", FeedbackVariant.Error);
            return;
        }

        _profile.DeviceExpression = controller.DeviceExpression;
        _listeningAction = action;
        SdlControllerService.BeginCapture(controller.InstanceId);
        UpdateBindingRows();

        var definition = MarioKartInputCatalog.GetDefinition(action);
        var prompt =
            definition.CaptureKind == MarioKartInputCaptureKind.DirectionalInput
                ? $"Move a stick or press the D-pad for {definition.Title}."
                : $"Press the control you want to use for {definition.Title}.";

        SetFeedback(prompt, FeedbackVariant.Info);
    }

    private void AutoMapButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedController is not { IsConnected: true } controller)
        {
            SetFeedback("Connect a controller first to build an automatic layout.", FeedbackVariant.Error);
            return;
        }

        _listeningAction = null;
        _profile = MarioKartInputConfigService.CreateAutoMappedProfile(controller, _profile);
        MarioKartInputConfigService.SaveProfile(_profile);
        UpdateBindingRows();
        SetFeedback("A Mario Kart Wii layout has been applied and saved to Dolphin.", FeedbackVariant.Success);
    }

    private void TestRumbleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedController is not { IsConnected: true } controller)
        {
            SetFeedback("Connect the controller you want to test first.", FeedbackVariant.Error);
            return;
        }

        if (!SdlControllerService.TestRumble(controller.InstanceId))
        {
            SetFeedback("Rumble is not available for this controller right now.", FeedbackVariant.Error);
            return;
        }

        SetFeedback("Rumble test sent.", FeedbackVariant.Success);
    }

    private void ReloadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _listeningAction = null;
        LoadProfileFromDisk();
        SetFeedback("Reloaded the current Dolphin controller setup.", FeedbackVariant.Success);
    }

    private void SetFeedback(string text, FeedbackVariant variant)
    {
        FeedbackText.Text = text;
        FeedbackBorder.Classes.Clear();
        FeedbackBorder.Classes.Add(variant.ToString());
    }

    private static int ParseSavedDeviceIndex(string deviceExpression)
    {
        var split = deviceExpression.Split('/');
        return split.Length >= 2 && int.TryParse(split[1], out var index) ? index : 0;
    }

    private enum FeedbackVariant
    {
        Info,
        Success,
        Error,
    }
}
