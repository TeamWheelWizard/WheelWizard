using System.IO.Abstractions;

namespace WheelWizard.Shared.Platform;

public static class RuntimeEnvironmentExtensions
{
    public static bool IsFlatpakSandboxed(this IRuntimeEnvironment environment, IFileSystem fileSystem) =>
        environment.IsLinux
        && fileSystem.File.Exists("/.flatpak-info")
        && !string.IsNullOrWhiteSpace(environment.GetEnvironmentVariable("FLATPAK_ID"));
}
