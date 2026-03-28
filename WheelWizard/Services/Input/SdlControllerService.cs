using SDL3;

namespace WheelWizard.Services.Input;

public static class SdlControllerService
{
    private const short AxisCaptureThreshold = 16000;
    private const short DirectionalAxisDeltaThreshold = 8000;

    private static readonly SDL.GamepadButton[] CapturableButtons =
    [
        SDL.GamepadButton.South,
        SDL.GamepadButton.East,
        SDL.GamepadButton.West,
        SDL.GamepadButton.North,
        SDL.GamepadButton.LeftShoulder,
        SDL.GamepadButton.RightShoulder,
        SDL.GamepadButton.Start,
        SDL.GamepadButton.Back,
        SDL.GamepadButton.LeftStick,
        SDL.GamepadButton.RightStick,
        SDL.GamepadButton.DPadUp,
        SDL.GamepadButton.DPadDown,
        SDL.GamepadButton.DPadLeft,
        SDL.GamepadButton.DPadRight,
    ];

    private static readonly SDL.GamepadAxis[] CapturableAxes =
    [
        SDL.GamepadAxis.LeftX,
        SDL.GamepadAxis.LeftY,
        SDL.GamepadAxis.RightX,
        SDL.GamepadAxis.RightY,
        SDL.GamepadAxis.LeftTrigger,
        SDL.GamepadAxis.RightTrigger,
    ];

    private static readonly Dictionary<uint, ControllerRuntimeState> OpenControllers = new();
    private static bool _initialized;
    private static string? _initializationError;

    public static string? InitializationError => _initializationError;

    public static IReadOnlyList<ControllerDeviceOption> GetControllers()
    {
        if (!EnsureInitialized())
            return [];

        RefreshControllers();

        return OpenControllers
            .Values.OrderBy(state => state.DolphinDeviceIndex)
            .Select(state => new ControllerDeviceOption(
                state.InstanceId,
                state.DolphinDeviceIndex,
                state.DisplayName,
                state.Subtitle,
                state.DeviceExpression,
                state.ControllerType,
                IsConnected: true
            ))
            .ToList();
    }

