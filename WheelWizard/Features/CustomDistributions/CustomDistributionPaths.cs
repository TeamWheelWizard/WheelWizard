using System.IO.Abstractions;
using WheelWizard.ApplicationData;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Shared.IO;

namespace WheelWizard.CustomDistributions;

public interface ICustomDistributionPaths
{
    /// <summary>Where Retro Rewind lives right now: the managed folder, or a legacy install that has not been moved yet.</summary>
    string RootFolderPath { get; }

    /// <summary>The single Wheel Wizard-owned Retro Rewind folder inside the data folder, for every frontend.</summary>
    string ManagedRootFolderPath { get; }

    /// <summary>
    /// The pre-onboarding location inside Dolphin's Load folder, or <see langword="null"/> when no Dolphin
    /// folder is configured. It stays active until <see cref="IRetroRewindMigrationService"/> has moved it.
    /// </summary>
    string? LegacyRootFolderPath { get; }

    /// <summary>Whether a legacy install still holds Retro Rewind and is therefore the active root.</summary>
    bool IsLegacyRootActive { get; }

    /// <summary>
    /// Whether "no legacy install" can be trusted right now: there is no Dolphin folder at all, or the folder that would
    /// hold the legacy install can be read. An unplugged drive or an unreachable share cannot rule one out.
    /// </summary>
    bool CanRuleOutLegacyRoot { get; }

    /// <summary>
    /// Written into the managed root once the startup migration moved a legacy install there or found none,
    /// so a Retro Rewind in some Dolphin's Load folder is never picked up after that.
    /// </summary>
    string MigratedMarkerFilePath { get; }

    string RetroRewindFolderPath { get; }
    string BetaFolderPath { get; }
    string PatchesFolderPath { get; }
    string BetaPatchesFolderPath { get; }
    string SaveFolderPath { get; }
    string XmlFolderPath { get; }
    string XmlFilePath { get; }
    string BetaXmlFilePath { get; }
    string LaunchJsonFilePath { get; }
    string DownloadFolderPath { get; }
    string RetroRewindArchivePath { get; }
    string BetaDownloadFolderPath { get; }
    string BetaArchivePath { get; }
    string BetaManifestFilePath { get; }
}

public sealed class CustomDistributionPaths(IApplicationDataLocation applicationData, IDolphinPaths dolphin, IFileSystem fileSystem)
    : ICustomDistributionPaths
{
    public const string MigratedMarkerFileName = ".wheelwizard-moved";

    // Both frontends share this installation. The launch descriptor passes the root to Dolphin explicitly,
    // so it does not have to live in Dolphin's Load folder; older installs did, and keep working from
    // there until the startup migration has moved them.
    public string RootFolderPath => IsLegacyRootActive ? LegacyRootFolderPath! : ManagedRootFolderPath;
    public string ManagedRootFolderPath => fileSystem.Path.Combine(applicationData.DirectoryPath, "RetroRewind");
    public string MigratedMarkerFilePath => fileSystem.Path.Combine(ManagedRootFolderPath, MigratedMarkerFileName);

    public string? LegacyRootFolderPath =>
        dolphin.HasCustomLoadFolder || !string.IsNullOrWhiteSpace(dolphin.UserFolderPath)
            ? fileSystem.Path.Combine(dolphin.LoadFolderPath, "Riivolution", "WheelWizard")
            : null;

    public bool IsLegacyRootActive
    {
        get
        {
            var legacy = LegacyRootFolderPath;
            if (legacy is null || fileSystem.Path.PathsEqual(legacy, ManagedRootFolderPath))
                return false;
            try
            {
                return !fileSystem.File.Exists(MigratedMarkerFilePath)
                    && fileSystem.Directory.Exists(legacy)
                    && fileSystem.Directory.EnumerateFileSystemEntries(legacy).Any();
            }
            catch
            {
                return false;
            }
        }
    }

    public bool CanRuleOutLegacyRoot
    {
        get
        {
            // Nothing usable is set. That only rules a legacy install out when nothing is configured at all: a
            // Dolphin or Load folder that was chosen but is not reachable right now (an unplugged drive) may hold it.
            if (LegacyRootFolderPath is null)
                return string.IsNullOrWhiteSpace(dolphin.ConfiguredUserFolderPath)
                    && string.IsNullOrWhiteSpace(dolphin.ConfiguredLoadFolderPath);
            // A custom Load folder that is configured but missing falls back to the user folder's Load folder,
            // which would wrongly look empty.
            if (!dolphin.HasCustomLoadFolder && !string.IsNullOrWhiteSpace(dolphin.ConfiguredLoadFolderPath))
                return false;
            if (!IsReadable(dolphin.HasCustomLoadFolder ? dolphin.LoadFolderPath : dolphin.UserFolderPath))
                return false;
            // A legacy folder that is there but cannot be listed (wrong owner, a flaky share) looks empty to
            // IsLegacyRootActive; ruling it out would hide that install and its saves for good.
            return !SafeDirectoryExists(LegacyRootFolderPath) || IsReadable(LegacyRootFolderPath);
        }
    }

    private bool SafeDirectoryExists(string folder)
    {
        try
        {
            return fileSystem.Directory.Exists(folder);
        }
        catch
        {
            return true;
        }
    }

    private bool IsReadable(string folder)
    {
        try
        {
            if (!fileSystem.Directory.Exists(folder))
                return false;
            _ = fileSystem.Directory.EnumerateFileSystemEntries(folder).Any();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string RetroRewindFolderPath => fileSystem.Path.Combine(RootFolderPath, "RetroRewind6");
    public string BetaFolderPath => fileSystem.Path.Combine(RootFolderPath, "RRBeta");
    public string PatchesFolderPath => fileSystem.Path.Combine(RetroRewindFolderPath, "Patches");
    public string BetaPatchesFolderPath => fileSystem.Path.Combine(BetaFolderPath, "Patches");
    public string SaveFolderPath => fileSystem.Path.Combine(RootFolderPath, "riivolution", "save", "RetroWFC");
    public string XmlFolderPath => fileSystem.Path.Combine(RootFolderPath, "riivolution");
    public string XmlFilePath => fileSystem.Path.Combine(XmlFolderPath, "RetroRewind6.xml");
    public string BetaXmlFilePath => fileSystem.Path.Combine(XmlFolderPath, "RRBeta.xml");
    public string LaunchJsonFilePath => fileSystem.Path.Combine(applicationData.DirectoryPath, "RR.json");
    public string DownloadFolderPath => fileSystem.Path.Combine(applicationData.DirectoryPath, "Mods", "Temp");
    public string RetroRewindArchivePath => fileSystem.Path.Combine(DownloadFolderPath, "RetroRewind.zip");
    public string BetaDownloadFolderPath => fileSystem.Path.Combine(DownloadFolderPath, "RRBetaTemp");
    public string BetaArchivePath => fileSystem.Path.Combine(BetaDownloadFolderPath, "Testers.zip");
    public string BetaManifestFilePath => fileSystem.Path.Combine(applicationData.DirectoryPath, "RRBeta.manifest.json");
}
