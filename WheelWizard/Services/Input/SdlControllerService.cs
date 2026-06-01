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
                IsConnected: true,
                IsGenericJoystick: !state.IsGamepad
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

        if (!controller.IsGamepad)
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

        preview = controller.IsGamepad
            ? new(
                NormalizeAxis(controller.CurrentAxes.GetValueOrDefault(SDL.GamepadAxis.LeftX)),
                NormalizeAxis(controller.CurrentAxes.GetValueOrDefault(SDL.GamepadAxis.LeftY)),
                NormalizeAxis(controller.CurrentAxes.GetValueOrDefault(SDL.GamepadAxis.RightX)),
                NormalizeAxis(controller.CurrentAxes.GetValueOrDefault(SDL.GamepadAxis.RightY))
            )
            : new(
                NormalizeAxis(controller.CurrentJoystickAxes.GetValueOrDefault(0)),
                NormalizeAxis(controller.CurrentJoystickAxes.GetValueOrDefault(1)),
                NormalizeAxis(controller.CurrentJoystickAxes.GetValueOrDefault(2)),
                NormalizeAxis(controller.CurrentJoystickAxes.GetValueOrDefault(3))
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
            if (!SDL.Init(SDL.InitFlags.Gamepad | SDL.InitFlags.Joystick))
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
        SDL.UpdateJoysticks();

        var connectedIds = SDL.GetJoysticks(out var count) ?? [];
        var connectedIdSet = connectedIds.ToHashSet();

        foreach (var disconnectedId in OpenControllers.Keys.Where(id => !connectedIdSet.Contains(id)).ToList())
        {
            OpenControllers[disconnectedId].Close();
            OpenControllers.Remove(disconnectedId);
        }

        for (var index = 0; index < count; index++)
        {
            var instanceId = connectedIds[index];
            if (!OpenControllers.TryGetValue(instanceId, out var controller))
            {
                controller = CreateControllerState(instanceId, index);
                if (controller == null)
                    continue;

                OpenControllers[instanceId] = controller;
            }

            controller.DolphinDeviceIndex = index;
            controller.Refresh();
        }
    }

    private static ControllerRuntimeState? CreateControllerState(uint instanceId, int dolphinDeviceIndex)
    {
        if (SDL.IsGamepad(instanceId))
        {
            var gamepad = SDL.OpenGamepad(instanceId);
            return gamepad == IntPtr.Zero ? null : ControllerRuntimeState.ForGamepad(instanceId, dolphinDeviceIndex, gamepad);
        }

        var joystick = SDL.OpenJoystick(instanceId);
        return joystick == IntPtr.Zero ? null : ControllerRuntimeState.ForJoystick(instanceId, dolphinDeviceIndex, joystick);
    }

    private static string TryCaptureDirectionalBinding(ControllerRuntimeState controller)
    {
        if (!controller.IsGamepad)
            return TryCaptureGenericDirectionalBinding(controller);

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
        if (!controller.IsGamepad)
            return TryCaptureGenericSingleBinding(controller);

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

    private static string TryCaptureGenericDirectionalBinding(ControllerRuntimeState controller)
    {
        if (TryCaptureGenericHat(controller, out var hatBinding))
        {
            controller.ClearCaptureSession();
            return MarioKartInputConfigService.CreateDirectionalBinding(
                MarioKartInputAction.Steering,
                new DirectionalBindingSet
                {
                    Up = MarioKartInputConfigService.WrapToken($"{hatBinding} N"),
                    Down = MarioKartInputConfigService.WrapToken($"{hatBinding} S"),
                    Left = MarioKartInputConfigService.WrapToken($"{hatBinding} W"),
                    Right = MarioKartInputConfigService.WrapToken($"{hatBinding} E"),
                }
            );
        }

        for (var axisIndex = 0; axisIndex < controller.JoystickAxisCount; axisIndex++)
        {
            if (!HasJoystickAxisChangedSinceCaptureStart(controller, axisIndex))
                continue;

            var horizontalAxisIndex = axisIndex % 2 == 0 ? axisIndex : axisIndex - 1;
            var verticalAxisIndex = horizontalAxisIndex + 1;
            if (verticalAxisIndex >= controller.JoystickAxisCount)
                return string.Empty;

            controller.ClearCaptureSession();
            return MarioKartInputConfigService.CreateDirectionalBinding(
                MarioKartInputAction.Steering,
                new DirectionalBindingSet
                {
                    Up = MarioKartInputConfigService.WrapToken($"Axis {verticalAxisIndex}-"),
                    Down = MarioKartInputConfigService.WrapToken($"Axis {verticalAxisIndex}+"),
                    Left = MarioKartInputConfigService.WrapToken($"Axis {horizontalAxisIndex}-"),
                    Right = MarioKartInputConfigService.WrapToken($"Axis {horizontalAxisIndex}+"),
                }
            );
        }

        return string.Empty;
    }

    private static string TryCaptureGenericSingleBinding(ControllerRuntimeState controller)
    {
        for (var buttonIndex = 0; buttonIndex < controller.JoystickButtonCount; buttonIndex++)
        {
            if (!IsJoystickButtonNewlyActiveSinceCaptureStart(controller, buttonIndex))
                continue;

            controller.ClearCaptureSession();
            return MarioKartInputConfigService.WrapToken($"Button {buttonIndex}");
        }

        if (TryCaptureGenericHat(controller, out var hatBinding))
        {
            controller.ClearCaptureSession();
            return MarioKartInputConfigService.WrapToken(hatBinding);
        }

        for (var axisIndex = 0; axisIndex < controller.JoystickAxisCount; axisIndex++)
        {
            if (!IsJoystickAxisNewlyActivatedSinceCaptureStart(controller, axisIndex))
                continue;

            controller.ClearCaptureSession();
            var axisValue = controller.CurrentJoystickAxes.GetValueOrDefault(axisIndex);
            return MarioKartInputConfigService.WrapToken($"Axis {axisIndex}{(axisValue >= 0 ? "+" : "-")}");
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

    private static bool IsJoystickButtonNewlyActiveSinceCaptureStart(ControllerRuntimeState controller, int buttonIndex)
    {
        if (!controller.HasCaptureSession)
            controller.StartCaptureSession();

        var baseline = controller.CaptureJoystickButtons.GetValueOrDefault(buttonIndex);
        var current = controller.CurrentJoystickButtons.GetValueOrDefault(buttonIndex);
        return current && !baseline;
    }

    private static bool IsJoystickAxisNewlyActivatedSinceCaptureStart(ControllerRuntimeState controller, int axisIndex)
    {
        if (!controller.HasCaptureSession)
            controller.StartCaptureSession();

        var baseline = controller.CaptureJoystickAxes.GetValueOrDefault(axisIndex);
        var current = controller.CurrentJoystickAxes.GetValueOrDefault(axisIndex);
        return IsAxisActivated(current) && !IsAxisActivated(baseline);
    }

    private static bool HasJoystickAxisChangedSinceCaptureStart(ControllerRuntimeState controller, int axisIndex)
    {
        if (!controller.HasCaptureSession)
            controller.StartCaptureSession();

        var baseline = controller.CaptureJoystickAxes.GetValueOrDefault(axisIndex);
        var current = controller.CurrentJoystickAxes.GetValueOrDefault(axisIndex);
        return Math.Abs(current - baseline) >= DirectionalAxisDeltaThreshold;
    }

    private static bool TryCaptureGenericHat(ControllerRuntimeState controller, out string binding)
    {
        binding = string.Empty;

        if (!controller.HasCaptureSession)
            controller.StartCaptureSession();

        for (var hatIndex = 0; hatIndex < controller.JoystickHatCount; hatIndex++)
        {
            var baseline = controller.CaptureJoystickHats.GetValueOrDefault(hatIndex);
            var current = controller.CurrentJoystickHats.GetValueOrDefault(hatIndex);
            var newlyPressedDirection = GetNewlyPressedHatDirection(baseline, current);
            if (newlyPressedDirection == null)
                continue;

            binding = $"Hat {hatIndex} {newlyPressedDirection}";
            return true;
        }

        return false;
    }

    private static string? GetNewlyPressedHatDirection(SDL.JoystickHat baseline, SDL.JoystickHat current)
    {
        if (HasNewHatDirection(baseline, current, SDL.JoystickHat.Up))
            return "N";

        if (HasNewHatDirection(baseline, current, SDL.JoystickHat.Down))
            return "S";

        if (HasNewHatDirection(baseline, current, SDL.JoystickHat.Left))
            return "W";

        if (HasNewHatDirection(baseline, current, SDL.JoystickHat.Right))
            return "E";

        return null;
    }

    private static bool HasNewHatDirection(SDL.JoystickHat baseline, SDL.JoystickHat current, SDL.JoystickHat direction) =>
        (current & direction) == direction && (baseline & direction) != direction;

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

    private sealed class ControllerRuntimeState
    {
        private ControllerRuntimeState(uint instanceId, int dolphinDeviceIndex, IntPtr handle, bool isGamepad)
        {
            InstanceId = instanceId;
            DolphinDeviceIndex = dolphinDeviceIndex;
            IsGamepad = isGamepad;
            if (isGamepad)
                GamepadHandle = handle;
            else
                JoystickHandle = handle;
        }

        public uint InstanceId { get; }
        public bool IsGamepad { get; }
        public IntPtr GamepadHandle { get; }
        public IntPtr JoystickHandle { get; }
        public int DolphinDeviceIndex { get; set; }
        public int JoystickAxisCount { get; private set; }
        public int JoystickButtonCount { get; private set; }
        public int JoystickHatCount { get; private set; }
        public Dictionary<SDL.GamepadButton, bool> CurrentButtons { get; } = new();
        public Dictionary<SDL.GamepadButton, bool> PreviousButtons { get; } = new();
        public Dictionary<SDL.GamepadAxis, short> CurrentAxes { get; } = new();
        public Dictionary<SDL.GamepadAxis, short> PreviousAxes { get; } = new();
        public Dictionary<SDL.GamepadButton, bool> CaptureButtons { get; } = new();
        public Dictionary<SDL.GamepadAxis, short> CaptureAxes { get; } = new();
        public Dictionary<int, bool> CurrentJoystickButtons { get; } = new();
        public Dictionary<int, bool> PreviousJoystickButtons { get; } = new();
        public Dictionary<int, short> CurrentJoystickAxes { get; } = new();
        public Dictionary<int, short> PreviousJoystickAxes { get; } = new();
        public Dictionary<int, SDL.JoystickHat> CurrentJoystickHats { get; } = new();
        public Dictionary<int, SDL.JoystickHat> PreviousJoystickHats { get; } = new();
        public Dictionary<int, bool> CaptureJoystickButtons { get; } = new();
        public Dictionary<int, short> CaptureJoystickAxes { get; } = new();
        public Dictionary<int, SDL.JoystickHat> CaptureJoystickHats { get; } = new();
        public string DeviceExpression => $"SDL/{DolphinDeviceIndex}/{DisplayName}";
        public SDL.GamepadType ControllerType => IsGamepad ? SDL.GetGamepadType(GamepadHandle) : SDL.GamepadType.Unknown;
        public string DisplayName =>
            IsGamepad ? SDL.GetGamepadName(GamepadHandle) ?? "Gamepad" : SDL.GetJoystickName(JoystickHandle) ?? "Controller";
        public string Subtitle => IsGamepad ? DescribeControllerType(ControllerType) : "Generic SDL controller";
        public bool HasCaptureSession { get; private set; }

        public static ControllerRuntimeState ForGamepad(uint instanceId, int dolphinDeviceIndex, IntPtr gamepadHandle) =>
            new(instanceId, dolphinDeviceIndex, gamepadHandle, isGamepad: true);

        public static ControllerRuntimeState ForJoystick(uint instanceId, int dolphinDeviceIndex, IntPtr joystickHandle) =>
            new(instanceId, dolphinDeviceIndex, joystickHandle, isGamepad: false);

        public void Close()
        {
            if (IsGamepad)
                SDL.CloseGamepad(GamepadHandle);
            else
                SDL.CloseJoystick(JoystickHandle);
        }

        public void StartCaptureSession()
        {
            CaptureButtons.Clear();
            CaptureAxes.Clear();
            CaptureJoystickButtons.Clear();
            CaptureJoystickAxes.Clear();
            CaptureJoystickHats.Clear();

            foreach (var button in CapturableButtons)
                CaptureButtons[button] = CurrentButtons.GetValueOrDefault(button);

            foreach (var axis in CapturableAxes)
                CaptureAxes[axis] = CurrentAxes.GetValueOrDefault(axis);

            for (var buttonIndex = 0; buttonIndex < JoystickButtonCount; buttonIndex++)
                CaptureJoystickButtons[buttonIndex] = CurrentJoystickButtons.GetValueOrDefault(buttonIndex);

            for (var axisIndex = 0; axisIndex < JoystickAxisCount; axisIndex++)
                CaptureJoystickAxes[axisIndex] = CurrentJoystickAxes.GetValueOrDefault(axisIndex);

            for (var hatIndex = 0; hatIndex < JoystickHatCount; hatIndex++)
                CaptureJoystickHats[hatIndex] = CurrentJoystickHats.GetValueOrDefault(hatIndex);

            HasCaptureSession = true;
        }

        public void ClearCaptureSession()
        {
            CaptureButtons.Clear();
            CaptureAxes.Clear();
            CaptureJoystickButtons.Clear();
            CaptureJoystickAxes.Clear();
            CaptureJoystickHats.Clear();
            HasCaptureSession = false;
        }

        public void Refresh()
        {
            if (!IsGamepad)
            {
                RefreshJoystick();
                return;
            }

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

        private void RefreshJoystick()
        {
            JoystickButtonCount = Math.Max(0, SDL.GetNumJoystickButtons(JoystickHandle));
            JoystickAxisCount = Math.Max(0, SDL.GetNumJoystickAxes(JoystickHandle));
            JoystickHatCount = Math.Max(0, SDL.GetNumJoystickHats(JoystickHandle));

            for (var buttonIndex = 0; buttonIndex < JoystickButtonCount; buttonIndex++)
            {
                PreviousJoystickButtons[buttonIndex] = CurrentJoystickButtons.GetValueOrDefault(buttonIndex);
                CurrentJoystickButtons[buttonIndex] = SDL.GetJoystickButton(JoystickHandle, buttonIndex);
            }

            for (var axisIndex = 0; axisIndex < JoystickAxisCount; axisIndex++)
            {
                PreviousJoystickAxes[axisIndex] = CurrentJoystickAxes.GetValueOrDefault(axisIndex);
                CurrentJoystickAxes[axisIndex] = SDL.GetJoystickAxis(JoystickHandle, axisIndex);
            }

            for (var hatIndex = 0; hatIndex < JoystickHatCount; hatIndex++)
            {
                PreviousJoystickHats[hatIndex] = CurrentJoystickHats.GetValueOrDefault(hatIndex);
                CurrentJoystickHats[hatIndex] = (SDL.JoystickHat)SDL.GetJoystickHat(JoystickHandle, hatIndex);
            }
        }
    }
}
