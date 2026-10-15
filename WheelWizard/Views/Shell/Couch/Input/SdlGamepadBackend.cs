using System.Runtime.InteropServices;

namespace WheelWizard.Views.Shell.Couch.Input;

[Flags]
internal enum CouchButtons
{
    None = 0,
    Up = 1,
    Down = 2,
    Left = 4,
    Right = 8,
    Confirm = 16,
    Back = 32,
    PreviousSection = 64,
    NextSection = 128,
    Menu = 256,
    TextInput = 512,
}

/// <summary>SDL owns device mappings/hotplug, not the application window or keyboard.</summary>
internal sealed class SdlGamepadBackend : IDisposable
{
    private const uint GamepadSubsystem = 0x2000;
    private readonly Dictionary<uint, nint> _devices = [];
    private readonly List<uint> _disconnected = [];
    public int Generation { get; private set; }
    private bool _initialized;
    private CouchButtons _pendingPresses;
    public int DeviceCount => _devices.Count;

    public SdlGamepadBackend()
    {
        // SDL has no video window: Avalonia remains the authority on foreground input.
        // Sampling in the background lets the controller require a neutral state on return.
        SDL_SetMainReady();
        SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        if (!SDL_InitSubSystem(GamepadSubsystem))
            throw new InvalidOperationException(Marshal.PtrToStringUTF8(SDL_GetError()) ?? "SDL gamepad initialization failed.");
        _initialized = true;
        try
        {
            OpenConnectedDevices();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private unsafe void OpenConnectedDevices()
    {
        var ids = SDL_GetGamepads(out var count);
        if (ids == null)
            return;
        try
        {
            for (var i = 0; i < count; i++)
                Open(ids[i]);
        }
        finally
        {
            SDL_free((nint)ids);
        }
    }

    private void Open(uint id)
    {
        if (_devices.ContainsKey(id))
            return;
        var device = SDL_OpenGamepad(id);
        if (device != 0)
        {
            _devices.Add(id, device);
            Generation++;
        }
    }

    public CouchButtons Read(bool consumePresses = true)
    {
        SDL_PumpEvents();
        // SDL_Event is a 128-byte union; device id follows type/reserved/timestamp.
        // Only consume joystick/gamepad events, leaving unrelated SDL clients alone.
        while (SDL_PeepEvents(out var e, 1, 2, 0x600, 0x6ff) > 0)
        {
            if (e.Type == 0x653) // SDL_EVENT_GAMEPAD_ADDED
                Open(e.DeviceId);
            else if (e.Type == 0x654 && _devices.Remove(e.DeviceId, out var removed))
            {
                SDL_CloseGamepad(removed);
                Generation++;
            }
            else if (e.Type == 0x651 && _devices.ContainsKey(e.DeviceId)) // SDL_EVENT_GAMEPAD_BUTTON_DOWN
                _pendingPresses |= MapButton(e.Button);
        }

        var buttons = CouchButtons.None;
        foreach (var (id, device) in _devices)
        {
            if (!SDL_GamepadConnected(device))
            {
                _disconnected.Add(id);
                continue;
            }
            if (SDL_GetGamepadButton(device, 0))
                buttons |= CouchButtons.Confirm;
            if (SDL_GetGamepadButton(device, 1) || SDL_GetGamepadButton(device, 4))
                buttons |= CouchButtons.Back;
            if (SDL_GetGamepadButton(device, 3))
                buttons |= CouchButtons.TextInput;
            if (SDL_GetGamepadButton(device, 6))
                buttons |= CouchButtons.Menu;
            if (SDL_GetGamepadButton(device, 9))
                buttons |= CouchButtons.PreviousSection;
            if (SDL_GetGamepadButton(device, 10))
                buttons |= CouchButtons.NextSection;
            if (SDL_GetGamepadButton(device, 11))
                buttons |= CouchButtons.Up;
            if (SDL_GetGamepadButton(device, 12))
                buttons |= CouchButtons.Down;
            if (SDL_GetGamepadButton(device, 13))
                buttons |= CouchButtons.Left;
            if (SDL_GetGamepadButton(device, 14))
                buttons |= CouchButtons.Right;
            var x = SDL_GetGamepadAxis(device, 0);
            var y = SDL_GetGamepadAxis(device, 1);
            // Radial intent: a diagonal stick selects one axis instead of racing two moves.
            if (Math.Max(Math.Abs((int)x), Math.Abs((int)y)) > 16000)
            {
                if (Math.Abs((int)x) > Math.Abs((int)y))
                    buttons |= x < 0 ? CouchButtons.Left : CouchButtons.Right;
                else
                    buttons |= y < 0 ? CouchButtons.Up : CouchButtons.Down;
            }
        }
        foreach (var id in _disconnected)
        {
            SDL_CloseGamepad(_devices[id]);
            _devices.Remove(id);
            Generation++;
        }
        _disconnected.Clear();
        buttons |= _pendingPresses;
        if (consumePresses)
            _pendingPresses = CouchButtons.None;
        return buttons;
    }

    private static CouchButtons MapButton(byte button) =>
        button switch
        {
            0 => CouchButtons.Confirm,
            1 or 4 => CouchButtons.Back,
            3 => CouchButtons.TextInput,
            6 => CouchButtons.Menu,
            9 => CouchButtons.PreviousSection,
            10 => CouchButtons.NextSection,
            11 => CouchButtons.Up,
            12 => CouchButtons.Down,
            13 => CouchButtons.Left,
            14 => CouchButtons.Right,
            _ => CouchButtons.None,
        };

    public void Dispose()
    {
        foreach (var device in _devices.Values)
            SDL_CloseGamepad(device);
        _devices.Clear();
        if (_initialized)
        {
            SDL_QuitSubSystem(GamepadSubsystem);
            _initialized = false;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct SdlEvent
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(16)]
        public uint DeviceId;

        [FieldOffset(20)]
        public byte Button;
    }

    // The NuGet package distributes SDL3.dll/libSDL3.so/libSDL3.dylib for desktop RIDs.
    // Keep this deliberately small ABI surface independent of generated binding APIs.
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_SetMainReady();

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_InitSubSystem(uint flags);

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_QuitSubSystem(uint flags);

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetHint(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value
    );

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint SDL_GetError();

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe uint* SDL_GetGamepads(out int count);

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_free(nint memory);

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint SDL_OpenGamepad(uint id);

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_CloseGamepad(nint device);

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_PumpEvents();

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_PeepEvents(out SdlEvent events, int count, int action, uint minimum, uint maximum);

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GamepadConnected(nint device);

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_GetGamepadButton(nint device, int button);

    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    private static extern short SDL_GetGamepadAxis(nint device, int axis);
}
