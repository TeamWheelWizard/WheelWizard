using System.IO.Abstractions;

namespace WheelWizard.Shared.Platform;

public static class RuntimeEnvironmentExtensions
{
    public static bool IsFlatpakSandboxed(this IRuntimeEnvironment environment, IFileSystem fileSystem) =>
        environment.IsLinux
        && fileSystem.File.Exists("/.flatpak-info")
        && !string.IsNullOrWhiteSpace(environment.GetEnvironmentVariable("FLATPAK_ID"));

    public static bool IsMissingDolphinFlatpakExtension(this IRuntimeEnvironment environment, IFileSystem fileSystem) =>
        IsFlatpakSandboxed(environment, fileSystem)
        && !fileSystem.File.Exists("/app/extensions/backends/dolphin-emu/bin/dolphin-emu-wrapper");

    public static bool IsMissingRecompFlatpakExtension(this IRuntimeEnvironment environment, IFileSystem fileSystem) =>
        IsFlatpakSandboxed(environment, fileSystem)
        && !fileSystem.File.Exists("/app/extensions/backends/wiicompiled/bin/wiicompiled-setup");
}
