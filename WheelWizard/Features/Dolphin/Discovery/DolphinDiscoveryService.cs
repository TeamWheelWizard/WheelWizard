using System.IO.Abstractions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Dolphin.Discovery;

public interface IDolphinDiscoveryService
{
    string? FindUserDirectory(string dolphinLocation, string currentUserDirectory);
    string? FindApplication();
}

public sealed class DolphinDiscoveryService(
    IFileSystem fileSystem,
    IRuntimeEnvironment environment,
    IDolphinPathResolver paths,
    IDolphinRegistrySettings registry
) : IDolphinDiscoveryService
{
    public string? FindUserDirectory(string dolphinLocation, string currentUserDirectory)
    {
        var layout = paths.Resolve(dolphinLocation, currentUserDirectory);
        var portableDirectory = FindPortableDirectory(layout);
        if (!string.IsNullOrWhiteSpace(portableDirectory))
            return portableDirectory;

        if (environment.IsWindows)
        {
            var registryPath = registry.UserConfigPath;
            if (!string.IsNullOrWhiteSpace(registryPath) && fileSystem.Directory.Exists(registryPath))
                return registryPath.Replace(fileSystem.Path.AltDirectorySeparatorChar, fileSystem.Path.DirectorySeparatorChar);

            var documents = fileSystem.Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Dolphin Emulator");
            if (fileSystem.Directory.Exists(documents))
                return documents;

            var appData = fileSystem.Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dolphin Emulator");
            if (fileSystem.Directory.Exists(appData))
                return appData;

            var adjacentUserDirectory = PortableDirectory(layout);
            if (fileSystem.Directory.Exists(adjacentUserDirectory))
                return adjacentUserDirectory;
        }
        else if (environment.IsMacOS)
        {
            var library = fileSystem.Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dolphin");
            if (fileSystem.Directory.Exists(library))
                return library;
        }
        else if (environment.IsLinux)
        {
            var flatpakDirectory = fileSystem.Directory.Exists(layout.LinuxDolphinFlatpakAppDataFolderPath)
                ? layout.LinuxDolphinFlatpakDataDir
                : null;
            // Sandboxed Wheel Wizard prefers a discovered Flatpak installation; outside the sandbox,
            // the selected Dolphin command determines whether Flatpak or native data is appropriate.
            return
                layout.IsFlatpakSandboxed() && flatpakDirectory != null
                || !layout.IsFlatpakSandboxed() && layout.IsFlatpakDolphinFilePath(dolphinLocation)
                ? flatpakDirectory
                : FindNativeLinuxDirectory(layout);
        }

        return null;
    }

    public string? FindApplication()
    {
        if (!environment.IsMacOS)
            return null;

        var app = fileSystem.Path.Combine("Dolphin.app", "Contents", "MacOS", "Dolphin");
        var systemInstall = fileSystem.Path.Combine("/Applications", app);
        if (fileSystem.File.Exists(systemInstall))
            return systemInstall;

        var userInstall = fileSystem.Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", app);
        return fileSystem.File.Exists(userInstall) ? userInstall : null;
    }

    private string PortableDirectory(DolphinPathLayout layout) =>
        fileSystem.Path.Combine(layout.GetDolphinExeDirectory(), environment.IsLinux ? "user" : "User");

    private string? FindPortableDirectory(DolphinPathLayout layout)
    {
        if (layout.IsFlatpakSandboxed())
            return null;

        if (environment.IsLinux)
        {
            // Dolphin also recognizes an embedded user directory in the current working directory.
            var embedded = fileSystem.Path.GetFullPath("user");
            if (fileSystem.Directory.Exists(embedded))
                return embedded;
        }

        var portable = PortableDirectory(layout);
        if (
            fileSystem.File.Exists(fileSystem.Path.Combine(layout.GetDolphinExeDirectory(), "portable.txt"))
            || environment.IsWindows && registry.UseLocalUserDirectory
        )
            return fileSystem.Directory.Exists(portable) ? portable : null;

        return null;
    }

    private string? FindNativeLinuxDirectory(DolphinPathLayout layout)
    {
        if (fileSystem.Directory.Exists(layout.LinuxDolphinLegacyFolderPath))
            return layout.LinuxDolphinLegacyFolderPath;

        if (layout.IsFlatpakSandboxed())
        {
            if (
                fileSystem.Directory.Exists(layout.LinuxHostXdgConfigHome)
                && fileSystem.Directory.Exists(layout.LinuxHostXdgDataHome)
                && fileSystem.Directory.Exists(layout.LinuxDolphinHostNativeInstallConfigDir)
                && fileSystem.Directory.Exists(layout.LinuxDolphinHostNativeInstallDataDir)
            )
                return layout.LinuxDolphinHostNativeInstallDataDir;
        }
        else if (
            fileSystem.Directory.Exists(layout.LinuxDolphinNativeInstallConfigDir)
            && fileSystem.Directory.Exists(layout.LinuxDolphinNativeInstallDataDir)
        )
            return layout.LinuxDolphinNativeInstallDataDir;

        return null;
    }
}
