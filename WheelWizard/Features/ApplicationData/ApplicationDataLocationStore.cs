using System.IO.Abstractions;
using Microsoft.Win32;
using WheelWizard.Shared.IO;

namespace WheelWizard.ApplicationData;

public interface IApplicationDataLocationStore
{
    string? Load();
    void Save(string? location);
}

public sealed class FileApplicationDataLocationStore(IFileSystem fileSystem, ApplicationDataDirectories directories)
    : IApplicationDataLocationStore
{
    public string? Load() =>
        fileSystem.File.Exists(directories.OverrideFilePath) ? fileSystem.File.ReadAllText(directories.OverrideFilePath) : null;

    public void Save(string? location)
    {
        if (location is null)
            fileSystem.File.Delete(directories.OverrideFilePath);
        else
            fileSystem.WriteAllTextCreatingDirectory(directories.OverrideFilePath, location);
    }
}

public sealed class WindowsApplicationDataLocationStore : IApplicationDataLocationStore
{
    private const string KeyPath = @"Software\\WheelWizard";
    private const string ValueName = "AppDataLocation";

    public string? Load()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        return key?.GetValue(ValueName) as string;
    }

    public void Save(string? location)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        if (location is null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (key is null)
                throw new IOException("Could not open the Wheel Wizard registry key.");
            key.SetValue(ValueName, location, RegistryValueKind.String);
        }
    }
}
