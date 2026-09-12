using System.IO.Abstractions;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Platform;

namespace WheelWizard.ApplicationData;

public sealed class ApplicationDataDirectories
{
    public string DefaultDirectoryPath { get; }
    public string OverrideFilePath { get; }

    public ApplicationDataDirectories(IFileSystem fileSystem, IRuntimeEnvironment environment)
    {
        var sandboxed = environment.IsFlatpakSandboxed(fileSystem);
        var portable = !sandboxed && fileSystem.File.Exists("portable-ww.txt");
        var baseDirectory = portable ? string.Empty : environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DefaultDirectoryPath = fileSystem.Path.NormalizePath(fileSystem.Path.Combine(baseDirectory, "CT-MKWII"));
        OverrideFilePath = fileSystem.Path.Combine(baseDirectory, "wheelwizard-appdata-location");
    }
}
