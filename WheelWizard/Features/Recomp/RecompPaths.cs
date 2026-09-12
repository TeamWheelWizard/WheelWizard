using System.IO.Abstractions;
using WheelWizard.ApplicationData;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Recomp;

public interface IRecompPaths
{
    string RootFolderPath { get; }
    string InstallFolderPath { get; }
    bool IsPortableInstall { get; }
    string CacheFolderPath { get; }
    string UserDataFolderPath { get; }
    string InstallStateFilePath { get; }
    string SetupFilePath { get; }
    string LinuxBackendFolderPath { get; }
    string LinuxBackendStateFilePath { get; }
    string ConfigFilePath { get; }
    string NandCopyFolderPath { get; }
    string PortableMarkerFilePath { get; }
    string PrivateNandFolderPath { get; }
}

// These locations depend on application data and platform only, so settings can use them without a cycle.
public sealed class RecompPaths(IApplicationDataLocation applicationData, IFileSystem fileSystem, IRuntimeEnvironment environment)
    : IRecompPaths
{
    public const string InstallStateFileName = "install-state.json";
    private bool IsLinux => environment.IsLinux && RecompPlatform.LinuxReleaseAssetName(environment.OSArchitecture) is not null;

    public string RootFolderPath => fileSystem.Path.Combine(applicationData.DirectoryPath, "Recomp");
    public string InstallFolderPath => fileSystem.Path.Combine(RootFolderPath, "Install");
    public bool IsPortableInstall => !IsLinux;
    public string CacheFolderPath => fileSystem.Path.Combine(RootFolderPath, "Cache");
    public string UserDataFolderPath => fileSystem.Path.Combine(RootFolderPath, "UserData");
    public string InstallStateFilePath => fileSystem.Path.Combine(InstallFolderPath, InstallStateFileName);
    public string SetupFilePath =>
        fileSystem.Path.Combine(InstallFolderPath, IsLinux ? "WiiCompiled-Setup.AppImage" : RecompSetupCommandBuilder.SetupFileName);
    public string LinuxBackendFolderPath =>
        fileSystem.Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WiiCompiled");
    public string LinuxBackendStateFilePath => fileSystem.Path.Combine(LinuxBackendFolderPath, InstallStateFileName);
    public string ConfigFilePath => fileSystem.Path.Combine(IsLinux ? LinuxBackendFolderPath : UserDataFolderPath, "Config.toml");
    public string NandCopyFolderPath => fileSystem.Path.Combine(RootFolderPath, "Nand");
    public string PortableMarkerFilePath => fileSystem.Path.Combine(RootFolderPath, "portable.txt");
    public string PrivateNandFolderPath => fileSystem.Path.Combine(IsLinux ? LinuxBackendFolderPath : UserDataFolderPath, "NAND");
}
