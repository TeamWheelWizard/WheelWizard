using Microsoft.Win32;

namespace WheelWizard.Dolphin.Discovery;

public interface IDolphinRegistrySettings
{
    bool UseLocalUserDirectory { get; }
    string? UserConfigPath { get; }
}

public sealed class DolphinRegistrySettings : IDolphinRegistrySettings
{
    public bool UseLocalUserDirectory => ReadValue("LocalUserConfig") is "1" or 1 or 1L;
    public string? UserConfigPath => ReadValue("UserConfigPath") as string;

    private static object? ReadValue(string name)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Dolphin Emulator");
            return key?.GetValue(name);
        }
        catch
        {
            return null;
        }
    }
}
