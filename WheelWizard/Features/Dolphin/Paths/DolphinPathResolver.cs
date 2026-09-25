using System.IO.Abstractions;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Dolphin.Paths;

public interface IDolphinPathResolver
{
    DolphinPathLayout Resolve(string dolphinLocation, string userFolderPath);
}

// Settings supply values to this resolver; the resolver never reads or resolves settings itself.
public sealed class DolphinPathResolver(IFileSystem fileSystem, IRuntimeEnvironment environment) : IDolphinPathResolver
{
    public DolphinPathLayout Resolve(string dolphinLocation, string userFolderPath) =>
        new(fileSystem.Path, environment, IsFlatpakSandboxed(), dolphinLocation, userFolderPath);

    private bool IsFlatpakSandboxed() =>
        environment.IsLinux
        && fileSystem.File.Exists("/.flatpak-info")
        && !string.IsNullOrWhiteSpace(environment.GetEnvironmentVariable("FLATPAK_ID"));
}
