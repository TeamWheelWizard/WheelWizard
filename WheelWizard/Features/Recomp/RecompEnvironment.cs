using System.IO.Abstractions;
using WheelWizard.Services;

namespace WheelWizard.Recomp;

/// <summary>
/// Every path the recomp needs, sourced from WheelWizard's existing configuration.
/// WheelWizard never asks the user for these again; the recomp is handed what the user already set up.
/// </summary>
public interface IRecompEnvironment
{
    /// <summary>The user's Mario Kart Wii disc image.</summary>
    string GameFilePath { get; }

    /// <summary>The directory the recomp is installed into.</summary>
    string InstallFolderPath { get; }

    /// <summary>
    /// Whether <see cref="InstallFolderPath"/> is the portable location, which is what decides whether
    /// a fresh install is asked to lay itself out portably.
    /// </summary>
    bool IsPortableInstall { get; }

    /// <summary>Where downloaded setup executables are cached between runs.</summary>
    string CacheFolderPath { get; }

    /// <summary>The backend-owned runtime user state (Config.toml, private NAND, caches) beside the install directory.</summary>
    string UserDataFolderPath { get; }

    /// <summary>The <c>portable.txt</c> marker that makes the recomp folder a portable root.</summary>
    string PortableMarkerFilePath { get; }

    /// <summary>The <c>install-state.json</c> Wheel Wizard reads to know which setup release is installed.</summary>
    string InstallStateFilePath { get; }

    /// <summary>
    /// The backend's own <c>install-state.json</c>. On Windows the setup writes the same file Wheel Wizard
    /// reads, so this equals <see cref="InstallStateFilePath"/>. On Linux the AppImage keeps its own record,
    /// in its own schema, in its own directory, and Wheel Wizard writes <see cref="InstallStateFilePath"/> itself.
    /// </summary>
    string BackendStateFilePath { get; }

    /// <summary>The launcher copy of the setup executable inside the install directory.</summary>
    string InstalledSetupFilePath { get; }

    /// <summary>
    /// The <c>RetroRewind6</c> directory handed to the backend as <c>--retro-dir</c>, or
    /// <see langword="null"/> when WheelWizard has not installed Retro Rewind yet.
    /// </summary>
    string? RetroRewindFolderPath { get; }

    /// <summary>The Wheel Wizard-owned copy of the Dolphin NAND, whether or not it exists yet.</summary>
    string NandCopyFolderPath { get; }
}

/// <inheritdoc />
public sealed class RecompEnvironment(IFileSystem fileSystem) : IRecompEnvironment
{
    public string GameFilePath => PathManager.GameFilePath;

    public string InstallFolderPath => PathManager.RecompInstallFolderPath;

    public bool IsPortableInstall => PathManager.IsRecompInstallPortable;

    public string CacheFolderPath => PathManager.RecompCacheFolderPath;

    public string UserDataFolderPath => PathManager.RecompUserDataFolderPath;

    public string PortableMarkerFilePath => PathManager.RecompPortableMarkerFilePath;

    public string InstallStateFilePath => PathManager.RecompInstallStateFilePath;

    public string BackendStateFilePath => PathManager.RecompInstallStateFilePath;

    public string InstalledSetupFilePath => PathManager.RecompSetupFilePath;

    public string? RetroRewindFolderPath => ExistingFolderOrNull(PathManager.RetroRewind6FolderPath);

    public string NandCopyFolderPath => PathManager.RecompNandCopyFolderPath;

    private string? ExistingFolderOrNull(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return fileSystem.Directory.Exists(path) ? path : null;
    }
}

/// <summary>
/// The Linux layout. The AppImage owns <see cref="PathManager.RecompLinuxBackendFolderPath"/> (products,
/// state, Config.toml, workspace); Wheel Wizard's own <c>Recomp</c> folder only holds the download cache,
/// the installed copy of the AppImage and the state file Wheel Wizard writes about it.
/// </summary>
public sealed class RecompLinuxEnvironment(IFileSystem fileSystem) : IRecompEnvironment
{
    public string GameFilePath => PathManager.GameFilePath;

    public string InstallFolderPath => PathManager.RecompLinuxBackendFolderPath;

    public bool IsPortableInstall => false;

    public string CacheFolderPath => PathManager.RecompCacheFolderPath;

    // Config.toml, logs and the private NAND all live in the backend folder on Linux.
    public string UserDataFolderPath => PathManager.RecompLinuxBackendFolderPath;

    public string PortableMarkerFilePath => PathManager.RecompPortableMarkerFilePath;

    public string InstallStateFilePath => PathManager.RecompInstallStateFilePath;

    public string BackendStateFilePath => PathManager.RecompLinuxBackendStateFilePath;

    public string InstalledSetupFilePath => PathManager.RecompSetupFilePath;

    public string? RetroRewindFolderPath =>
        fileSystem.Directory.Exists(PathManager.RetroRewind6FolderPath) ? PathManager.RetroRewind6FolderPath : null;

    public string NandCopyFolderPath => PathManager.RecompNandCopyFolderPath;
}
