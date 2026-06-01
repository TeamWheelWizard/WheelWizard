using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SDL3;
using WheelWizard.Services.Input;
using WheelWizard.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views;
using WheelWizard.Views.Popups.Input;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Pages;

public partial class InputPage : UserControlBase
{
    private readonly DispatcherTimer _controllerTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly List<InputBindingRow> _bindingRows;

    private MarioKartInputProfile _profile = new();
    private MarioKartInputAction? _listeningAction;
    private ControllerDeviceOption? _selectedController;
    private MarioKartInputPresetOption? _selectedPreset;
    private IReadOnlyList<MarioKartInputPresetOption> _presetOptions = [];
    private bool _inputConfigAvailable = true;
    private bool _suppressControllerChange;
    private bool _suppressPresetChange;
    private bool _suppressRumbleToggleChange;
    private KeyModifiers _pendingBindingClickModifiers;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    public InputPage()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, CaptureKeyboardBinding_OnKeyDown, RoutingStrategies.Tunnel);

        _bindingRows = MarioKartInputCatalog
            .Definitions.Select(definition => new InputBindingRow(definition.Action, definition.Title))
            .ToList();

        BindingsList.ItemsSource = _bindingRows;
        _controllerTimer.Tick += ControllerTimer_OnTick;

        if (!CanUseInputConfig())
        {
            ShowInputConfigUnavailable();
            return;
        }

        LoadProfileFromDisk();
        _controllerTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _controllerTimer.Stop();
    }

    private void ControllerTimer_OnTick(object? sender, EventArgs e)
    {
        if (!_inputConfigAvailable)
            return;

        RefreshControllerOptions();

        if (_listeningAction == null || _selectedController is not { IsConnected: true, IsKeyboard: false })
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
        RefreshPresetOptions();
        ShowSnackbar($"{actionTitle} is now set to {bindingDescription}.", ViewUtils.SnackbarType.Success);
    }

    private void LoadProfileFromDisk(MarioKartInputPresetOption? preset = null)
    {
        RefreshPresetOptions(preset);

        _profile = MarioKartInputConfigService.LoadProfile(_selectedPreset);
        EnsureAllBindingsExist();

        UpdateBindingRows();
        RefreshControllerOptions();
    }

    private void RefreshPresetOptions(MarioKartInputPresetOption? preferredPreset = null)
    {
        var options = MarioKartInputConfigService.GetPresetOptions();
        var selectedPreset =
            FindMatchingPreset(options, preferredPreset ?? _selectedPreset)
            ?? options.FirstOrDefault(option => option.Kind == MarioKartInputPresetKind.CurrentDolphinSettings)
            ?? options.FirstOrDefault();
        var optionsChanged = !ArePresetOptionsEquivalent(_presetOptions, options);
        var selectionChanged = !AreSamePresetOption(_selectedPreset, selectedPreset);

        _suppressPresetChange = true;
        if (optionsChanged)
            PresetDropdown.ItemsSource = options;

        _presetOptions = options;
        _selectedPreset = selectedPreset;
        if (optionsChanged || selectionChanged)
            PresetDropdown.SelectedItem = _selectedPreset;
        _suppressPresetChange = false;
    }

    private void RefreshControllerOptions()
    {
        if (!_inputConfigAvailable)
            return;

        var connectedControllers = SdlControllerService.GetControllers().ToList();
        var options = new List<ControllerDeviceOption> { KeyboardInputService.CreateKeyboardOption() };
        options.AddRange(connectedControllers);

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
                    MarioKartInputConfigService.GetSavedDeviceSubtitle(_profile.DeviceExpression),
                    _profile.DeviceExpression,
                    SDL.GamepadType.Unknown,
                    IsConnected: false,
                    IsSavedMapping: true,
                    IsKeyboard: KeyboardInputService.IsKeyboardDevice(_profile.DeviceExpression)
                )
            );
        }

        var preferredDeviceExpression = _selectedController?.DeviceExpression ?? _profile.DeviceExpression;
        var selectedController =
            options.FirstOrDefault(option => option.DeviceExpression == preferredDeviceExpression) ?? options.FirstOrDefault();
        var currentOptions = ControllerDropdown.ItemsSource as IEnumerable<ControllerDeviceOption>;
        var optionsChanged = !AreControllerOptionsEquivalent(currentOptions, options);
        var selectionChanged = !AreSameControllerOption(_selectedController, selectedController);

        _suppressControllerChange = true;
        if (optionsChanged)
            ControllerDropdown.ItemsSource = options;

        _selectedController = selectedController;
        if (optionsChanged || selectionChanged)
            ControllerDropdown.SelectedItem = _selectedController;
        _suppressControllerChange = false;

        UpdateControllerState();
    }

    private void UpdateControllerState()
    {
        if (!_inputConfigAvailable)
        {
            ShowInputConfigUnavailable();
            return;
        }

        InputContent.IsVisible = true;
        NoControllerInfo.IsVisible = false;
        UpdateRumbleToggleState();

        if (!string.IsNullOrWhiteSpace(SdlControllerService.InitializationError))
        {
            ControllerStatusText.Text =
                $"Controller support could not start: {SdlControllerService.InitializationError} Keyboard bindings still work.";
            AutoMapButton.IsEnabled = _selectedController is { IsGenericJoystick: false };
            RumbleToggle.IsEnabled = false;
            return;
        }

        if (_selectedController == null)
        {
            ControllerStatusText.Text = "Choose Keyboard or a controller to start mapping your controls.";
            AutoMapButton.IsEnabled = false;
            RumbleToggle.IsEnabled = false;
            return;
        }

        if (_selectedController.IsKeyboard)
        {
            ControllerStatusText.Text =
                "Keyboard is active. Click any single-input action and press a key, or hold Shift while clicking to open the extra-input editor. Steering and Trick / Wheelie open the four-direction editor.";
            AutoMapButton.IsEnabled = true;
            RumbleToggle.IsEnabled = false;
            return;
        }

        if (_selectedController is { IsConnected: true, IsGenericJoystick: true })
        {
            ControllerStatusText.Text =
                $"{_selectedController.DisplayName} is connected as a generic controller. Click each binding and press the exact button, axis, or hat direction you want to use.";
            AutoMapButton.IsEnabled = false;
            RumbleToggle.IsEnabled = false;
            return;
        }

        if (_selectedController.IsConnected)
        {
            ControllerStatusText.Text =
                $"{_selectedController.DisplayName} is connected. Click any binding to remap it, press a keyboard key while listening if you want a keyboard bind, or hold Shift while clicking to open the extra-input editor.";
            AutoMapButton.IsEnabled = true;
            RumbleToggle.IsEnabled = true;
            return;
        }

        ControllerStatusText.Text =
            $"{_selectedController.DisplayName} is saved in Dolphin but not connected right now. You can still bind keyboard keys, or connect it again to capture controller inputs and test rumble.";
        AutoMapButton.IsEnabled = true;
        RumbleToggle.IsEnabled = false;
    }

    private void UpdateRumbleToggleState()
    {
        _suppressRumbleToggleChange = true;
        RumbleToggle.IsChecked = MarioKartInputConfigService.IsRumbleEnabled(_profile);
        _suppressRumbleToggleChange = false;
    }

    private void UpdateBindingRows()
    {
        foreach (var row in _bindingRows)
        {
            var binding = _profile.Bindings.GetValueOrDefault(row.Action, string.Empty);
            var bindingDescription = MarioKartInputConfigService.DescribeBinding(row.Action, binding);
            row.Value = bindingDescription;
            row.ActionButtonText = _listeningAction == row.Action ? "Listening..." : bindingDescription;
            row.IsAdvancedVisible =
                MarioKartInputConfigService.SupportsStickSettings(row.Action, binding)
                || MarioKartInputConfigService.SupportsDirectionEditor(row.Action, binding)
                || (
                    _selectedController?.IsKeyboard == true
                    && (row.Action is MarioKartInputAction.Steering or MarioKartInputAction.TrickWheelie)
                );
        }
    }

    private void PresetDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetChange || !_inputConfigAvailable || PresetDropdown.SelectedItem is not MarioKartInputPresetOption preset)
            return;

        _selectedPreset = preset;
        _listeningAction = null;
        _profile = MarioKartInputConfigService.LoadProfile(preset);
        EnsureAllBindingsExist();

        if (string.IsNullOrWhiteSpace(_profile.DeviceExpression))
        {
            UpdateBindingRows();
            RefreshControllerOptions();
            ShowSnackbar("This preset does not have a Dolphin input device yet.", ViewUtils.SnackbarType.Warning);
            return;
        }

        MarioKartInputConfigService.SaveProfile(_profile);
        RefreshPresetOptions(preset);
        UpdateBindingRows();
        RefreshControllerOptions();
        ShowSnackbar($"{preset.DisplayName} is loaded and ready for launch.", ViewUtils.SnackbarType.Success);
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
        RefreshPresetOptions();
        ShowSnackbar($"{controller.DisplayName} is now the active Mario Kart Wii input device.", ViewUtils.SnackbarType.Success);
    }

    private void ChangeBinding_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button actionButton || actionButton.Tag is not MarioKartInputAction action)
            return;

        var binding = _profile.Bindings.GetValueOrDefault(action, string.Empty);
        var openEditor = (_pendingBindingClickModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;
        _pendingBindingClickModifiers = KeyModifiers.None;
        var definition = MarioKartInputCatalog.GetDefinition(action);

        if (openEditor && MarioKartInputConfigService.SupportsMultiBindingEditor(action))
        {
            OpenMultiBindingEditor(action);
            return;
        }

        if (openEditor && MarioKartInputConfigService.SupportsDirectionEditor(action, binding))
        {
            OpenDirectionalBindingEditor(action);
            return;
        }

        if (
            definition.CaptureKind == MarioKartInputCaptureKind.DirectionalInput
            && (_selectedController?.IsKeyboard == true || _selectedController is not { IsConnected: true, IsKeyboard: false })
        )
        {
            OpenDirectionalBindingEditor(action);
            return;
        }

        if (_selectedController != null)
            _profile.DeviceExpression = _selectedController.DeviceExpression;

        _listeningAction = action;

        if (_selectedController is { IsConnected: true, IsKeyboard: false } controller)
            SdlControllerService.BeginCapture(controller.InstanceId);

        UpdateBindingRows();

        var prompt =
            definition.CaptureKind == MarioKartInputCaptureKind.DirectionalInput
                ? $"Move a stick or press the D-pad for {definition.Title}."
            : _selectedController is { IsConnected: true, IsKeyboard: false }
                ? $"Press the controller input or keyboard key you want to use for {definition.Title}."
            : $"Press the keyboard key you want to use for {definition.Title}.";

        ShowSnackbar(prompt, ViewUtils.SnackbarType.Warning);
    }

    private void CaptureKeyboardBinding_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_listeningAction is not { } action)
            return;

        var definition = MarioKartInputCatalog.GetDefinition(action);
        if (
            definition.CaptureKind != MarioKartInputCaptureKind.SingleInput
            || !KeyboardInputService.TryCreateBinding(e.Key, out var binding)
        )
            return;

        e.Handled = true;

        if (_selectedController != null)
            _profile.DeviceExpression = _selectedController.DeviceExpression;

        _profile.Bindings[action] = binding;
        MarioKartInputConfigService.SaveProfile(_profile);

        var actionTitle = definition.Title;
        var bindingDescription = MarioKartInputConfigService.DescribeBinding(action, binding);

        _listeningAction = null;
        UpdateBindingRows();
        RefreshPresetOptions();
        ShowSnackbar($"{actionTitle} is now set to {bindingDescription}.", ViewUtils.SnackbarType.Success);
    }

    private void BindingButton_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _pendingBindingClickModifiers = e.KeyModifiers;
    }

    private async void OpenMultiBindingEditor(MarioKartInputAction action)
    {
        _listeningAction = null;
        UpdateBindingRows();

        await new MultiBindingWindow().SetProfile(_profile).SetController(_selectedController).SetAction(action).ShowDialog();

        LoadProfileFromDisk(_selectedPreset);
    }

    private async void OpenDirectionalBindingEditor(MarioKartInputAction action)
    {
        _listeningAction = null;
        UpdateBindingRows();

        await new DirectionalBindingWindow().SetProfile(_profile).SetController(_selectedController).SetAction(action).ShowDialog();

        LoadProfileFromDisk(_selectedPreset);
    }

    private async void AdvancedBinding_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button advancedButton || advancedButton.Tag is not MarioKartInputAction action)
            return;

        _listeningAction = null;
        UpdateBindingRows();

        var binding = _profile.Bindings.GetValueOrDefault(action, string.Empty);
        if (MarioKartInputConfigService.SupportsStickSettings(action, binding))
        {
            await new StickDeadzoneWindow().SetProfile(_profile).SetController(_selectedController).SetStickBinding(binding).ShowDialog();

            LoadProfileFromDisk(_selectedPreset);
            return;
        }

        var isKeyboardDirectionalAction =
            _selectedController?.IsKeyboard == true && (action is MarioKartInputAction.Steering or MarioKartInputAction.TrickWheelie);

        if (!MarioKartInputConfigService.SupportsDirectionEditor(action, binding) && !isKeyboardDirectionalAction)
            return;

        OpenDirectionalBindingEditor(action);
    }

    private void AutoMapButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedController == null)
        {
            ShowSnackbar("Choose Keyboard or a controller first to build an automatic layout.", ViewUtils.SnackbarType.Danger);
            return;
        }

        if (_selectedController.IsGenericJoystick)
        {
            ShowSnackbar(
                "Generic controllers need manual mapping because their button numbers are device-specific.",
                ViewUtils.SnackbarType.Warning
            );
            return;
        }

        _listeningAction = null;
        _profile = MarioKartInputConfigService.CreateAutoMappedProfile(_selectedController, _profile);
        MarioKartInputConfigService.SaveProfile(_profile);
        UpdateBindingRows();
        UpdateRumbleToggleState();
        RefreshPresetOptions();
        ShowSnackbar($"{_selectedController.DisplayName} auto map applied and saved to Dolphin.", ViewUtils.SnackbarType.Success);
    }

    private void RumbleToggle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_suppressRumbleToggleChange || sender is not CheckBox rumbleToggle)
            return;

        if (_selectedController?.IsKeyboard == true)
        {
            UpdateRumbleToggleState();
            ShowSnackbar("Rumble only applies to controllers.", ViewUtils.SnackbarType.Danger);
            return;
        }

        if (_selectedController is not { IsConnected: true, IsKeyboard: false } controller)
        {
            UpdateRumbleToggleState();
            ShowSnackbar("Connect the controller you want to configure first.", ViewUtils.SnackbarType.Danger);
            return;
        }

        var enableRumble = rumbleToggle.IsChecked == true;

        _profile.DeviceExpression = controller.DeviceExpression;
        MarioKartInputConfigService.SetRumbleEnabled(_profile, enableRumble);
        MarioKartInputConfigService.SaveProfile(_profile);
        RefreshPresetOptions();

        if (!enableRumble)
        {
            ShowSnackbar("Rumble disabled and saved to Dolphin.", ViewUtils.SnackbarType.Success);
            return;
        }

        if (!SdlControllerService.TestRumble(controller.InstanceId))
        {
            ShowSnackbar(
                "Rumble enabled and saved to Dolphin, but this controller did not accept a test rumble right now.",
                ViewUtils.SnackbarType.Danger
            );
            return;
        }

        ShowSnackbar("Rumble enabled, saved to Dolphin, and tested.", ViewUtils.SnackbarType.Success);
    }

    private void ReloadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _listeningAction = null;
        LoadProfileFromDisk(_selectedPreset);
        ShowSnackbar("Reloaded the current Dolphin controller setup.", ViewUtils.SnackbarType.Success);
    }

    private static void ShowSnackbar(string text, ViewUtils.SnackbarType type) => ViewUtils.ShowSnackbar(text, type);

    private bool CanUseInputConfig()
    {
        try
        {
            return SettingsService.USER_FOLDER_PATH.IsValid();
        }
        catch
        {
            return false;
        }
    }

    private void ShowInputConfigUnavailable()
    {
        _inputConfigAvailable = false;
        _controllerTimer.Stop();
        InputContent.IsVisible = false;
        NoControllerInfo.IsVisible = true;
        NoControllerInfo.Title = "Dolphin user folder needed";
        NoControllerInfo.BodyText = "Set your Dolphin user folder in Settings before editing input.";
        ReloadButton.IsEnabled = false;
        AutoMapButton.IsEnabled = false;
        RumbleToggle.IsEnabled = false;
    }

    private void EnsureAllBindingsExist()
    {
        foreach (var definition in MarioKartInputCatalog.Definitions)
            _profile.Bindings.TryAdd(definition.Action, string.Empty);
    }

    private static int ParseSavedDeviceIndex(string deviceExpression)
    {
        var split = deviceExpression.Split('/');
        return split.Length >= 2 && int.TryParse(split[1], out var index) ? index : 0;
    }

    private static MarioKartInputPresetOption? FindMatchingPreset(
        IReadOnlyList<MarioKartInputPresetOption> options,
        MarioKartInputPresetOption? preset
    )
    {
        if (preset == null)
            return null;

        return options.FirstOrDefault(option => AreSamePresetOption(option, preset));
    }

    private static bool AreControllerOptionsEquivalent(
        IEnumerable<ControllerDeviceOption>? currentOptions,
        IReadOnlyList<ControllerDeviceOption> nextOptions
    )
    {
        if (currentOptions == null)
            return false;

        using var currentEnumerator = currentOptions.GetEnumerator();
        using var nextEnumerator = nextOptions.GetEnumerator();

        while (true)
        {
            var hasCurrent = currentEnumerator.MoveNext();
            var hasNext = nextEnumerator.MoveNext();

            if (hasCurrent != hasNext)
                return false;

            if (!hasCurrent)
                return true;

            if (!AreSameControllerOption(currentEnumerator.Current, nextEnumerator.Current))
                return false;
        }
    }

    private static bool AreSameControllerOption(ControllerDeviceOption? left, ControllerDeviceOption? right)
    {
        if (left == null || right == null)
            return left == right;

        return left.InstanceId == right.InstanceId
            && left.DolphinDeviceIndex == right.DolphinDeviceIndex
            && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
            && string.Equals(left.Subtitle, right.Subtitle, StringComparison.Ordinal)
            && string.Equals(left.DeviceExpression, right.DeviceExpression, StringComparison.Ordinal)
            && left.ControllerType == right.ControllerType
            && left.IsConnected == right.IsConnected
            && left.IsSavedMapping == right.IsSavedMapping
            && left.IsKeyboard == right.IsKeyboard
            && left.IsGenericJoystick == right.IsGenericJoystick;
    }

    private static bool ArePresetOptionsEquivalent(
        IEnumerable<MarioKartInputPresetOption>? currentOptions,
        IReadOnlyList<MarioKartInputPresetOption> nextOptions
    )
    {
        if (currentOptions == null)
            return false;

        using var currentEnumerator = currentOptions.GetEnumerator();
        using var nextEnumerator = nextOptions.GetEnumerator();

        while (true)
        {
            var hasCurrent = currentEnumerator.MoveNext();
            var hasNext = nextEnumerator.MoveNext();

            if (hasCurrent != hasNext)
                return false;

            if (!hasCurrent)
                return true;

            if (!AreSamePresetOption(currentEnumerator.Current, nextEnumerator.Current))
                return false;
        }
    }

    private static bool AreSamePresetOption(MarioKartInputPresetOption? left, MarioKartInputPresetOption? right)
    {
        if (left == null || right == null)
            return left == right;

        return left.Kind == right.Kind
            && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
            && string.Equals(left.Subtitle, right.Subtitle, StringComparison.Ordinal)
            && string.Equals(left.FilePath, right.FilePath, StringComparison.Ordinal);
    }
}
