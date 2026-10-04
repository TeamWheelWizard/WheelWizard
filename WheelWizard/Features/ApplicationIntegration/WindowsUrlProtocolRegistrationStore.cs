using Microsoft.Win32;

namespace WheelWizard.ApplicationIntegration;

public sealed class WindowsUrlProtocolRegistrationStore : IUrlProtocolRegistrationStore
{
    public UrlProtocolRegistrationState? Read(string scheme)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        using var key = Registry.CurrentUser.OpenSubKey($@"SOFTWARE\Classes\{scheme}");
        if (key == null)
            return null;
        using var command = key.OpenSubKey(@"shell\open\command");
        return new(command?.GetValue("") as string, key.GetValue("URL Protocol") != null);
    }

    public void Write(string scheme, string command)
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var key = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\Classes\{scheme}");
        key.SetValue("", $"URL:{scheme} Protocol");
        key.SetValue("URL Protocol", "");
        using var commandKey = key.CreateSubKey(@"shell\open\command");
        commandKey.SetValue("", command);
    }
}
