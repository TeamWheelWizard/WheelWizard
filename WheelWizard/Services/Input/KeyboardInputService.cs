using Avalonia.Input;
using SDL3;

namespace WheelWizard.Services.Input;

public static class KeyboardInputService
{
    private const string WindowsKeyboardDeviceExpression = "DInput/0/Keyboard Mouse";
    private const string LinuxKeyboardDeviceExpression = "XInput2/0/Keyboard Mouse";
    private const string MacKeyboardDeviceExpression = "Quartz/0/Keyboard & Mouse";

    public static string GetKeyboardDeviceExpression()
    {
        if (OperatingSystem.IsWindows())
            return WindowsKeyboardDeviceExpression;

        if (OperatingSystem.IsMacOS())
            return MacKeyboardDeviceExpression;

        return LinuxKeyboardDeviceExpression;
    }

    public static ControllerDeviceOption CreateKeyboardOption() =>
        new(
            0,
            0,
            "Keyboard",
            "Keyboard-only layout",
            GetKeyboardDeviceExpression(),
            SDL.GamepadType.Unknown,
            IsConnected: true,
            IsKeyboard: true
        );

    public static bool IsKeyboardDevice(string? deviceExpression) =>
        !string.IsNullOrWhiteSpace(deviceExpression) && deviceExpression.Contains("Keyboard", StringComparison.OrdinalIgnoreCase);

    public static string CreateAbsoluteBinding(string token) =>
        MarioKartInputConfigService.WrapToken($"{GetKeyboardDeviceExpression()}:{token}");

    public static bool TryCreateBinding(Key key, out string binding)
    {
        binding = string.Empty;

        if (!TryGetDolphinKeyToken(key, out var token))
            return false;

        binding = CreateAbsoluteBinding(token);
        return true;
    }

    public static bool TryGetDolphinKeyToken(Key key, out string token)
    {
        token = key.ToString().ToUpperInvariant();

        var keyName = key.ToString();

        if (keyName.Length == 1 && char.IsLetter(keyName[0]))
        {
            token = keyName.ToUpperInvariant();
            return true;
        }

        if (keyName.StartsWith("D", StringComparison.Ordinal) && keyName.Length == 2 && char.IsDigit(keyName[1]))
        {
            token = keyName[1].ToString();
            return true;
        }

        if (
            keyName.StartsWith("F", StringComparison.Ordinal)
            && int.TryParse(keyName[1..], out var functionNumber)
            && functionNumber is >= 1 and <= 24
        )
        {
            token = keyName.ToUpperInvariant();
            return true;
        }

        if (
            keyName.StartsWith("NumPad", StringComparison.Ordinal)
            && int.TryParse(keyName["NumPad".Length..], out var numpadDigit)
            && numpadDigit is >= 0 and <= 9
        )
        {
            token = $"NUMPAD{numpadDigit}";
            return true;
        }

        token = key switch
        {
            Key.Return or Key.Enter => "RETURN",
            Key.Space => "SPACE",
            Key.Tab => "TAB",
            Key.Escape => "ESCAPE",
            Key.Back => "BACK",
            Key.Delete => "DELETE",
            Key.Insert => "INSERT",
            Key.Home => "HOME",
            Key.End => "END",
            Key.PageUp => "PRIOR",
            Key.PageDown => "NEXT",
            Key.CapsLock => "CAPITAL",
            Key.NumLock => "NUMLOCK",
            Key.Scroll => "SCROLL",
            Key.Pause => "PAUSE",
            Key.PrintScreen => "SYSRQ",
            Key.Left => "LEFT",
            Key.Right => "RIGHT",
            Key.Up => "UP",
            Key.Down => "DOWN",
            Key.LeftShift => "LSHIFT",
            Key.RightShift => "RSHIFT",
            Key.LeftCtrl => "LCONTROL",
            Key.RightCtrl => "RCONTROL",
            Key.LeftAlt => "LMENU",
            Key.RightAlt => "RMENU",
            Key.LWin => "LWIN",
            Key.RWin => "RWIN",
            Key.Apps => "APPS",
            Key.Multiply => "MULTIPLY",
            Key.Add => "ADD",
            Key.Subtract => "SUBTRACT",
            Key.Decimal => "DECIMAL",
            Key.Divide => "DIVIDE",
            Key.OemMinus => "MINUS",
            Key.OemPlus => "EQUALS",
            Key.OemComma => "COMMA",
            Key.OemPeriod => "PERIOD",
            Key.OemSemicolon or Key.Oem1 => "SEMICOLON",
            Key.OemQuestion or Key.Oem2 => "SLASH",
            Key.OemTilde or Key.Oem3 => "GRAVE",
            Key.OemOpenBrackets or Key.Oem4 => "LBRACKET",
            Key.OemPipe or Key.Oem5 or Key.OemBackslash or Key.Oem102 => "BACKSLASH",
            Key.OemCloseBrackets or Key.Oem6 => "RBRACKET",
            Key.OemQuotes or Key.Oem7 => "APOSTROPHE",
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(token);
    }
}