    public static bool TryCaptureBinding(uint instanceId, MarioKartInputCaptureKind captureKind, out string binding)
    {
        binding = string.Empty;

        if (!EnsureInitialized())
            return false;

        RefreshControllers();
        if (!OpenControllers.TryGetValue(instanceId, out var controller))
            return false;

        binding = captureKind switch
        {
            MarioKartInputCaptureKind.SingleInput => TryCaptureSingleBinding(controller),
            MarioKartInputCaptureKind.DirectionalInput => TryCaptureDirectionalBinding(controller),
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(binding);
    }

    public static bool TestRumble(uint instanceId)
    {
        if (!EnsureInitialized())
            return false;

        RefreshControllers();
        if (!OpenControllers.TryGetValue(instanceId, out var controller))
            return false;

        return SDL.RumbleGamepad(controller.GamepadHandle, ushort.MaxValue, ushort.MaxValue, 400);
    }

    public static bool TryGetStickPreview(uint instanceId, out ControllerStickPreview preview)
    {
        preview = new(0, 0, 0, 0);

        if (!EnsureInitialized())
            return false;

        RefreshControllers();
        if (!OpenControllers.TryGetValue(instanceId, out var controller))
            return false;

        preview = new(
            NormalizeAxis(controller.CurrentAxes.GetValueOrDefault(SDL.GamepadAxis.LeftX)),
            NormalizeAxis(controller.CurrentAxes.GetValueOrDefault(SDL.GamepadAxis.LeftY)),
            NormalizeAxis(controller.CurrentAxes.GetValueOrDefault(SDL.GamepadAxis.RightX)),
            NormalizeAxis(controller.CurrentAxes.GetValueOrDefault(SDL.GamepadAxis.RightY))
        );
        return true;
    }

    public static void BeginCapture(uint instanceId)
    {
        if (!EnsureInitialized())
            return;

        RefreshControllers();
        if (!OpenControllers.TryGetValue(instanceId, out var controller))
            return;

        controller.StartCaptureSession();
    }

    private static bool EnsureInitialized()
    {
        if (_initialized)
            return true;

        if (_initializationError != null)
            return false;

        try
        {
            if (!SDL.Init(SDL.InitFlags.Gamepad))
            {
                _initializationError = SDL.GetError();
                return false;
            }

            SDL.SetGamepadEventsEnabled(true);
            _initialized = true;
            return true;
        }
        catch (Exception ex)
        {
            _initializationError = ex.Message;
            return false;
        }
    }

    private static void RefreshControllers()
    {
        SDL.PumpEvents();
        SDL.UpdateGamepads();

        var connectedIds = SDL.GetGamepads(out var count) ?? [];
        var connectedIdSet = connectedIds.ToHashSet();

        foreach (var disconnectedId in OpenControllers.Keys.Where(id => !connectedIdSet.Contains(id)).ToList())
        {
            SDL.CloseGamepad(OpenControllers[disconnectedId].GamepadHandle);
            OpenControllers.Remove(disconnectedId);
        }

        for (var index = 0; index < count; index++)
        {
            var instanceId = connectedIds[index];
            if (!OpenControllers.TryGetValue(instanceId, out var controller))
            {
                var gamepad = SDL.OpenGamepad(instanceId);
                if (gamepad == IntPtr.Zero)
                    continue;

                controller = new(instanceId, index, gamepad);
                OpenControllers[instanceId] = controller;
            }

            controller.DolphinDeviceIndex = index;
            controller.Refresh();
        }
    }

    private static string TryCaptureDirectionalBinding(ControllerRuntimeState controller)
    {
        if (
            IsNewlyActiveSinceCaptureStart(controller, SDL.GamepadButton.DPadUp)
            || IsNewlyActiveSinceCaptureStart(controller, SDL.GamepadButton.DPadDown)
            || IsNewlyActiveSinceCaptureStart(controller, SDL.GamepadButton.DPadLeft)
            || IsNewlyActiveSinceCaptureStart(controller, SDL.GamepadButton.DPadRight)
        )
        {
            controller.ClearCaptureSession();
            return "dpad";
        }

        if (
            HasDirectionalAxisChangedSinceCaptureStart(controller, SDL.GamepadAxis.LeftX)
            || HasDirectionalAxisChangedSinceCaptureStart(controller, SDL.GamepadAxis.LeftY)
        )
        {
            controller.ClearCaptureSession();
            return "left-stick";
        }

        if (
            HasDirectionalAxisChangedSinceCaptureStart(controller, SDL.GamepadAxis.RightX)
            || HasDirectionalAxisChangedSinceCaptureStart(controller, SDL.GamepadAxis.RightY)
        )
        {
            controller.ClearCaptureSession();
            return "right-stick";
        }

        return string.Empty;
    }

    private static string TryCaptureSingleBinding(ControllerRuntimeState controller)
    {
        foreach (var button in CapturableButtons)
        {
            if (!IsNewlyActiveSinceCaptureStart(controller, button))
                continue;

            controller.ClearCaptureSession();
            return MapButtonToDolphinExpression(button);
        }

        if (IsNewlyActivatedSinceCaptureStart(controller, SDL.GamepadAxis.LeftTrigger))
        {
            controller.ClearCaptureSession();
            return MarioKartInputConfigService.WrapToken("Trigger L");
        }

        if (IsNewlyActivatedSinceCaptureStart(controller, SDL.GamepadAxis.RightTrigger))
        {
            controller.ClearCaptureSession();
            return MarioKartInputConfigService.WrapToken("Trigger R");
        }

        return string.Empty;
    }

    private static bool IsNewlyActiveSinceCaptureStart(ControllerRuntimeState controller, SDL.GamepadButton button)
    {
        if (!controller.HasCaptureSession)
            controller.StartCaptureSession();

        var baseline = controller.CaptureButtons.GetValueOrDefault(button);
        var current = controller.CurrentButtons.GetValueOrDefault(button);
        return current && !baseline;
    }

    private static bool IsNewlyActivatedSinceCaptureStart(ControllerRuntimeState controller, SDL.GamepadAxis axis)
    {
        if (!controller.HasCaptureSession)
            controller.StartCaptureSession();

        var baseline = controller.CaptureAxes.GetValueOrDefault(axis);
        var current = controller.CurrentAxes.GetValueOrDefault(axis);
        return IsAxisActivated(current) && !IsAxisActivated(baseline);
    }

    private static bool HasDirectionalAxisChangedSinceCaptureStart(ControllerRuntimeState controller, SDL.GamepadAxis axis)
    {
        if (!controller.HasCaptureSession)
            controller.StartCaptureSession();

        var baseline = controller.CaptureAxes.GetValueOrDefault(axis);
        var current = controller.CurrentAxes.GetValueOrDefault(axis);
        return Math.Abs(current - baseline) >= DirectionalAxisDeltaThreshold;
    }

    private static bool IsAxisActivated(short value) => Math.Abs(value) >= AxisCaptureThreshold;

    private static double NormalizeAxis(short value)
    {
        var normalized = value / (double)short.MaxValue;
        return Math.Clamp(normalized, -1d, 1d);
    }

    private static string MapButtonToDolphinExpression(SDL.GamepadButton button)
    {
        return button switch
        {
            SDL.GamepadButton.South => MarioKartInputConfigService.WrapToken("Button A"),
            SDL.GamepadButton.East => MarioKartInputConfigService.WrapToken("Button B"),
            SDL.GamepadButton.West => MarioKartInputConfigService.WrapToken("Button X"),
            SDL.GamepadButton.North => MarioKartInputConfigService.WrapToken("Button Y"),
            SDL.GamepadButton.LeftShoulder => MarioKartInputConfigService.WrapToken("Shoulder L"),
            SDL.GamepadButton.RightShoulder => MarioKartInputConfigService.WrapToken("Shoulder R"),
            SDL.GamepadButton.DPadUp => MarioKartInputConfigService.WrapToken("Pad N"),
            SDL.GamepadButton.DPadDown => MarioKartInputConfigService.WrapToken("Pad S"),
            SDL.GamepadButton.DPadLeft => MarioKartInputConfigService.WrapToken("Pad W"),
            SDL.GamepadButton.DPadRight => MarioKartInputConfigService.WrapToken("Pad E"),
            SDL.GamepadButton.LeftStick => MarioKartInputConfigService.WrapToken("Left Stick"),
            SDL.GamepadButton.RightStick => MarioKartInputConfigService.WrapToken("Right Stick"),
            SDL.GamepadButton.Back => "Back",
            _ => "Start",
        };
    }

    private static string DescribeControllerType(SDL.GamepadType controllerType)
    {
        return controllerType switch
        {
            SDL.GamepadType.Xbox360 => "Xbox 360 style",
            SDL.GamepadType.XboxOne => "Xbox style",
            SDL.GamepadType.PS3 => "PlayStation 3 style",
            SDL.GamepadType.PS4 => "PlayStation 4 style",
            SDL.GamepadType.PS5 => "PlayStation 5 style",
            SDL.GamepadType.NintendoSwitchPro => "Switch Pro style",
            SDL.GamepadType.NintendoSwitchJoyconLeft => "Joy-Con Left",
            SDL.GamepadType.NintendoSwitchJoyconRight => "Joy-Con Right",
            SDL.GamepadType.NintendoSwitchJoyconPair => "Joy-Con pair",
            SDL.GamepadType.GameCube => "GameCube style",
            _ => "Standard controller",
        };
    }

    private sealed class ControllerRuntimeState(uint instanceId, int dolphinDeviceIndex, IntPtr gamepadHandle)
    {
        public uint InstanceId { get; } = instanceId;
        public IntPtr GamepadHandle { get; } = gamepadHandle;
        public int DolphinDeviceIndex { get; set; } = dolphinDeviceIndex;
        public Dictionary<SDL.GamepadButton, bool> CurrentButtons { get; } = new();
        public Dictionary<SDL.GamepadButton, bool> PreviousButtons { get; } = new();
        public Dictionary<SDL.GamepadAxis, short> CurrentAxes { get; } = new();
        public Dictionary<SDL.GamepadAxis, short> PreviousAxes { get; } = new();
        public Dictionary<SDL.GamepadButton, bool> CaptureButtons { get; } = new();
        public Dictionary<SDL.GamepadAxis, short> CaptureAxes { get; } = new();
        public string DeviceExpression => $"SDL/{DolphinDeviceIndex}/{DisplayName}";
        public SDL.GamepadType ControllerType => SDL.GetGamepadType(GamepadHandle);
        public string DisplayName => SDL.GetGamepadName(GamepadHandle);
        public string Subtitle => DescribeControllerType(ControllerType);
        public bool HasCaptureSession { get; private set; }

        public void StartCaptureSession()
        {
            CaptureButtons.Clear();
            CaptureAxes.Clear();

            foreach (var button in CapturableButtons)
                CaptureButtons[button] = CurrentButtons.GetValueOrDefault(button);

            foreach (var axis in CapturableAxes)
                CaptureAxes[axis] = CurrentAxes.GetValueOrDefault(axis);

            HasCaptureSession = true;
        }

        public void ClearCaptureSession()
        {
            CaptureButtons.Clear();
            CaptureAxes.Clear();
            HasCaptureSession = false;
        }

        public void Refresh()
        {
            foreach (var button in CapturableButtons)
            {
                PreviousButtons[button] = CurrentButtons.GetValueOrDefault(button);
                CurrentButtons[button] = SDL.GetGamepadButton(GamepadHandle, button);
            }

            foreach (var axis in CapturableAxes)
            {
                PreviousAxes[axis] = CurrentAxes.GetValueOrDefault(axis);
                CurrentAxes[axis] = SDL.GetGamepadAxis(GamepadHandle, axis);
            }
        }
    }
}
