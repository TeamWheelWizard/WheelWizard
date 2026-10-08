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
    private bool IsMacOS => environment.IsMacOS;

    // Linux and macOS both run a setup that keeps its products, Config.toml and NAND in its own backend folder.
    private bool UsesBackendFolder => IsLinux || IsMacOS;
    private bool IsFlatpak => environment.IsFlatpakSandboxed(fileSystem);
    public string RootFolderPath => fileSystem.Path.Combine(applicationData.DirectoryPath, "Recomp");
    public string InstallFolderPath => fileSystem.Path.Combine(RootFolderPath, "Install");
    public bool IsPortableInstall => !UsesBackendFolder;
    public string CacheFolderPath => fileSystem.Path.Combine(RootFolderPath, "Cache");
    public string UserDataFolderPath => fileSystem.Path.Combine(RootFolderPath, "UserData");
    public string InstallStateFilePath => fileSystem.Path.Combine(InstallFolderPath, InstallStateFileName);
    public string SetupFilePath =>
        IsFlatpak
            ? "/app/extensions/backends/wiicompiled/bin/wiicompiled-setup"
            : fileSystem.Path.Combine(
                InstallFolderPath,
                IsLinux ? "WiiCompiled-Setup.AppImage"
                    : IsMacOS ? RecompPlatform.MacReleaseAssetName
                    : RecompSetupCommandBuilder.SetupFileName
            );
    public string LinuxBackendFolderPath =>
        fileSystem.Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WiiCompiled");
    public string LinuxBackendStateFilePath => fileSystem.Path.Combine(LinuxBackendFolderPath, InstallStateFileName);
    public string ConfigFilePath => fileSystem.Path.Combine(UsesBackendFolder ? LinuxBackendFolderPath : UserDataFolderPath, "Config.toml");
    public string NandCopyFolderPath => fileSystem.Path.Combine(RootFolderPath, "Nand");
    public string PortableMarkerFilePath => fileSystem.Path.Combine(RootFolderPath, "portable.txt");
    public string PrivateNandFolderPath => fileSystem.Path.Combine(UsesBackendFolder ? LinuxBackendFolderPath : UserDataFolderPath, "NAND");
}
