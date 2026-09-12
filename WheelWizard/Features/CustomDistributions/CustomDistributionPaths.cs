using System.IO.Abstractions;
using WheelWizard.ApplicationData;
using WheelWizard.Dolphin.Paths;

namespace WheelWizard.CustomDistributions;

public interface ICustomDistributionPaths
{
    string RootFolderPath { get; }
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
    // Both frontends share this installation. Recomp-only setups use launcher-owned data until
    // a Dolphin user folder or valid custom Load folder has been configured.
    public string RootFolderPath =>
        dolphin.HasCustomLoadFolder || !string.IsNullOrWhiteSpace(dolphin.UserFolderPath)
            ? fileSystem.Path.Combine(dolphin.LoadFolderPath, "Riivolution", "WheelWizard")
            : fileSystem.Path.Combine(applicationData.DirectoryPath, "RetroRewind");
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
